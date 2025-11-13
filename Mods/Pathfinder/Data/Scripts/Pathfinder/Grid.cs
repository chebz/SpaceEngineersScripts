using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Render;
using VRage.Utils;
using VRageRender;
using VRage.Game;
using Pathfinder;

namespace Pathfinder
{
    public class Grid
    {
        private readonly Dictionary<Vector3I, PathNode> _nodes = new Dictionary<Vector3I, PathNode>();
        private readonly List<PathNode> _pathNodesDebug = new List<PathNode>();
        private readonly double _nodeSize;
        private readonly double _maxDistance = 1000.0; // 1km max distance
        private readonly int _maxNodesPerFrame = 10;
        private readonly int _gridSize;
        private Vector3I _currentGridCenter;
        private readonly double _nodeDiagonal;
        private PathNode _startNode;
        private PathNode _endNode;
        
        // Pathfinding state for multi-frame calculation
        private bool _isPathfinding = false;
        private bool _isPathCalculated = false;
        private Vector3D _pathfindingStart;
        private Vector3D _pathfindingEnd;
        private List<Vector3D> _calculatedPath = new List<Vector3D>();
        private List<Vector3D> _partialPath = new List<Vector3D>();
        private Vector3I _bestNodeSoFar = Vector3I.Zero;
        private HashSet<Vector3I> _openSet = new HashSet<Vector3I>();
        private HashSet<Vector3I> _closedSet = new HashSet<Vector3I>();
        private Dictionary<Vector3I, Vector3I> _cameFrom = new Dictionary<Vector3I, Vector3I>();
        private int _nodesExplored = 0;
        private const int _maxTotalNodes = 10000;

        public double NodeSize { get { return _nodeSize; } }
        public int GridSize { get { return _gridSize; } }
        public bool IsPathCalculated { get { return _isPathCalculated; } }
        public List<Vector3D> CalculatedPath { get { return _calculatedPath; } }
        public List<Vector3D> PartialPath { get { return _partialPath; } }

        // Static directions array - initialized once for all instances
        private static readonly Vector3I[] Directions = new Vector3I[]
        {
            // Cardinal directions (cost 1)
            new Vector3I(1, 0, 0),   // +X
            new Vector3I(-1, 0, 0),  // -X
            new Vector3I(0, 1, 0),   // +Y
            new Vector3I(0, -1, 0),  // -Y
            new Vector3I(0, 0, 1),   // +Z
            new Vector3I(0, 0, -1),  // -Z
            
            // Diagonal directions (cost 1.4, but only if intermediate nodes are clear)
            new Vector3I(1, 1, 0),   // +X +Y
            new Vector3I(-1, 1, 0),  // -X +Y
            new Vector3I(1, -1, 0),  // +X -Y
            new Vector3I(-1, -1, 0), // -X -Y
            new Vector3I(1, 0, 1),   // +X +Z
            new Vector3I(-1, 0, 1),  // -X +Z
            new Vector3I(1, 0, -1),  // +X -Z
            new Vector3I(-1, 0, -1), // -X -Z
            new Vector3I(0, 1, 1),   // +Y +Z
            new Vector3I(0, -1, 1),  // -Y +Z
            new Vector3I(0, 1, -1),  // +Y -Z
            new Vector3I(0, -1, -1), // -Y -Z
            
            // 3D diagonal directions (cost 1.7, but only if all intermediate nodes are clear)
            new Vector3I(1, 1, 1),   // +X +Y +Z
            new Vector3I(-1, 1, 1),  // -X +Y +Z
            new Vector3I(1, -1, 1),  // +X -Y +Z
            new Vector3I(-1, -1, 1), // -X -Y +Z
            new Vector3I(1, 1, -1),  // +X +Y -Z
            new Vector3I(-1, 1, -1), // -X +Y -Z
            new Vector3I(1, -1, -1), // +X -Y -Z
            new Vector3I(-1, -1, -1) // -X -Y -Z
        };

        private readonly IMyCubeGrid _ownGrid;

        public Grid(double nodeSize, IMyCubeGrid ownGrid)
        {
            _nodeSize = Math.Ceiling(nodeSize); // Round up to integer
            _gridSize = (int)Math.Ceiling(_maxDistance / _nodeSize);
            _ownGrid = ownGrid;
            _nodeDiagonal = Math.Sqrt(3.0 * _nodeSize * _nodeSize); // Diagonal of a cube
        }

        public Vector3I WorldToGrid(Vector3D worldPosition)
        {
            var relativePosition = worldPosition - new Vector3D(_currentGridCenter.X, _currentGridCenter.Y, _currentGridCenter.Z);
            return new Vector3I(
                (int)Math.Round(relativePosition.X / _nodeSize),
                (int)Math.Round(relativePosition.Y / _nodeSize),
                (int)Math.Round(relativePosition.Z / _nodeSize)
            );
        }

        public Vector3D GridToWorld(Vector3I gridPosition)
        {
            var relativePosition = new Vector3D(
                gridPosition.X * _nodeSize,
                gridPosition.Y * _nodeSize,
                gridPosition.Z * _nodeSize
            );
            return new Vector3D(_currentGridCenter.X, _currentGridCenter.Y, _currentGridCenter.Z) + relativePosition;
        }

        public bool IsNodeObstacle(Vector3I gridPosition)
        {
            // Check bounds
            if (Math.Abs(gridPosition.X) > _gridSize || 
                Math.Abs(gridPosition.Y) > _gridSize || 
                Math.Abs(gridPosition.Z) > _gridSize)
            {
                return true; // Out of bounds is considered obstacle
            }

            var node = GetOrCreateNode(gridPosition);
            return node.IsObstacle;
        }

        public PathNode GetOrCreateNode(Vector3I gridPosition)
        {
            PathNode existingNode;
            if (_nodes.TryGetValue(gridPosition, out existingNode))
            {
                return existingNode;
            }

            return AddNode(gridPosition);
        }


        public void ClearObstacles()
        {
            _nodes.Clear();
        }

        private PathNode AddNode(Vector3I gridPosition)
        {
            // Convert grid position to world position
            var worldPosition = GridToWorld(gridPosition);
            
            // Create a small bounding box around this node
            var nodeSize = new Vector3D(_nodeSize, _nodeSize, _nodeSize);
            var box = new BoundingBoxD(worldPosition - nodeSize / 2, worldPosition + nodeSize / 2);
            
            var entitiesInNode = new List<MyEntity>();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref box, entitiesInNode);

            bool hasObstacle = false;

            foreach (var entity in entitiesInNode)
            {
                // Check for voxel maps
                var voxelMap = entity as MyVoxelMap;
                if (voxelMap != null)
                {
                    // Check if there are any voxel cells in this node
                    var vol = voxelMap.GetVoxelContentInBoundingBox_Fast(box, MatrixD.Identity);
                    // If there are any voxels at all, it's an obstacle
                    if (vol.Item2 > 0)
                    {
                        hasObstacle = true;
                        break;
                    }
                }

                var cubeGrid = entity as IMyCubeGrid;
                if (cubeGrid != null) 
                {
                    if (_ownGrid != null && cubeGrid == _ownGrid)
                        continue;

                    var slimBlocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(slimBlocks);

                    foreach (var slimBlock in slimBlocks)
                    {
                        var blockOBB = GetBlockOBB(slimBlock);
                        if (blockOBB.Contains(ref box) != ContainmentType.Disjoint)
                        {
                            hasObstacle = true;
                            break;
                        }
                    }
                }
            }

            // Create the node with obstacle status
            var node = new PathNode(gridPosition);
            node.IsObstacle = hasObstacle;
            _nodes[gridPosition] = node;
            // _pathNodesDebug.Add(node);
            // if (_pathNodesDebug.Count > 10)
            // {
            //     _pathNodesDebug.RemoveAt(0);
            // }
            return node;
        }

        private static MyOrientedBoundingBoxD GetBlockOBB(IMySlimBlock block)
        {
            Vector3D worldCtr;
            block.ComputeWorldCenter(out worldCtr);
            var myGrid = (MyCubeGrid)block.CubeGrid;
            var halfExtents = (block.Max + 1 - block.Min) * myGrid.GridSizeHalf;
            var matrix = block.CubeGrid.PositionComp.WorldMatrixRef;
            matrix.Translation = worldCtr;
            return new MyOrientedBoundingBoxD(new BoundingBoxD(-halfExtents, halfExtents), matrix);
        }

        public void StartPathfinding(Vector3D start, Vector3D end)
        {
            // Compute new grid center to rounded start position
            var newGridCenter = new Vector3I(
                (int)Math.Round(start.X),
                (int)Math.Round(start.Y),
                (int)Math.Round(start.Z)
            );
            
            // Check if grid center has changed and clean up if necessary
            _currentGridCenter = newGridCenter;
            _nodes.Clear();
            _pathNodesDebug.Clear();

            
            // Initialize pathfinding state
            _isPathfinding = true;
            _isPathCalculated = false;
            _pathfindingStart = start;
            _pathfindingEnd = end;
            _calculatedPath.Clear();
            _partialPath.Clear();
            _bestNodeSoFar = Vector3I.Zero;
            _openSet.Clear();
            _closedSet.Clear();
            _cameFrom.Clear();
            _nodesExplored = 0;
            
            var startGrid = WorldToGrid(start);
            var endGrid = WorldToGrid(end);
            
            // Clamp end position to grid bounds
            var clampedEndGrid = ClampToGridBounds(endGrid);
            
            // If start and end are the same, mark as complete
            if (startGrid == clampedEndGrid)
            {
                _isPathfinding = false;
                _isPathCalculated = true;
                return;
            }
            
            // Initialize A* pathfinding
            _startNode = new PathNode(startGrid, 0, Vector3I.DistanceManhattan(startGrid, clampedEndGrid));
            _nodes[_startNode.position] = _startNode;
            _openSet.Add(_startNode.position);
        }

        public void Update()
        {
            if (!_isPathfinding)
            {
                return;
            }
            if (_isPathCalculated)
            {
                Utils.ShowHudMessage("Pathfinding not started or already calculated");
                return;
            }

            Utils.ShowHudMessage($"Pathfinding open: {_openSet.Count}, closed: {_closedSet.Count}, nodes explored: {_nodesExplored}");

            var endGrid = WorldToGrid(_pathfindingEnd);
            var clampedEndGrid = ClampToGridBounds(endGrid);
            
            int nodesProcessed = 0;
            
            while (_openSet.Count > 0 && nodesProcessed < _maxNodesPerFrame && _nodesExplored < _maxTotalNodes)
            {
                // Find node with lowest fCost
                Vector3I current = Vector3I.Zero;
                int lowestFCost = int.MaxValue;
                foreach (var nodePos in _openSet)
                {
                    var node = GetOrCreateNode(nodePos);
                    if (node.fCost < lowestFCost)
                    {
                        lowestFCost = (int)node.fCost;
                        current = nodePos;
                    }
                }

                _openSet.Remove(current);
                _closedSet.Add(current);
                _nodesExplored++;
                nodesProcessed++;

                // Check if we've reached the destination
                if (current == clampedEndGrid)
                {
                    _calculatedPath = ReconstructPath(_cameFrom, current, true);
                    _isPathfinding = false;
                    _isPathCalculated = true;
                    Utils.ShowHudMessage($"Pathfinding reached destination in {_nodesExplored} nodes");
                    return;
                }

                // Check if we're close enough to the destination (within 1 node)
                if (Vector3I.DistanceManhattan(current, clampedEndGrid) <= 1)
                {
                    _calculatedPath = ReconstructPath(_cameFrom, current, true);
                    _isPathfinding = false;
                    _isPathCalculated = true;
                    Utils.ShowHudMessage($"Pathfinding reached destination in {_nodesExplored} nodes");
                    return;
                }

                // Explore neighbors
                foreach (var direction in Directions)
                {
                    var neighbor = current + direction;
                    
                    // Skip if out of bounds
                    if (Math.Abs(neighbor.X) > _gridSize || 
                        Math.Abs(neighbor.Y) > _gridSize || 
                        Math.Abs(neighbor.Z) > _gridSize)
                        continue;
                    
                    // Skip if already processed
                    if (_closedSet.Contains(neighbor))
                        continue;
                    
                    // Skip if obstacle
                    if (IsNodeObstacle(neighbor))
                        continue;
                    
                    var neighborNode = GetOrCreateNode(neighbor);
                    var tentativeGCost = GetOrCreateNode(current).gCost + GetMovementCost(direction);
                    
                    if (!_openSet.Contains(neighbor))
                    {
                        _openSet.Add(neighbor);
                    }
                    else if (tentativeGCost >= neighborNode.gCost)
                    {
                        continue;
                    }
                    
                    // Update neighbor
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.hCost = Vector3I.DistanceManhattan(neighbor, clampedEndGrid);
                    _cameFrom[neighbor] = current;
                }
            }

            // Update best node so far for partial path visualization
            if (_cameFrom.Count > 0)
            {
                Vector3I closestNode = Vector3I.Zero;
                int closestDistance = int.MaxValue;
                
                foreach (var nodePos in _closedSet)
                {
                    int distance = Vector3I.DistanceManhattan(nodePos, clampedEndGrid);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestNode = nodePos;
                    }
                }
                
                // Only update partial path if we found a better node
                if (closestNode != _bestNodeSoFar)
                {
                    _bestNodeSoFar = closestNode;
                    _partialPath = ReconstructPath(_cameFrom, closestNode, false);
                }
            }

            // If we hit the node limit, finalize the path
            if (_nodesExplored >= _maxTotalNodes && _cameFrom.Count > 0)
            {
                _calculatedPath = ReconstructPath(_cameFrom, _bestNodeSoFar, true);
                _partialPath.Clear(); // Clear partial path after completion
                _isPathfinding = false;
                _isPathCalculated = true;
                Utils.ShowHudMessage($"Pathfinding node limit reached in {_nodesExplored} nodes");
                return;
            }
            
            // If no more nodes to explore, no path found
            if (_openSet.Count == 0)
            {
                _partialPath.Clear(); // Clear partial path
                _isPathfinding = false;
                _isPathCalculated = true;
                Utils.ShowHudMessage($"Pathfinding no more nodes to explore in {_nodesExplored} nodes");
            }
        }

        private double GetMovementCost(Vector3I direction)
        {
            int distance = Math.Abs(direction.X) + Math.Abs(direction.Y) + Math.Abs(direction.Z);
            
            switch (distance)
            {
                case 1: return 1.0;      // Cardinal movement
                case 2: return 1.4;      // 2D diagonal (√2 ≈ 1.4)
                case 3: return 1.7;      // 3D diagonal (√3 ≈ 1.7)
                default: return 1.0;
            }
        }

        private Vector3I ClampToGridBounds(Vector3I gridPosition)
        {
            return new Vector3I(
                Math.Max(-_gridSize, Math.Min(_gridSize, gridPosition.X)),
                Math.Max(-_gridSize, Math.Min(_gridSize, gridPosition.Y)),
                Math.Max(-_gridSize, Math.Min(_gridSize, gridPosition.Z))
            );
        }

        private List<Vector3D> ReconstructPath(Dictionary<Vector3I, Vector3I> cameFrom, Vector3I current, bool optimize)
        {
            var path = new List<Vector3D>();
            path.Add(GridToWorld(current));

            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Add(GridToWorld(current));
            }

            path.Reverse();
            if (optimize)
                OptimizePath(path);
            return path;
        }

        private void OptimizePath(List<Vector3D> path)
        {
            if (path.Count < 3)
                return; // Can't optimize paths with less than 3 points

            // First reduce redundant nodes
            ReducePath(path);
            
            var maxAttempts = 100;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                if (!SmoothPath(path))
                    break;
                attempts++;
            }
            Utils.ShowHudMessage($"OptimizePath: Attempts: {attempts}");
        }

        private void ReducePath(List<Vector3D> path)
        {
            if (path.Count < 3)
                return;

            var optimizedPath = new List<Vector3D>();
            optimizedPath.Add(path[0]); // Always include start point

            int i = 0;
            while (i < path.Count - 1)
            {
                var currentPos = path[i];
                var nextPos = path[i + 1];
                var direction = GetDirection(currentPos, nextPos);
                
                // Find how many consecutive nodes are in the same direction
                int count = 1;
                int j = i + 1;
                
                while (j < path.Count - 1)
                {
                    var currentDirection = GetDirection(path[j], path[j + 1]);
                    if (currentDirection == direction)
                    {
                        count++;
                        j++;
                    }
                    else
                    {
                        break;
                    }
                }
                
                // Add the optimized segment
                if (count == 1)
                {
                    // Single step, just add the next point
                    optimizedPath.Add(path[i + 1]);
                }
                else
                {
                    // Multiple steps in same direction, add the final point
                    optimizedPath.Add(path[i + count]);
                }
                
                i += count;
            }

            // Apply changes if we actually removed nodes
            if (optimizedPath.Count < path.Count)
            {
                path.Clear();
                path.AddRange(optimizedPath);
            }
        }

        private bool SmoothPath(List<Vector3D> path)
        {
            if (path.Count < 3)
            {
                return false;
            }

            var smoothedPath = new List<Vector3D>();
            smoothedPath.Add(path[0]); // Always include start point

            int startIndex = 0;
            int segmentsCreated = 0;
            
            while (startIndex < path.Count - 1)
            {
                int endIndex = path.Count - 1; // Start with the last node
                int attempts = 0;
                bool foundClearPath = false;
                
                // Try to find the furthest point we can reach with a clear line
                while (endIndex > startIndex + 1)
                {
                    attempts++;
                    
                    if (IsLineClear(path[startIndex], path[endIndex]))
                    {
                        // Found a clear path, use this end point
                        smoothedPath.Add(path[endIndex]);
                        startIndex = endIndex;
                        segmentsCreated++;
                        foundClearPath = true;
                        break;
                    }
                    else
                    {
                        endIndex--;
                    }
                }
                
                // If we couldn't find a clear path, just move to the next node
                if (!foundClearPath)
                {
                    smoothedPath.Add(path[startIndex + 1]);
                    startIndex++;
                }
            }

            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"SmoothPath: Created {segmentsCreated} segments");

            // Apply changes if we actually removed nodes
            if (smoothedPath.Count < path.Count)
            {
                path.Clear();
                path.AddRange(smoothedPath);
                return true;
            }
            return false;
        }

        private bool IsLineClear(Vector3D start, Vector3D end)
        {
            // Convert to grid coordinates
            var startGrid = WorldToGrid(start);
            var endGrid = WorldToGrid(end);
            
            // Get all grid positions that the line intersects
            var intersectingNodes = GetLineIntersectingNodes(startGrid, endGrid);
            
            // Check each intersecting node for obstacles
            foreach (var nodePos in intersectingNodes)
            {
                var node = GetOrCreateNode(nodePos);
                if (node.IsObstacle)
                {
                    return false;
                }
            }
            
            return true;
        }

        private List<Vector3I> GetLineIntersectingNodes(Vector3I start, Vector3I end)
        {
            var nodes = new List<Vector3I>();
            
            // Convert grid positions back to world coordinates
            var startWorld = GridToWorld(start);
            var endWorld = GridToWorld(end);
            
            // Get all nodes within node diagonal distance of the line
            var lineDirection = endWorld - startWorld;
            var lineLength = lineDirection.Length();
            
            if (lineLength < 0.001) // Very short line, just check start node
            {
                nodes.Add(start);
                return nodes;
            }
            
            lineDirection.Normalize();
            
            // Create a bounding box around the line with node diagonal padding
            var min = new Vector3D(
                Math.Min(startWorld.X, endWorld.X) - _nodeDiagonal,
                Math.Min(startWorld.Y, endWorld.Y) - _nodeDiagonal,
                Math.Min(startWorld.Z, endWorld.Z) - _nodeDiagonal
            );
            var max = new Vector3D(
                Math.Max(startWorld.X, endWorld.X) + _nodeDiagonal,
                Math.Max(startWorld.Y, endWorld.Y) + _nodeDiagonal,
                Math.Max(startWorld.Z, endWorld.Z) + _nodeDiagonal
            );
            
            // Find all grid positions in the bounding box
            var minGrid = WorldToGrid(min);
            var maxGrid = WorldToGrid(max);
            
            for (int x = minGrid.X; x <= maxGrid.X; x++)
            {
                for (int y = minGrid.Y; y <= maxGrid.Y; y++)
                {
                    for (int z = minGrid.Z; z <= maxGrid.Z; z++)
                    {
                        var nodePos = new Vector3I(x, y, z);
                        var nodeWorld = GridToWorld(nodePos);
                        
                        // Check if this node is within node diagonal distance of the line
                        if (IsPointNearLine(startWorld, endWorld, nodeWorld, _nodeDiagonal / 2))
                        {
                            nodes.Add(nodePos);
                        }
                    }
                }
            }
            
            return nodes;
        }
        
        private bool IsPointNearLine(Vector3D lineStart, Vector3D lineEnd, Vector3D point, double maxDistance)
        {
            var lineDirection = lineEnd - lineStart;
            var lineLength = lineDirection.Length();
            
            if (lineLength < 0.001) // Very short line
            {
                return (point - lineStart).Length() <= maxDistance;
            }
            
            lineDirection.Normalize();
            
            // Project point onto line
            var pointToLineStart = point - lineStart;
            var projectionLength = Vector3D.Dot(pointToLineStart, lineDirection);
            
            // Clamp projection to line segment
            projectionLength = Math.Max(0, Math.Min(projectionLength, lineLength));
            
            // Find closest point on line
            var closestPointOnLine = lineStart + lineDirection * projectionLength;
            
            // Check distance from point to closest point on line
            return (point - closestPointOnLine).Length() <= maxDistance;
        }


        public void Draw()
        {
            // Draw spheres for all intersecting nodes from the last IsLineClear call
            foreach (var node in _nodes.Values)
            {
                // Draw green sphere at node position
                var matrix = MatrixD.CreateTranslation(GridToWorld(node.position));
                var colorYellow = Color.Yellow;
                var colorRed = Color.Red;
                var color = node.IsObstacle ? colorRed : colorYellow;
                color.A = 10;

                MySimpleObjectDraw.DrawTransparentSphere(
                    ref matrix,
                    (float)_nodeSize / 2.0f,
                    ref color,
                    MySimpleObjectRasterizer.Solid,
                    12,
                    null,
                    null,
                    -1,
                    -1,
                    null,
                    MyBillboard.BlendTypeEnum.Standard,
                    1f
                );
            }
        }

        private Vector3I GetDirection(Vector3D from, Vector3D to)
        {
            var diff = to - from;
            var gridDiff = WorldToGrid(to) - WorldToGrid(from);
            
            // Normalize the direction to a unit vector
            if (gridDiff.X != 0) gridDiff.X = Math.Sign(gridDiff.X);
            if (gridDiff.Y != 0) gridDiff.Y = Math.Sign(gridDiff.Y);
            if (gridDiff.Z != 0) gridDiff.Z = Math.Sign(gridDiff.Z);
            
            return gridDiff;
        }

        public class PathNode
        {
            public Vector3I position;
            public double gCost;
            public double hCost;
            public double fCost { get { return gCost + hCost; } }
            public bool IsObstacle { get; set; }

            public PathNode(Vector3I position, double gCost, double hCost)
            {
                this.position = position;
                this.gCost = gCost;
                this.hCost = hCost;
                IsObstacle = false;
            }

            public PathNode(Vector3I position)
            {
                this.position = position;
                IsObstacle = false;
            }
        }
    }
}

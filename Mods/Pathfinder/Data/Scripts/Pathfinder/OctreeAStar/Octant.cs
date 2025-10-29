using VRageMath;
using System.Collections.Generic;
using System;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace Pathfinder.OctreeAStar
{
    public class Octant
    {
        public enum OctantState
        {
            Unexplored,
            FullObstacle,
            PartialObstacle,
            Empty
        }

        private readonly Graph _graph;
        private BoundingBoxD _bounds;
        private OctantState _state = OctantState.Unexplored;
        private double _g;
        private double _h;
        private Octant _parent;
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly List<Octant> _children = new List<Octant>();

        public double F 
        {   
            get 
            { 
                var f = _g + _h;
                foreach (var child in _children)
                {
                    f = Math.Min(f, child.F);
                }
                return f;
            } 
        }

        public int Depth
        {
            get
            {
                if (_parent == null)
                {
                    return 0;
                }
                return _parent.Depth + 1;
            }
        }

        public OctantState State 
        {
            get
            {
                return _state;
            }
        }

        public Octant(BoundingBoxD bounds, Graph graph, Octant parent)
        {
            _bounds = bounds;
            _graph = graph;
            _parent = parent;
        }

        public void Explore()
        {
            if (_state != OctantState.Unexplored)
            {
                return;
            }

            var entitiesInNode = new List<MyEntity>();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref _bounds, entitiesInNode);

            foreach (var entity in entitiesInNode)
            {
                // Check for voxel maps
                var voxelMap = entity as MyVoxelMap;
                if (voxelMap != null)
                {
                    // Check if there are any voxel cells in this node
                    var vol = voxelMap.GetVoxelContentInBoundingBox_Fast(_bounds, MatrixD.Identity);
                    // If there are any voxels at all, it's an obstacle
                    if (vol.Item2 > 0)
                    {
                        _state = OctantState.FullObstacle;
                        break;
                    }
                }

                var cubeGrid = entity as IMyCubeGrid;
                if (cubeGrid != null) 
                {
                    if (cubeGrid == _graph.OwnGrid)
                        continue;

                    var slimBlocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(slimBlocks);

                    foreach (var slimBlock in slimBlocks)
                    {
                        var blockOBB = Utils.GetBlockOBB(slimBlock);
                        var contains = blockOBB.Contains(ref _bounds);
                        if (contains != ContainmentType.Disjoint)
                        {
                            _state = contains == ContainmentType.Contains ? OctantState.FullObstacle : OctantState.PartialObstacle;
                            break;
                        }
                    }
                }
            }

            if (_state == OctantState.PartialObstacle)
            {
                var size = _bounds.Size;
                if (size.Length() > _graph.AgentSize * 2)
                {
                    Subdivide();
                }
                else
                {
                    _state = OctantState.FullObstacle;
                }
            }
        }

        private void Subdivide()
        {
            var center = _bounds.Center;
            var min = _bounds.Min;
            var max = _bounds.Max;
            _children.Add(new Octant(new BoundingBoxD(min, center), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(center.X, min.Y, min.Z), new Vector3D(max.X, center.Y, center.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(min.X, center.Y, min.Z), new Vector3D(center.X, max.Y, center.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(center.X, center.Y, min.Z), new Vector3D(max.X, max.Y, center.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(min.X, min.Y, center.Z), new Vector3D(center.X, center.Y, max.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(center.X, min.Y, center.Z), new Vector3D(max.X, center.Y, max.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(new Vector3D(min.X, center.Y, center.Z), new Vector3D(center.X, max.Y, max.Z)), _graph, this));
            _children.Add(new Octant(new BoundingBoxD(center, max), _graph, this));
            foreach (var child in _children)
            {
                child._h = Vector3D.Distance(child._bounds.Center, _graph.End);
            }
        }

        public Octant GetBestChild(Vector3D pos)
        {
            if (_bounds.Contains(pos) != ContainmentType.Contains)
            {
                return null;
            }
            if (_children.Count == 0)
            {
                return this;
            }
            
            double closestDistance = double.MaxValue;
            Octant closestChild = null;
            foreach (var child in _children)
            {
                var distance = (child._bounds.Center - pos).LengthSquared();
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestChild = child;
                }
            }
            return closestChild.GetBestChild(pos);
        }
        public void Render()
        {
            MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"{_children.Count}");
            if (_children.Count > 0)
            {
                foreach (var child in _children)
                {
                    child.Render();
                }
            }
            else
            {
                var color = Color.Green;
                switch (_state)
                {
                    case OctantState.FullObstacle:
                        color = Color.Red;
                        break;
                    case OctantState.PartialObstacle:
                        color = Color.Orange;
                        break;
                    default:
                        return;
                }
                Utils.DrawAabb(_bounds, color, false);
            }
        }
    }
}
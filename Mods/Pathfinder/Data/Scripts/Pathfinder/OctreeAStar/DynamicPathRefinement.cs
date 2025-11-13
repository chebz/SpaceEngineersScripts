using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Pathfinder.OctreeAStar
{
    public class DynamicPathRefinement
    {
        private readonly Graph _graph = new Graph();
        private readonly List<MyOrientedBoundingBoxD> _velocityObstacles = new List<MyOrientedBoundingBoxD>();
        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private readonly List<MyEntity> _staticQueryResults = new List<MyEntity>();
        private Path _refinedPath;
        private string _refinedPathString = string.Empty;
        MyOrientedBoundingBoxD _pathObb;
        private int _blockedVOIndex = -1;

        public double TimeHorizon { get; set; } = 10.0;

        public Path RefinedPath => _refinedPath;

        public string RefinedPathString => _refinedPathString;

        public void Clear()
        {
            _graph.Reset();
            _refinedPath = null;
            _refinedPathString = string.Empty;
        }

        public void Update(NavigationComponent navigation)
        {
            if (navigation == null)
            {
                Clear();
                return;
            }

            var path = _graph.Path;
            if (path == null)
            {
                RenderRefinedPath();
                return;
            }

            if (path.state == Path.State.Calculating)
            {
                var iterationLimit = Math.Max(16, OctreeAStarSettings.Instance?.MaxNodesPerFrame ?? 16);
                var iterations = 0;
                while (_graph.Path != null && _graph.Path.state == Path.State.Calculating && iterations < iterationLimit)
                {
                    _graph.Update();
                    iterations++;
                }
                path = _graph.Path;
            }

            if (path == null)
            {
                Clear();
                return;
            }

            if (path.state == Path.State.Ready)
            {
                _refinedPath = path;
                var newPathString = path.ToString();
                if (!string.Equals(_refinedPathString, newPathString, StringComparison.Ordinal))
                {
                    _refinedPathString = newPathString;
                }
            }
            else if (path.state == Path.State.NoPath)
            {
                Clear();
            }

            RenderRefinedPath();
        }

        public void RefinePath(NavigationComponent navigation)
        {
            Clear();

            if (navigation == null)
            {
                return;
            }

            var navigationGraph = navigation.Graph;
            if (navigationGraph == null || navigationGraph.Path == null || navigationGraph.Path.state != Path.State.Ready)
            {
                return;
            }

            var remoteControl = navigation.RemoteControl;
            if (remoteControl == null || remoteControl.CubeGrid == null || remoteControl.CubeGrid.Physics == null)
            {
                return;
            }

            var points = navigationGraph.Path.points;
            if (points == null || points.Count < 2)
            {
                return;
            }

            var waypointIndex = navigation.CurrentWaypointIndex;
            if (waypointIndex <= 0 || waypointIndex >= points.Count)
            {
                return;
            }

            var segmentStart = points[waypointIndex - 1];
            var segmentEnd = points[waypointIndex];

            var currentPosition = remoteControl.GetPosition();

            var segmentVector = segmentEnd - segmentStart;
            var segmentLength = segmentVector.Length();
            if (segmentLength < 1e-3)
            {
                return;
            }
            var dirAlongPath = segmentVector / segmentLength;

            var projection = Vector3D.Dot(currentPosition - segmentStart, dirAlongPath);
            var clampedProjection = MathHelper.Clamp(projection, 0.0, segmentLength);
            var projectionPoint = segmentStart + dirAlongPath * clampedProjection;
            var remainingDistance = segmentLength - clampedProjection;

            var velocity = remoteControl.GetShipVelocities().LinearVelocity;
            var speed = velocity.Length();
            var predictedDistance = speed > 1e-3 ? speed * TimeHorizon : remainingDistance;
            var alongCurrentSegment = Math.Min(predictedDistance, remainingDistance);

            var option1 = projectionPoint + dirAlongPath * alongCurrentSegment;
            var option2 = segmentEnd;

            var targetPoint = Vector3D.DistanceSquared(currentPosition, option1) <= Vector3D.DistanceSquared(currentPosition, option2)
                ? option1
                : option2;

            var updatedWaypointIndex = waypointIndex;

            if (predictedDistance > remainingDistance && waypointIndex < points.Count - 1)
            {
                var nextSegmentStart = points[waypointIndex];
                var nextSegmentEnd = points[waypointIndex + 1];
                var nextSegmentVector = nextSegmentEnd - nextSegmentStart;
                var nextSegmentLength = nextSegmentVector.Length();

                if (nextSegmentLength >= 1e-3)
                {
                    var nextSegmentDir = nextSegmentVector / nextSegmentLength;
                    var extraDistance = predictedDistance - remainingDistance;
                    var clampedExtra = Math.Min(extraDistance, nextSegmentLength);
                    targetPoint = nextSegmentStart + nextSegmentDir * clampedExtra;
                }
                else
                {
                    targetPoint = nextSegmentEnd;
                }

                updatedWaypointIndex = waypointIndex + 1;
            }

            var distanceToTarget = Vector3D.Distance(currentPosition, targetPoint);
            if (distanceToTarget < 1e-2)
            {
                return;
            }

            if (updatedWaypointIndex != navigation.CurrentWaypointIndex)
            {
                navigation.SetCurrentWaypointIndex(updatedWaypointIndex);
            }

            var travelTime = speed > 1e-3 ? Math.Min(TimeHorizon, distanceToTarget / speed) : TimeHorizon;
            travelTime = Math.Max(1.0, travelTime);

            BuildDynamicObstacles(navigation, travelTime);

            // Optimization: Check if path is clear before full computation
            if (IsPathClear(currentPosition, targetPoint, navigation))
            {
                // Create simple 2-point path
                _refinedPath = new Path
                {
                    points = new List<Vector3D> { currentPosition, targetPoint },
                    state = Path.State.Ready
                };
                _refinedPathString = _refinedPath.ToString();
                return;
            }

            _graph.BeginFindPath(remoteControl, currentPosition, targetPoint, navigation.MinAltitude, navigation.MaxAltitude, true, _velocityObstacles);

            Update(navigation);
        }

        private void RenderRefinedPath()
        {
            if (_refinedPath == null)
            {
                return;
            }

            if (!OctreeAStarSettings.Instance.RenderPath)
            {
                return;
            }

            _refinedPath.Render(Color.Red, false);
            // Utils.DrawOBB(_pathObb, Color.Beige, true);
            // for (int i = 0; i < _velocityObstacles.Count; i++)
            // {
            //     var color = i == _blockedVOIndex ? Color.Red : Color.Green;
            //     Utils.DrawOBB(_velocityObstacles[i], color, true);
            // }
        }

        private void BuildDynamicObstacles(NavigationComponent navigation, double timeHorizon)
        {
            _blockedVOIndex = -1;
            _velocityObstacles.Clear();
            _entityCache.Clear();

            var dist = navigation.RemoteControl.GetShipVelocities().LinearVelocity.Length() * timeHorizon * 2;
            var min = navigation.RemoteControl.GetPosition() - new Vector3D(dist, dist, dist);
            var max = navigation.RemoteControl.GetPosition() + new Vector3D(dist, dist, dist);
            var aabb = new BoundingBoxD(min, max);

            MyAPIGateway.Entities.GetEntities(_entityCache, entity => entity is IMyCubeGrid && aabb.Intersects(entity.WorldAABB));

            var remoteGrid = navigation.RemoteControl?.CubeGrid;
            var agentRadius = navigation.Graph != null ? navigation.Graph.AgentSize * 0.5 : remoteGrid?.WorldVolume.Radius ?? 1.0;

            foreach (var entity in _entityCache)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    continue;
                }
                if (remoteGrid != null && grid == remoteGrid)
                {
                    continue;
                }
                if (Utils.IsStaticObstacle(grid))
                {
                    continue;
                }

                var velocity = (Vector3D)grid.Physics.LinearVelocity;
                var speed = velocity.Length();
                var displacement = velocity * timeHorizon;

                var halfExtents = new Vector3D(grid.WorldAABB.HalfExtents.X, grid.WorldAABB.HalfExtents.Y, grid.WorldAABB.HalfExtents.Z);
                halfExtents.Z = speed * timeHorizon * 0.5 + grid.WorldVolume.Radius;
                var forwardVector = speed > 1e-3 ? velocity / speed : (Vector3D)grid.WorldMatrix.Forward;
                var upVector = (Vector3D)grid.WorldMatrix.Up;
                if (Math.Abs(Vector3D.Dot(forwardVector, upVector)) > 0.95)
                {
                    upVector = Vector3D.CalculatePerpendicularVector(forwardVector);
                }
                upVector.Normalize();
                var quatRotation = Quaternion.CreateFromForwardUp(forwardVector, upVector);
                var pathStart = grid.WorldAABB.Center + displacement * 0.5;
                _velocityObstacles.Add(new MyOrientedBoundingBoxD(pathStart, halfExtents, quatRotation));
            }
        }

        private bool IsPathClear(Vector3D start, Vector3D end, NavigationComponent navigation)
        {
            var remoteGrid = navigation.RemoteControl?.CubeGrid;
            var agentSize = navigation.Graph.AgentSize;

            var direction = end - start;
            var length = direction.Length();

            var halfExtents = new Vector3D(agentSize * 0.5, agentSize * 0.5, agentSize * 0.5);
            
            if (length < 1e-6)
            {
                _pathObb = new MyOrientedBoundingBoxD(start, halfExtents, Quaternion.Identity);
            }
            else
            {
                halfExtents.Z = length * 0.5 + agentSize;
                var pathStart = start + direction * 0.5;
                var up = Vector3D.CalculatePerpendicularVector(direction.Normalized());
                var quatRotation = Quaternion.CreateFromForwardUp(direction.Normalized(), up);
                _pathObb = new MyOrientedBoundingBoxD(pathStart, halfExtents, quatRotation);
            }

            // Check dynamic obstacles
            for (int i = 0; i < _velocityObstacles.Count; i++)
            {
                var velocityObstacle = _velocityObstacles[i];
                if (_pathObb.Intersects(ref velocityObstacle))
                {
                    _blockedVOIndex = i;
                    return false;
                }
            }

            // Check static environment
            _staticQueryResults.Clear();
            MyGamePruningStructure.GetAllEntitiesInOBB(ref _pathObb, _staticQueryResults, MyEntityQueryType.Static);

            for (int i = 0; i < _staticQueryResults.Count; i++)
            {
                var grid = _staticQueryResults[i] as IMyCubeGrid;
                if (grid == null)
                {
                    continue;
                }

                if (remoteGrid != null && grid.EntityId == remoteGrid.EntityId)
                {
                    continue;
                }

                var slimBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(slimBlocks);

                foreach (var slimBlock in slimBlocks)
                {
                    var blockOBB = Utils.GetBlockOBB(slimBlock);
                    var contains = blockOBB.Contains(ref _pathObb);
                    if (contains != ContainmentType.Disjoint)
                    {
                        return false;
                    }
                }
            }

            _staticQueryResults.Clear();
            return true;
        }


    }
}


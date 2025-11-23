using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRageMath;
using VRageRender;
using Pathfinder.OctreeAStar;
using Sandbox.Game.Entities;
using System;
using VRage.Game.Entity;
using VRage.ModAPI;
using Sandbox.ModAPI;

namespace Pathfinder
{
    public enum CollisionAvoidanceState
    {
        NotStarted,
        Calculating,
        Ready,
        NoPath
    }

    public class CollisionAvoidance
    {
        private struct PathCheck
        {
            public Vector3D Start;
            public Vector3D End;
            public bool IsClear;

            public PathCheck(Vector3D start, Vector3D end, bool isClear)
            {
                Start = start;
                End = end;
                IsClear = isClear;
            }
        }

        private struct VoxelOBB
        {
            public MyOrientedBoundingBoxD OBB;
            public bool IsOccupied;

            public VoxelOBB(MyOrientedBoundingBoxD obb, bool isOccupied)
            {
                OBB = obb;
                IsOccupied = isOccupied;
            }
        }

        private struct SubOctant
        {
            public Vector3D Center;
            public double DistToTarget;

            public SubOctant(Vector3D center, double distToTarget)
            {
                Center = center;
                DistToTarget = distToTarget;
            }
        }

        private struct SubOctantOBB
        {
            public MyOrientedBoundingBoxD OBB;
            public bool IsClear;

            public SubOctantOBB(MyOrientedBoundingBoxD obb, bool isClear)
            {
                OBB = obb;
                IsClear = isClear;
            }
        }

        private readonly static Dictionary<long, MyOrientedBoundingBoxD> _intentObstacles = new Dictionary<long, MyOrientedBoundingBoxD>();
        private readonly List<MyOrientedBoundingBoxD> _velocityObstacles = new List<MyOrientedBoundingBoxD>();

        private MyOrientedBoundingBoxD _pathObb;
        private List<int> _blockedVOIndices = new List<int>();
        private List<long> _blockedIntentEntityIds = new List<long>();
        private List<MyEntity> _staticQueryResults = new List<MyEntity>();
        private readonly HashSet<IMyEntity> _entityCache = new HashSet<IMyEntity>();
        private readonly List<PathCheck> _pathChecks = new List<PathCheck>();
        private readonly List<VoxelOBB> _voxelOBBs = new List<VoxelOBB>();
        private readonly List<SubOctantOBB> _subOctantOBBs = new List<SubOctantOBB>();

        private CollisionAvoidanceState _state = CollisionAvoidanceState.NotStarted;

        private Vector3D _startPosition;
        private Vector3D _targetWaypoint;
        private Vector3D _closestCollisionPoint;
        private double _closestCollisionDistanceToTarget;
        private double _radiusIncrement;
        private int _maxRadiusIncrements;
        private List<double> _currentAngles;
        private Vector3D _forward;
        private Vector3D _right;
        private Vector3D _up;
        private NavigationComponent _navigation;
        private bool _isOnBaseApproach = false;

        public CollisionAvoidanceState State => _state;
        public bool IsOnBaseApproach => _isOnBaseApproach;

        public void Clear()
        {
            if (_navigation != null && _navigation.RemoteControl != null && _navigation.RemoteControl.CubeGrid != null)
            {
                var entityId = _navigation.RemoteControl.CubeGrid.EntityId;
                _intentObstacles.Remove(entityId);
                _navigation.CATarget = null;
            }
            _state = CollisionAvoidanceState.NotStarted;
            _isOnBaseApproach = false;
            _blockedVOIndices.Clear();
            _blockedIntentEntityIds.Clear();
            _staticQueryResults.Clear();
            _entityCache.Clear();
            _velocityObstacles.Clear();
            _pathChecks.Clear();
            _voxelOBBs.Clear();
            _subOctantOBBs.Clear();
            _navigation = null;
        }

        private void ResetCalculationState()
        {
            // Reset calculation state without clearing CATarget
            _state = CollisionAvoidanceState.NotStarted;
            _isOnBaseApproach = false;
            _blockedVOIndices.Clear();
            _blockedIntentEntityIds.Clear();
            _staticQueryResults.Clear();
            _entityCache.Clear();
            _velocityObstacles.Clear();
            _pathChecks.Clear();
            _voxelOBBs.Clear();
            _subOctantOBBs.Clear();
        }

        public void BuildDynamicObstacles(NavigationComponent navigation, double timeHorizon)
        {
            _blockedVOIndices.Clear();
            _blockedIntentEntityIds.Clear();
            _velocityObstacles.Clear();
            _entityCache.Clear();

            var dist = navigation.RemoteControl.GetShipVelocities().LinearVelocity.Length() * timeHorizon * 2;
            var min = navigation.RemoteControl.GetPosition() - new Vector3D(dist, dist, dist);
            var max = navigation.RemoteControl.GetPosition() + new Vector3D(dist, dist, dist);
            var aabb = new BoundingBoxD(min, max);

            MyAPIGateway.Entities.GetEntities(_entityCache, entity => entity is IMyCubeGrid && aabb.Intersects(entity.WorldAABB));

            var remoteGrid = navigation.RemoteControl?.CubeGrid;
            var agentRadius = navigation.AgentSize * 0.5;

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

        public void RefinePath(NavigationComponent navigation)
        {
            _isOnBaseApproach = false;
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

            var newTargetWaypoint = points[waypointIndex];
            var newStartPosition = remoteControl.GetPosition();
            
            // Only reset if target waypoint changed or we're not calculating
            var targetChanged = _navigation != navigation || 
                               _state != CollisionAvoidanceState.Calculating || 
                               Vector3D.Distance(_targetWaypoint, newTargetWaypoint) > 1e-2;
            
            if (targetChanged)
            {
                // Reset calculation state but preserve CATarget to avoid stuttering
                ResetCalculationState();
            }
            else if (_state == CollisionAvoidanceState.Calculating)
            {
                // Already calculating for this target, just update start position and rebuild obstacles
                _startPosition = newStartPosition;
                var distToTarget = Vector3D.Distance(_startPosition, _targetWaypoint);
                var vel = remoteControl.GetShipVelocities().LinearVelocity;
                var spd = vel.Length();
                const double TIME_H = 10.0;
                var tTime = spd > 1e-3 ? Math.Min(TIME_H, distToTarget / spd) : TIME_H;
                tTime = Math.Max(1.0, tTime);
                BuildDynamicObstacles(navigation, tTime);
                return;
            }

            _navigation = navigation;
            _startPosition = newStartPosition;
            _targetWaypoint = newTargetWaypoint;
            var distanceToTarget = Vector3D.Distance(_startPosition, _targetWaypoint);
            if (distanceToTarget < 1e-2)
            {
                _state = CollisionAvoidanceState.Ready;
                SetCATarget(navigation, _targetWaypoint);
                return;
            }

            var velocity = remoteControl.GetShipVelocities().LinearVelocity;
            var speed = velocity.Length();
            const double TIME_HORIZON = 10.0;
            var travelTime = speed > 1e-3 ? Math.Min(TIME_HORIZON, distanceToTarget / speed) : TIME_HORIZON;
            travelTime = Math.Max(1.0, travelTime);

            BuildDynamicObstacles(navigation, travelTime);
            _voxelOBBs.Clear();
            _subOctantOBBs.Clear();

            var intentDistance = PathfinderSettings.Instance.IntentDistance;
            if (distanceToTarget < intentDistance && waypointIndex + 1 < points.Count)
            {
                var nextWaypoint = points[waypointIndex + 1];
                Vector3D nextCollisionPos;
                bool dummyTerrainCollision;
                bool isNextPathClear = IsPathClear(_startPosition, nextWaypoint, navigation, out nextCollisionPos, out dummyTerrainCollision);
                
                if (isNextPathClear)
                {
                    navigation.SetCurrentWaypointIndex(waypointIndex + 1);
                    _targetWaypoint = nextWaypoint;
                    distanceToTarget = Vector3D.Distance(_startPosition, _targetWaypoint);
                }
            }

            var caDistance = PathfinderSettings.Instance.caDistance;
            
            Vector3D caTargetPoint;
            if (distanceToTarget <= caDistance)
            {
                caTargetPoint = _targetWaypoint;
            }
            else
            {
                var dirToTarget = (_targetWaypoint - _startPosition).Normalized();
                caTargetPoint = _startPosition + dirToTarget * caDistance;
            }
            
            Vector3D collisionPos;
            bool isTerrainCollision;
            bool isInitialPathClear = IsPathClear(_startPosition, caTargetPoint, navigation, out collisionPos, out isTerrainCollision);
            _pathChecks.Add(new PathCheck(_startPosition, caTargetPoint, isInitialPathClear));
            
            if (isInitialPathClear)
            {
                _state = CollisionAvoidanceState.Ready;
                _isOnBaseApproach = true;
                SetCATarget(navigation, _targetWaypoint);
                return;
            }
            
            // Path is not clear, so we're not on base approach
            _isOnBaseApproach = false;

            if (isTerrainCollision && collisionPos.LengthSquared() > 1e-6)
            {
                var planet = MyGamePruningStructure.GetClosestPlanet(_startPosition);
                if (planet != null)
                {
                    var dirToPlanet = (planet.WorldMatrix.Translation - collisionPos).Normalized();
                    var caTarget = collisionPos - dirToPlanet * PathfinderSettings.Instance.CAMinAltitude;
                    _state = CollisionAvoidanceState.Ready;
                    SetCATarget(navigation, caTarget);
                    return;
                }
            }

            // Store the first collision as closest collision point
            _closestCollisionPoint = collisionPos;
            _closestCollisionDistanceToTarget = Vector3D.Distance(_closestCollisionPoint, _targetWaypoint);
            
            var agentSize = navigation.AgentSize;
            
            var foundClearPath = TryFindClearPathToWaypoint(navigation, _targetWaypoint, _closestCollisionPoint, agentSize);
            if (foundClearPath)
            {
                return;
            }
            
            // Set up coordinate system based on collision point
            if (!UpdateCoordinateSystem(navigation))
            {
                return;
            }
            
            // Calculate total distance from current position to target
            var totalDistanceToTarget = Vector3D.Distance(_startPosition, _targetWaypoint);

            // Calculate radius increment based on agent size
            _radiusIncrement = agentSize * 0.5;
            _maxRadiusIncrements = PathfinderSettings.Instance.CANumRadiusIncrements;
            
            // Perform detour calculation
            PerformDetourCalculation(navigation, totalDistanceToTarget, agentSize);
        }
        
        private bool UpdateCoordinateSystem(NavigationComponent navigation)
        {
            // Set up coordinate system: circle is at collision point, facing remote control
            // Forward points from collision point towards remote control (start position)
            var toRemoteControl = _startPosition - _closestCollisionPoint;
            var toRemoteControlLength = toRemoteControl.Length();
            
            // If collision is at or very close to start, use direction to target instead
            if (toRemoteControlLength < 1e-6)
            {
                var toTarget = _targetWaypoint - _closestCollisionPoint;
                var toTargetLength = toTarget.Length();
                if (toTargetLength < 1e-6)
                {
                    _state = CollisionAvoidanceState.Ready;
                    _isOnBaseApproach = false;
                    SetCATarget(navigation, _targetWaypoint);
                    return false;
                }
                _forward = -toTarget / toTargetLength; // Negative because we want to point away from target (towards start)
            }
            else
            {
                _forward = toRemoteControl / toRemoteControlLength;
            }
            _right = Vector3D.CalculatePerpendicularVector(_forward);
            var rightLength = _right.Length();
            if (rightLength < 1e-6)
            {
                _right = Vector3D.UnitX;
            }
            else
            {
                _right = _right / rightLength;
            }
            
            _up = Vector3D.Cross(_forward, _right);
            var upLength = _up.Length();
            if (upLength < 1e-6)
            {
                _up = Vector3D.UnitY;
            }
            else
            {
                _up = _up / upLength;
            }
            
            return true;
        }
        
        private bool TryFindClearPathToWaypoint(NavigationComponent navigation, Vector3D targetWaypoint, Vector3D collisionPoint, double agentSize)
        {
            var sphereRadius = Vector3D.Distance(_startPosition, targetWaypoint);
            if (sphereRadius < 1e-6)
            {
                return false;
            }
            
            var direction = targetWaypoint - _startPosition;
            var directionNormalized = direction / sphereRadius;
            
            var right = Vector3D.CalculatePerpendicularVector(directionNormalized);
            var rightLength = right.Length();
            if (rightLength < 1e-6)
            {
                if (Math.Abs(Vector3D.Dot(Vector3D.UnitX, directionNormalized)) < 0.9)
                {
                    right = Vector3D.UnitX;
                }
                else if (Math.Abs(Vector3D.Dot(Vector3D.UnitY, directionNormalized)) < 0.9)
                {
                    right = Vector3D.UnitY;
                }
                else
                {
                    right = Vector3D.UnitZ;
                }
                right = right - directionNormalized * Vector3D.Dot(right, directionNormalized);
                rightLength = right.Length();
                if (rightLength > 1e-6)
                {
                    right = right / rightLength;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                right = right / rightLength;
            }
            
            var up = Vector3D.Cross(directionNormalized, right);
            var upLength = up.Length();
            if (upLength < 1e-6)
            {
                return false;
            }
            up = up / upLength;
            
            var angleIncrementDegrees = PathfinderSettings.Instance.CAAngleIncrement;
            var angleIncrementRadians = MathHelper.ToRadians(angleIncrementDegrees);
            var maxPitchRadians = Math.PI - 1e-6;
            
            for (double pitchRadians = angleIncrementRadians; pitchRadians <= maxPitchRadians; pitchRadians += angleIncrementRadians)
            {
                var cosPitch = Math.Cos(pitchRadians);
                var sinPitch = Math.Sin(pitchRadians);
                
                var azimuthSteps = (int)Math.Ceiling(2.0 * Math.PI / angleIncrementRadians);
                var azimuthStepSize = 2.0 * Math.PI / azimuthSteps;
                
                for (int azimuthStep = 0; azimuthStep < azimuthSteps; azimuthStep++)
                {
                    var azimuthRadians = azimuthStep * azimuthStepSize;
                    var cosAzimuth = Math.Cos(azimuthRadians);
                    var sinAzimuth = Math.Sin(azimuthRadians);
                    
                    var perpendicularComponent = right * (sinPitch * cosAzimuth) + up * (sinPitch * sinAzimuth);
                    var spherePoint = _startPosition + directionNormalized * (cosPitch * sphereRadius) + perpendicularComponent * sphereRadius;
                    
                    Vector3D testCollisionPos;
                    bool dummyTerrainCollision;
                    bool isTestPathClear = IsPathClear(_startPosition, spherePoint, navigation, out testCollisionPos, out dummyTerrainCollision);
                    
                    if (isTestPathClear)
                    {
                        _state = CollisionAvoidanceState.Ready;
                        _isOnBaseApproach = false;
                        SetCATarget(navigation, spherePoint);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        private void PerformDetourCalculation(NavigationComponent navigation, double totalDistanceToTarget, double agentSize)
        {
            // Perform entire calculation in one go
            for (int incrementIndex = 1; incrementIndex <= _maxRadiusIncrements; incrementIndex++)
            {
                var radius = _radiusIncrement * incrementIndex;
                CalculateAnglesForRadius(radius);
                
                foreach (var angle in _currentAngles)
                {
                    // Calculate offset around collision point
                    var offset = _right * (Math.Cos(angle) * radius) + _up * (Math.Sin(angle) * radius);
                    var offsetPoint = _closestCollisionPoint + offset;
                    
                    // Get direction from current position to offset point and extend by total distance to target
                    var dirToTarget = offsetPoint - _startPosition;
                    var dirToTargetLength = dirToTarget.Length();
                    if (dirToTargetLength > 1e-6)
                    {
                        var dirToTargetNormalized = dirToTarget / dirToTargetLength;
                        var testPoint = _startPosition + dirToTargetNormalized * totalDistanceToTarget;
                        
                        Vector3D testCollisionPos;
                        bool dummyTerrainCollision;
                        bool isTestPathClear = IsPathClear(_startPosition, testPoint, navigation, out testCollisionPos, out dummyTerrainCollision);
                        _pathChecks.Add(new PathCheck(_startPosition, testPoint, isTestPathClear));
                        
                        if (isTestPathClear)
                        {
                            _state = CollisionAvoidanceState.Ready;
                            _isOnBaseApproach = false; // Using detour, not direct approach
                            SetCATarget(navigation, testPoint);
                            return;
                        }
                        else
                        {
                            // Ensure collision position is valid
                            if (testCollisionPos.LengthSquared() > 1e-6)
                            {
                                // Check if this collision is closer to target than the closest we've seen
                                var collisionDistanceToTarget = Vector3D.Distance(testCollisionPos, _targetWaypoint);
                                if (collisionDistanceToTarget < _closestCollisionDistanceToTarget)
                                {
                                    _closestCollisionPoint = testCollisionPos;
                                    _closestCollisionDistanceToTarget = collisionDistanceToTarget;
                                    // Recalculate coordinate system based on new closest collision point
                                    if (!UpdateCoordinateSystem(navigation))
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            // No clear path found, use closest collision point
            var dirToRemoteControl = (_startPosition - _closestCollisionPoint).Normalized();
            if (dirToRemoteControl.LengthSquared() < 1e-6)
            {
                dirToRemoteControl = -_forward;
            }
            var caTarget = _closestCollisionPoint - dirToRemoteControl * agentSize;
            _state = CollisionAvoidanceState.Ready;
            _isOnBaseApproach = false; // No clear path found, using collision avoidance target
            SetCATarget(navigation, caTarget);
        }

        private void CalculateAnglesForRadius(double radius)
        {
            _currentAngles = new List<double>();
            
            // Start with at least 4 points at 0, 90, 180, 270 degrees
            var baseAngles = new[] { 0.0, Math.PI / 2.0, Math.PI, 3.0 * Math.PI / 2.0 };
            
            // Calculate arc distance (90 degrees = π/2 radians)
            var arcDistance = radius * (Math.PI / 2.0);
            
            // Calculate how many splits we need for a 90-degree arc
            var splitsNeeded = Math.Max(0, (int)Math.Ceiling(arcDistance / PathfinderSettings.Instance.caScanDistanceStep) - 1);
            
            // For each 90-degree arc, check if we need to add more points
            // Angle step is always π/2 divided by (splitsNeeded + 1)
            var angleStep = (Math.PI / 2.0) / (splitsNeeded + 1);
            
            for (int i = 0; i < 4; i++)
            {
                var startAngle = baseAngles[i];
                
                // Add points along this 90-degree arc
                for (int j = 0; j <= splitsNeeded; j++)
                {
                    var angle = startAngle + j * angleStep;
                    // Normalize angle to [0, 2π)
                    while (angle < 0) angle += 2.0 * Math.PI;
                    while (angle >= 2.0 * Math.PI) angle -= 2.0 * Math.PI;
                    _currentAngles.Add(angle);
                }
            }
            
            // Remove duplicates and sort
            _currentAngles = _currentAngles.Distinct().OrderBy(a => a).ToList();
        }

        public void Update(NavigationComponent navigation)
        {
            // Calculation is now done entirely in RefinePath, so Update is no longer needed
            // But we keep it for backwards compatibility in case it's called
        }

        private Vector3D GetCollisionPoint(MyOrientedBoundingBoxD pathObb, MyOrientedBoundingBoxD obstacleObb, Vector3D pathStart, Vector3D pathEnd)
        {
            // Get the path direction
            var pathDirection = pathEnd - pathStart;
            var pathLength = pathDirection.Length();
            if (pathLength < 1e-6)
            {
                // Path is a point, return the start position
                return pathStart;
            }
            var pathDirNormalized = pathDirection / pathLength;

            // Transform obstacle to local space for easier calculations
            var obstacleMatrix = MatrixD.CreateFromQuaternion(obstacleObb.Orientation);
            obstacleMatrix.Translation = obstacleObb.Center;
            var invObstacleMatrix = MatrixD.Invert(obstacleMatrix);
            var halfExtents = obstacleObb.HalfExtent;

            // Sample points along the path to find first intersection
            var stepSize = Math.Min(pathLength * 0.1, 1.0); // Sample every 10% or 1m, whichever is smaller
            var numSteps = Math.Max(1, (int)Math.Ceiling(pathLength / stepSize));
            stepSize = pathLength / numSteps;

            Vector3D firstIntersection = pathEnd; // Default to end if no intersection found
            bool foundIntersection = false;

            // Search from start to end to find first intersection
            for (int i = 0; i <= numSteps; i++)
            {
                var distanceAlong = Math.Min(pathLength, i * stepSize);
                var testPoint = pathStart + pathDirNormalized * distanceAlong;
                
                // Transform to obstacle's local space
                var localPoint = Vector3D.Transform(testPoint, invObstacleMatrix);
                
                // Check if point is within obstacle bounds
                var isInside = Math.Abs(localPoint.X) <= halfExtents.X &&
                              Math.Abs(localPoint.Y) <= halfExtents.Y &&
                              Math.Abs(localPoint.Z) <= halfExtents.Z;

                if (isInside)
                {
                    firstIntersection = testPoint;
                    foundIntersection = true;
                    break;
                }
            }

            if (foundIntersection)
            {
                return firstIntersection;
            }

            // If no intersection found in sampling, find closest point on path to obstacle center
            // This handles edge cases where sampling might miss
            var obstacleCenter = obstacleObb.Center;
            var toObstacle = obstacleCenter - pathStart;
            var projectionLength = Vector3D.Dot(toObstacle, pathDirNormalized);
            projectionLength = MathHelper.Clamp(projectionLength, 0.0, pathLength);
            var closestPointOnPath = pathStart + pathDirNormalized * projectionLength;

            return closestPointOnPath;
        }
        
        private bool IsPathClear(Vector3D start, Vector3D end, NavigationComponent navigation, out Vector3D collisionPosition, out bool isTerrainCollision)
        {
            isTerrainCollision = false;
            collisionPosition = Vector3D.Zero;
            var remoteGrid = navigation.RemoteControl?.CubeGrid;
            var agentSize = navigation.AgentSize;

            var direction = end - start;
            var length = direction.Length();
            var directionNormalized = length > 1e-6 ? direction / length : Vector3D.Zero;

            if (length > PathfinderSettings.Instance.IntentDistance && navigation.RemoteControl.GetNaturalGravity().LengthSquared() > 1e-6)
            {
                var planet = MyGamePruningStructure.GetClosestPlanet(start);
                if (planet != null)
                {
                    var downRayLength = PathfinderSettings.Instance.CAMinAltitude;
                    var step = PathfinderSettings.Instance.caScanDistanceStep;
                    var adjustedLength = length - PathfinderSettings.Instance.IntentDistance;
                    var stepCount = adjustedLength > 1e-6 ? Math.Max(1, (int)Math.Ceiling(adjustedLength / step)) : 1;
                    // skip start point
                    for (int i = 1; i <= stepCount; i++)
                    {
                        var distanceAlong = Math.Min(adjustedLength, i * step);
                        var pointOnPath = start + directionNormalized * distanceAlong;
                        var dirToPlanet = (planet.WorldMatrix.Translation - pointOnPath).Normalized();
                        var samplePoint = pointOnPath + dirToPlanet * downRayLength;
                        if (planet.IsUnderGround(samplePoint))
                        {
                            collisionPosition = pointOnPath;
                            isTerrainCollision = true;
                            // Utils.ShowHudMessage("Under ground");
                            return false;
                        }
                    }
                }
            }
            length = Math.Min(length, PathfinderSettings.Instance.IntentDistance);
            var halfExtents = new Vector3D(agentSize * 0.5, agentSize * 0.5, agentSize * 0.5);
            var offsetDistance = agentSize * 0.5;
            var offsetLength = Math.Max(0.0, length - offsetDistance);
            var offsetStart = start + directionNormalized * offsetDistance;
            var offsetEnd = offsetStart + directionNormalized * offsetLength;
            
            if (offsetLength < 1e-6)
            {
                _pathObb = new MyOrientedBoundingBoxD(offsetStart, halfExtents, Quaternion.Identity);
            }
            else
            {
                var delta = offsetEnd - offsetStart;
                var velocity = navigation.RemoteControl.GetShipVelocities().LinearVelocity;
                var velocityLength = velocity.Length();
                
                Vector3D forward;
                if (velocityLength > 1e-3)
                {
                    forward = velocity / velocityLength;
                }
                else
                {
                    forward = delta.Normalized();
                }
                
                var pathLength = delta.Length();
                var velocityExtent = velocityLength * PathfinderSettings.Instance.velFactor;
                halfExtents.Z = (pathLength + velocityExtent) * 0.5;
                
                var pathCenter = offsetStart + delta * 0.5;
                var velocityOffset = forward * velocityExtent * 0.5;
                var obbCenter = pathCenter + velocityOffset;
                
                var up = Vector3D.CalculatePerpendicularVector(forward);
                var quatRotation = Quaternion.CreateFromForwardUp(forward, up);
                _pathObb = new MyOrientedBoundingBoxD(obbCenter, halfExtents, quatRotation);
            }

            foreach (var kvp in _intentObstacles)
            {
                var intentEntityId = kvp.Key;
                if (_navigation.RemoteControl.CubeGrid.EntityId == intentEntityId)
                {
                    continue;
                }
                var intentObstacle = kvp.Value;
                if (_pathObb.Intersects(ref intentObstacle))
                {
                    _blockedIntentEntityIds.Add(kvp.Key);
                    collisionPosition = GetCollisionPoint(_pathObb, intentObstacle, start, end);
                    // Utils.ShowHudMessage("Intent obstacle");
                    return false;
                }
            }

            for (int i = 0; i < _velocityObstacles.Count; i++)
            {
                var velocityObstacle = _velocityObstacles[i];
                if (_pathObb.Intersects(ref velocityObstacle))
                {
                    _blockedVOIndices.Add(i);
                    collisionPosition = GetCollisionPoint(_pathObb, velocityObstacle, start, end);
                    // Utils.ShowHudMessage("Velocity obstacle");
                    return false;
                }
            }

            _staticQueryResults.Clear();
            if (length > agentSize * 2.0)
            {
                MyGamePruningStructure.GetAllEntitiesInOBB(ref _pathObb, _staticQueryResults, MyEntityQueryType.Both);
                var pathBounds = new BoundingBoxD(-_pathObb.HalfExtent, _pathObb.HalfExtent);
                var obbMatrix = MatrixD.CreateFromQuaternion(_pathObb.Orientation);
                obbMatrix.Translation = _pathObb.Center;

                for (int i = 0; i < _staticQueryResults.Count; i++)
                {
                    var entity = _staticQueryResults[i];

                    var voxelMap = entity as MyVoxelBase;
                    if (voxelMap != null)
                    {
                        Vector3D voxelCollisionPos;
                        if (FindVoxelCollisionPosition(voxelMap, start, end, directionNormalized, agentSize, obbMatrix, out voxelCollisionPos))
                        {
                            collisionPosition = voxelCollisionPos;
                            _staticQueryResults.Clear();
                            // Utils.ShowHudMessage("Voxel collision");
                            return false;
                        }
                        continue;
                    }

                    var grid = entity as IMyCubeGrid;
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
                            collisionPosition = blockOBB.Center;
                            _staticQueryResults.Clear();
                            // Utils.ShowHudMessage("Block collision");
                            return false;
                        }
                    }
                }
            }

            _staticQueryResults.Clear();
            return true;
        }

        private bool CheckVoxelOBB(MyVoxelBase voxelMap, Vector3D center, Vector3D halfExtents, MatrixD obbMatrix, Vector3D? targetPoint = null)
        {
            var bounds = new BoundingBoxD(-halfExtents, halfExtents);
            var vol = voxelMap.GetVoxelContentInBoundingBox_Fast(bounds, obbMatrix);
            return vol.Item2 > 0;
        }

        private bool FindVoxelCollisionPosition(MyVoxelBase voxelMap, Vector3D start, Vector3D end, Vector3D directionNormalized, double agentSize, MatrixD pathObbMatrix, out Vector3D collisionPosition)
        {
            collisionPosition = Vector3D.Zero;
            
            var pathObbOrientation = _pathObb.Orientation;
            var pathObbHalfExtents = _pathObb.HalfExtent;
            var pathObbCenter = _pathObb.Center;
            
            var obbMatrix = MatrixD.CreateFromQuaternion(pathObbOrientation);
            obbMatrix.Translation = pathObbCenter;
            
            var forward = obbMatrix.Forward;
            var pathObbLength = pathObbHalfExtents.Z * 2.0;
            var numSubOBBs = (int)Math.Floor(pathObbLength / agentSize);
            
            if (numSubOBBs <= 0)
            {
                return false;
            }
            
            var subObbHalfExtents = new Vector3D(agentSize * 0.5, agentSize * 0.5, agentSize * 0.5);

            for (int i = 0; i < numSubOBBs; i++)
            {
                var offsetAlongPath = (i - (numSubOBBs - 1) * 0.5) * agentSize;
                var subObbCenter = pathObbCenter + forward * offsetAlongPath;
                var subObbMatrix = obbMatrix;
                subObbMatrix.Translation = subObbCenter;

                var isOccupied = CheckVoxelOBB(voxelMap, subObbCenter, subObbHalfExtents, subObbMatrix);
                var voxelObb = new MyOrientedBoundingBoxD(subObbCenter, subObbHalfExtents, pathObbOrientation);
                _voxelOBBs.Add(new VoxelOBB(voxelObb, isOccupied));
                
                if (isOccupied)
                {
                    collisionPosition = subObbCenter;
                    return true;
                }
            }

            return false;
        }

        private void SetCATarget(NavigationComponent navigation, Vector3D? target)
        {
            if (navigation == null || navigation.RemoteControl == null || navigation.RemoteControl.CubeGrid == null)
            {
                return;
            }

            var entityId = navigation.RemoteControl.CubeGrid.EntityId;

            if (target.HasValue)
            {
                _intentObstacles[entityId] = _pathObb;
            }
            else
            {
                _intentObstacles.Remove(entityId);
            }

            navigation.CATarget = target;
        }

        public void Render(NavigationComponent navigation)
        {
            if (navigation == null || navigation.RemoteControl == null)
            {
                return;
            }

            var settings = PathfinderSettings.Instance;

            // Render all path checks
            if (settings.RenderPath)
            {
                var thickness = settings.PathRenderThickness;
                foreach (var pathCheck in _pathChecks)
                {
                    var color = pathCheck.IsClear ? Color.Green : Color.Orange;
                    Utils.DrawLine(pathCheck.Start, pathCheck.End, color, thickness);
                }
            }

            // Render CATarget line if path rendering is enabled
            if (settings.RenderPath)
            {
                var caTarget = navigation.CATarget;
                if (caTarget.HasValue)
                {
                    var currentPosition = navigation.RemoteControl.GetPosition();
                    var thickness = settings.PathRenderThickness;
                    Utils.DrawLine(currentPosition, caTarget.Value, Color.Red, thickness);
                    Utils.DrawSphere(caTarget.Value, Color.Red, 0.5f, false);
                }
            }

            // Render velocity and intent obstacles if dynamic obstacles rendering is enabled
            if (settings.RenderDynamicObstacles)
            {
                var lineWidth = 0.02f;
                // for (int i = 0; i < _velocityObstacles.Count; i++)
                // {
                //     var color = _blockedVOIndices.Contains(i) ? Color.Red : Color.Green;
                //     Utils.DrawOBB(_velocityObstacles[i], color, true, 1f, lineWidth);
                // }

                // foreach (var kvp in _intentObstacles)
                // {
                //     var color = _blockedIntentEntityIds.Contains(kvp.Key) ? Color.Red : Color.Yellow;
                //     Utils.DrawOBB(kvp.Value, color, true, 1f, lineWidth);
                // }

                Utils.DrawOBB(_pathObb, Color.Blue, true, 1f, lineWidth);

                foreach (var voxelObb in _voxelOBBs)
                {
                    var color = voxelObb.IsOccupied ? Color.Red : Color.Green;
                    Utils.DrawOBB(voxelObb.OBB, color, true, 1f, lineWidth * 2);
                }

                // Render sub-octants
                foreach (var subOctantObb in _subOctantOBBs)
                {
                    var color = subOctantObb.IsClear ? Color.Cyan : Color.Magenta;
                    Utils.DrawOBB(subOctantObb.OBB, color, true, 1f, lineWidth * 1.5f);
                }
            }
        }
    }
}
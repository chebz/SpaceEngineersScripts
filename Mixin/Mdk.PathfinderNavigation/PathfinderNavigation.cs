using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class PathfindingNavigation : Context
    {
        public enum NavigationState
        {
            Idle,
            CalculatingPath,
            Navigating,
            NoPath
        }

        private class PathfindingNavigationSection : Section
        {
            public DoubleProperty MaxSpeed { get; } = new DoubleProperty("MaxSpeed", MAX_SPEED_DEFAULT, comment: "Maximum navigation speed (m/s)");
            public StringProperty Alignment { get; } = new StringProperty("Alignment", "target", comment: "Alignment mode: none|target|gravity");
            public DoubleProperty MinPathDeviation { get; } = new DoubleProperty("MinPathDeviation", MIN_PATH_DEVIATION_DEFAULT, comment: "Distance from path to treat as on-course (m)");
            public DoubleProperty MaxPathDeviation { get; } = new DoubleProperty("MaxPathDeviation", MAX_PATH_DEVIATION_DEFAULT, comment: "Distance from path to fully snap back to path (m)");
            public DoubleProperty RecomputeMasterPathDelay { get; } = new DoubleProperty("RecomputeMasterPathDelay", RECOMPUTE_MASTER_PATH_DELAY_DEFAULT, comment: "Delay before recomputing the master path (s)");
            
            public PathfindingNavigationSection() : base("PathfindingNavigation")
            {
                _properties.Add(MaxSpeed);
                _properties.Add(Alignment);
                _properties.Add(MinPathDeviation);
                _properties.Add(MaxPathDeviation);
                _properties.Add(RecomputeMasterPathDelay);
            }
        }

        #region Fields
        private const double MAX_SPEED_DEFAULT = 100.0;
        private const double MIN_PATH_DEVIATION_DEFAULT = 1.0;
        private const double MAX_PATH_DEVIATION_DEFAULT = 10.0;
        private const double RECOMPUTE_MASTER_PATH_DELAY_DEFAULT = 5.0;

        private bool _initialized;
        private Program _program;
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyRemoteControl _remoteControl;
        private ITerminalProperty<int> _currentWaypointIndexProperty;
        private ITerminalProperty<string> _destinationNameProperty;
        private ITerminalProperty<Vector3D?> _destinationProperty;
        private ITerminalProperty<Vector3D> _offsetProperty;
        private ITerminalProperty<MatrixD?> _targetMatrixProperty;
        private ITerminalProperty<Vector3D?> _targetVelocityProperty;
        private ITerminalProperty<bool> _pathfindingEnabledProperty;
        private ITerminalProperty<bool> _collisionAvoidanceEnabledProperty;
        private ITerminalProperty<bool> _isOnBaseApproachProperty;
        private bool _trackDestination = false;
        private Action _onStart;
        private Action _onStop;
        private Action _onNoPath;
        private PathfindingNavigationSection _section;
        private NavigationState _navigationState = NavigationState.Idle;
        #endregion

        #region Properties
        public NavigationState State
        {
            get { return _navigationState; }
        }
        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, Navigation navigation,
            Alignment alignment, out string errorMessage)
        {
            _program = program;
            _navigation = navigation;
            _alignment = alignment;
            errorMessage = string.Empty;

            _remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "TestPathfinding: No remote control found!";
                return false;
            }

            _currentWaypointIndexProperty = _remoteControl.GetProperty("CurrentWaypointIndex") as ITerminalProperty<int>;
            if (_currentWaypointIndexProperty == null)
            {
                _remoteControl.Log("CurrentWaypointIndex property not found");
            }
            _currentWaypointIndexProperty?.SetValue(_remoteControl, -1);

            _destinationNameProperty = _remoteControl.GetProperty("PathfinderDestinationName") as ITerminalProperty<string>;
            if (_destinationNameProperty == null)
            {
                _remoteControl.Log("PathfinderDestinationName property not found");
            }

            _destinationProperty = _remoteControl.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D?>;
            if (_destinationProperty == null)
            {
                _remoteControl.Log("PathfinderDestination property not found");
            }

            _offsetProperty = _remoteControl.GetProperty("PathfinderOffset") as ITerminalProperty<Vector3D>;
            if (_offsetProperty == null)
            {
                _remoteControl.Log("PathfinderOffset property not found");
            }

            _targetMatrixProperty = _remoteControl.GetProperty("PathfinderTargetMatrix") as ITerminalProperty<MatrixD?>;
            if (_targetMatrixProperty == null)
            {
                _remoteControl.Log("PathfinderTargetMatrix property not found");
            }

            _targetVelocityProperty = _remoteControl.GetProperty("PathfinderTargetVelocity") as ITerminalProperty<Vector3D?>;
            if (_targetVelocityProperty == null)
            {
                _remoteControl.Log("PathfinderTargetVelocity property not found");
            }

            _pathfindingEnabledProperty = _remoteControl.GetProperty("PathfinderEnablePathfinding") as ITerminalProperty<bool>;
            if (_pathfindingEnabledProperty == null)
            {
                _remoteControl.Log("PathfinderEnablePathfinding property not found");
            }
            _pathfindingEnabledProperty?.SetValue(_remoteControl, true);

            _collisionAvoidanceEnabledProperty = _remoteControl.GetProperty("PathfinderEnableCollisionAvoidance") as ITerminalProperty<bool>;
            if (_collisionAvoidanceEnabledProperty == null)
            {
                _remoteControl.Log("PathfinderEnableCollisionAvoidance property not found");
            }

            _isOnBaseApproachProperty = _remoteControl.GetProperty("PathfinderIsOnBaseApproach") as ITerminalProperty<bool>;
            if (_isOnBaseApproachProperty == null)
            {
                _remoteControl.Log("PathfinderIsOnBaseApproach property not found");
            }

            var timers = program.GetLocalBlocks<IMyTimerBlock>();
            foreach (var timer in timers)
            {
                var name = timer.CustomName.ToLower();
                if (name.Contains("[pf:start]"))
                {
                    var capturedTimer = timer;
                    _onStart = delegate { capturedTimer.Trigger(); };
                }
                else if (name.Contains("[pf:stop]"))
                {
                    var capturedTimer = timer;
                    _onStop = delegate { capturedTimer.Trigger(); };
                }
                else if (name.Contains("[pf:nopath]"))
                {
                    var capturedTimer = timer;
                    _onNoPath = delegate { capturedTimer.Trigger(); };
                }
            }

            _section = new PathfindingNavigationSection();
            customDataConnector.AddSection(_section);

            // Start in Idle state
            TransitionTo(new IdleState(this));

            _initialized = true;
            return true;
        }

        public void SetStartCallback(Action onStart)
        {
            _onStart = CombineCallbacks(_onStart, onStart);
        }

        public void SetStopCallback(Action onStop)
        {
            _onStop = CombineCallbacks(_onStop, onStop);
        }

        public void SetNoPathCallback(Action onNoPath)
        {
            _onNoPath = CombineCallbacks(_onNoPath, onNoPath);
        }

        private Action CombineCallbacks(Action existing, Action additional)
        {
            if (additional == null)
            {
                return existing;
            }

            if (existing == null)
            {
                return additional;
            }

            return delegate
            {
                existing();
                additional();
            };
        }

        public override void Execute()
        {
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }

            base.Execute();
        }

        public void SetDestination(Vector3D destination)
        {
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }

            _remoteControl.Log($"Setting destination to {destination}");
            _destinationProperty.SetValue(_remoteControl, destination);
        }

        public void SetDestination(string gpsName)
        {
            SetDestination(gpsName, Vector3D.Zero);
        }

        public void SetDestination(string gpsName, Vector3D offset)
        {
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }

            var destinationNameProperty =
                _remoteControl.GetProperty("PathfinderDestinationName") as ITerminalProperty<string>;
            if (destinationNameProperty == null)
            {
                _remoteControl.Log("PathfinderDestinationName property not found");
                return;
            }

            _remoteControl.Log($"Setting destination name to {gpsName}");
            destinationNameProperty.SetValue(_remoteControl, gpsName);

            var offsetProperty =
                _remoteControl.GetProperty("PathfinderOffset") as ITerminalProperty<Vector3D>;
            if (offsetProperty != null)
            {
                offsetProperty.SetValue(_remoteControl, offset);
                _remoteControl.Log($"Set destination offset to {offset}");
            }
        }

        private List<Vector3D> ParsePath(bool dpr)
        {
            if (dpr)
            {
                var caTargetProperty = _remoteControl.GetProperty("PathfinderCATarget") as ITerminalProperty<Vector3D?>;
                if (caTargetProperty == null)
                {
                    return new List<Vector3D>();
                }

                var caTarget = caTargetProperty.GetValue(_remoteControl);
                if (!caTarget.HasValue)
                {
                    return new List<Vector3D>();
                }

                return new List<Vector3D> { caTarget.Value };
            }

            var propertyName = "PathfinderPath";
            var pathProperty = _remoteControl.GetProperty(propertyName) as ITerminalProperty<string>;
            if (pathProperty == null)
            {
                return new List<Vector3D>();
            }

            var pathString = pathProperty.GetValue(_remoteControl);
            if (string.IsNullOrEmpty(pathString))
            {
                return new List<Vector3D>();
            }

            var path = new List<Vector3D>();
            ParsePathFromString(pathString, path);
            return path;
        }

        private void ParsePathFromString(string pathString, List<Vector3D> path)
        {
            var segments = pathString.Split(new[] { ')' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var point = segment.Trim();
                if (point.Length == 0)
                {
                    continue;
                }

                if (point[0] == ',')
                {
                    point = point.Substring(1).Trim();
                }

                if (point.Length > 0 && point[0] == '(')
                {
                    point = point.Substring(1);
                }

                var coords = point.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length == 3)
                {
                    double x;
                    double y;
                    double z;
                    if (double.TryParse(coords[0], out x) &&
                        double.TryParse(coords[1], out y) &&
                        double.TryParse(coords[2], out z))
                    {
                        path.Add(new Vector3D(x, y, z));
                    }
                }
            }
        }

        private void SetCurrentWaypointIndex(int index)
        {
            if (_currentWaypointIndexProperty == null)
            {
                _currentWaypointIndexProperty = _remoteControl.GetProperty("CurrentWaypointIndex") as ITerminalProperty<int>;
            }
            _currentWaypointIndexProperty?.SetValue(_remoteControl, index);
        }

        private int GetCurrentWaypointIndexValue()
        {
            if (_currentWaypointIndexProperty == null)
            {
                _currentWaypointIndexProperty = _remoteControl.GetProperty("CurrentWaypointIndex") as ITerminalProperty<int>;
            }
            return _currentWaypointIndexProperty != null ? _currentWaypointIndexProperty.GetValue(_remoteControl) : -1;
        }

        public void Start()
        {
            // Don't reset _trackDestination here - it should be preserved when restarting in track mode
            _remoteControl.Log("Starting pathfinding navigation");
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }

            // When tracking is enabled (and pathfinding is off), always transition to waiting for path, even if already navigating
            // This allows restarting navigation when we reach the end or encounter issues
            if (CurrentState is IdleState || _trackDestination)
            {
                RecomputePath();
                TransitionTo(new WaitingForPathState(this));
                _remoteControl.Log("Started pathfinding navigation");
            }
            else
            {
                _remoteControl.Log("Already navigating or operation in progress");
            }
        }

        public void Start(Vector3D destination)
        {
            SetDestination(destination);
            Start();
        }

        public void Start(string gpsName)
        {
            Start(gpsName, Vector3D.Zero);
        }

        public void Start(string gpsName, Vector3D? offset)
        {
            SetDestination(gpsName, offset ?? Vector3D.Zero);
            Start();
        }

        public void Start(string gpsName, Vector3D? offset, bool track)
        {
            _trackDestination = track;
            SetDestination(gpsName, offset ?? Vector3D.Zero);
            Start();
        }

        public void Stop()
        {
            _trackDestination = false;
            _remoteControl.Log("Stopping pathfinding navigation");
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }
            var destinationProperty = _remoteControl.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D?>;
            if (destinationProperty == null)
            {
                _remoteControl.Log("PathfinderDestination property not found");
                return;
            }

            destinationProperty.SetValue(_remoteControl, null);
            _navigation.Stop();
            _alignment.Stop();
            TransitionTo(new IdleState(this));
        }

        public void RecomputePath()
        {
            if (_remoteControl == null)
            {
                return;
            }

            var action = _remoteControl.GetActionWithName("RecomputePath");
            if (action != null)
            {
                action.Apply(_remoteControl);
            }
            else
            {
                _remoteControl.Log("RecomputePath action not found");
            }
        }
        #endregion

        #region States
        private class IdleState : State<PathfindingNavigation>
        {
            public IdleState(PathfindingNavigation context, NavigationState navigationState = NavigationState.Idle) :
                base(context)
            {
                _context._program.Echo("IdleState");
                _context._navigationState = navigationState;
                _context._remoteControl.Log($"PathfindingNavigation: {navigationState}");

                if (navigationState == NavigationState.NoPath && _context._onNoPath != null)
                {
                    _context._onNoPath();
                }
                if (navigationState == NavigationState.Idle && _context._onStop != null)
                {
                    _context._onStop();
                }
            }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();
            }

            public override void Execute()
            {
                // Idle state - do nothing
            }
        }

        private class WaitingForPathState : State<PathfindingNavigation>
        {
            public WaitingForPathState(PathfindingNavigation context) : base(context)
            {
                _context._navigationState = NavigationState.CalculatingPath;
                _context._remoteControl.Log($"PathfindingNavigation: {_context._navigationState}");
            }

            public override void Enter()
            {
                if (_context._onStart != null)
                {
                    _context._onStart();
                }
            }

            public override void Execute()
            {
                var pathStatus = _context._remoteControl.GetProperty("PathfinderStatus") as ITerminalProperty<string>;
                if (pathStatus == null)
                {
                    _context._remoteControl.Log("PathfinderStatus property not found");
                    if (_context._trackDestination)
                    {
                        _context._program.Echo("WaitingForPathState: No path, restarting navigation");
                        // When tracking is enabled (and pathfinding is off), restart navigation instead of going idle
                        _context.Start();
                        return;
                    }
                    _context._program.Echo("WaitingForPathState: No path, going idle");
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                var pathStatusString = pathStatus.GetValue(_context._remoteControl);
                if (pathStatusString == "Ready")
                {
                    var path = _context.ParsePath(false);
                    if (path.Count > 1)
                    {
                        _context.TransitionTo(new NavigatingState(_context, path));
                    }
                    else if (_context._trackDestination)
                    {
                        _context._program.Echo("WaitingForPathState: Path found, restarting navigation");
                        _context.Start();
                    }
                }
                else if (pathStatusString == "NoPath")
                {
                    if (_context._trackDestination)
                    {
                        _context._program.Echo("WaitingForPathState: No path, restarting navigation");
                        _context.Start();
                    }
                    else
                    {
                        _context._remoteControl.Log("PathfindingNavigation: No path, scheduling recompute...");
                        _context._program.Echo("WaitingForPathState: No path, scheduling recompute");
                        _context.TransitionTo(new IdleState(_context, NavigationState.NoPath));
                    }
                }
                //_context.UpdateTrackedDestination();
            }
        }

        private class NavigatingState : State<PathfindingNavigation>
        {
            private List<Vector3D> _masterPath;
            private Vector3D? _caTarget;
            private Vector3D? _destination;
            private int _masterWaypointIndex = 1;
            private Vector3D _previousWaypoint;

            public NavigatingState(PathfindingNavigation context, List<Vector3D> path) : base(context)
            {
                _masterPath = path;
                _context._navigationState = NavigationState.Navigating;
            }

            public override void Enter()
            {
                _context._program.Echo("NavigatingState");
                _context._navigation.Stop();
                _context._alignment.Stop();
                _masterWaypointIndex = 1;
                _previousWaypoint = _context._remoteControl.GetPosition();
                _context.SetCurrentWaypointIndex(_masterWaypointIndex);
                _destination = _context._destinationProperty.GetValue(_context._remoteControl);
            }

            public override void Execute()
            {
                if (_context.UpdateTrackedDestination(ref _destination))
                {
                    _masterWaypointIndex = 1;
                    _previousWaypoint = _context._remoteControl.GetPosition();
                    _context.SetCurrentWaypointIndex(_masterWaypointIndex);
                    _context._program.Echo("NavigatingState: Destination moved, resetting master waypoint index");
                }

                var caTargetProperty = _context._remoteControl.GetProperty("PathfinderCATarget") as ITerminalProperty<Vector3D?>;
                Vector3D? newCaTarget = null;
                if (caTargetProperty != null)
                {
                    newCaTarget = caTargetProperty.GetValue(_context._remoteControl);
                }

                if (_caTarget != newCaTarget)
                {
                    _caTarget = newCaTarget;
                    _previousWaypoint = _context._remoteControl.GetPosition();
                    _masterWaypointIndex = _context.GetCurrentWaypointIndexValue();
                }

                bool pathfindingEnabled = _context._pathfindingEnabledProperty.GetValue(_context._remoteControl);
                
                if (!_caTarget.HasValue)
                {
                    _context._navigation.Stop();
                    _context._alignment.Stop();
                    _context._program.Echo("NavigatingState: No CA target, going idle");
                    return;
                }


                if (_masterWaypointIndex >= _masterPath.Count)
                {
                    _context.SetCurrentWaypointIndex(-1);
                    if (_context._trackDestination)
                    {
                        _context._navigation.Stop();
                        _context._alignment.Stop();
                    }
                    else
                    {
                        _context.Stop();
                    }
                    //_context._program.Echo($"masterWaypointIndex: {_masterWaypointIndex}, masterPath.Count: {_masterPath.Count}");
                    return;
                }

                var caWaypoint = _caTarget.Value;
                var currentPosition = _context._remoteControl.GetPosition();
                var distanceToCa = Vector3D.Distance(currentPosition, caWaypoint);
                
                // Only check master waypoint distance if we haven't reached the end of the path
                if (_masterWaypointIndex < _masterPath.Count)
                {
                    _context._program.Echo($"Tracking destination: {_context._trackDestination}");
                    if (_context._trackDestination)
                    {
                        _masterWaypointIndex = _masterPath.Count - 1;
                    }
                    else 
                    {
                        var masterWaypoint = _masterPath[_masterWaypointIndex];
                        var distanceToMaster = Vector3D.Distance(currentPosition, masterWaypoint);
                        
                        if (distanceToMaster < 1.0)
                        {
                            _masterWaypointIndex++;
                            _context.SetCurrentWaypointIndex(_masterWaypointIndex);
                            _caTarget = null;
                            _context._program.Echo("NavigatingState: Reached master waypoint, resetting CA target");
                            return;
                        }
                    }
                }

                _context._program.Echo("NavigatingState: Getting navigation target");
                var target = _context.GetNavigationTarget(_previousWaypoint, caWaypoint, currentPosition);
                var alignmentMode = _context.GetAlignmentMode();
                switch (alignmentMode)
                {
                    case "target":
                        if (distanceToCa > _context._remoteControl.CubeGrid.GridSize * 2)
                        {
                            var targetMatrixProperty = _context._remoteControl.GetProperty("PathfinderTargetMatrix") as ITerminalProperty<MatrixD?>;
                            MatrixD? targetMatrix = null;
                            if (targetMatrixProperty != null)
                            {
                                targetMatrix = targetMatrixProperty.GetValue(_context._remoteControl);
                            }
                            
                            if (targetMatrix.HasValue)
                            {
                                _context._alignment.AlignWithWorldMatrix(targetMatrix.Value);
                            }
                            else
                            {
                                _context._alignment.AlignWithTarget(target);
                            }
                        }
                        else
                        {
                            _context._alignment.Stop();
                        }
                        break;
                    case "gravity":
                        _context.AlignWithGravityAndYaw(caWaypoint);
                        break;
                    default:
                        _context.AlignWithGravityOnly();
                        break;
                }

                Vector3D targetVelocity = default(Vector3D);
                if (_context._trackDestination && _context._isOnBaseApproachProperty.GetValue(_context._remoteControl))
                {
                    var velocity = _context._targetVelocityProperty.GetValue(_context._remoteControl);
                    if (velocity.HasValue)
                    {
                        targetVelocity = velocity.Value;
                    }
                }

                _context._navigation.NavigateTo(
                    target,
                    _context._section.MaxSpeed.Value,
                    0,
                    targetVelocity);
            }
        }

        private bool UpdateTrackedDestination(ref Vector3D? destination)
        {
            if (!_trackDestination)
            {
                _program.Echo("UpdateTrackedDestination: Track destination is disabled");
                return false;
            }

            var destinationName = _destinationNameProperty.GetValue(_remoteControl);
            if (string.IsNullOrWhiteSpace(destinationName))
            {
                _program.Echo("UpdateTrackedDestination: Destination name is empty");
                return false;
            }

            var targetMatrix = _targetMatrixProperty.GetValue(_remoteControl);
            if (!targetMatrix.HasValue)
            {
                _program.Echo("UpdateTrackedDestination: Target matrix is not set");
                return false;
            }
            var offset = _offsetProperty.GetValue(_remoteControl);
            var newDestination = targetMatrix.Value.Translation;
            if (offset != Vector3D.Zero)
            {
                var offsetInTargetSpace = Vector3D.TransformNormal(offset, targetMatrix.Value);
                newDestination += offsetInTargetSpace;
            }

            double distTolerance;
            if (_pathfindingEnabledProperty.GetValue(_remoteControl))
            {
                distTolerance = 50.0;
            }
            else
            {
                distTolerance = 0.1;
            }

            if (!destination.HasValue || Vector3D.Distance(newDestination, destination.Value) > distTolerance)
            {
                _program.Echo("UpdateTrackedDestination: Destination moved, recomputing path");
                RecomputePath();
                destination = newDestination;
                return true;
            }
            return false;
        }
        #endregion

        private string GetAlignmentMode()
        {
            var mode = _section.Alignment.Value;
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "target";
            }

            mode = mode.Trim().ToLowerInvariant();
            if (mode == "none" || mode == "target" || mode == "gravity")
            {
                return mode;
            }

            return "none";
        }

        private Vector3D GetNavigationTarget(Vector3D previousWaypoint, Vector3D currentWaypoint, Vector3D currentPosition)
        {
            var pathVector = currentWaypoint - previousWaypoint;
            var pathLengthSquared = pathVector.LengthSquared();
            if (pathLengthSquared < 1e-6)
            {
                return currentWaypoint;
            }

            var pathLength = Math.Sqrt(pathLengthSquared);
            var dirAlongPath = pathVector / pathLength;
            var vectorToCurrent = currentPosition - previousWaypoint;

            var projectionLength = Vector3D.Dot(vectorToCurrent, dirAlongPath);
            var clampedProjectionLength = MathHelper.Clamp(projectionLength, 0.0, pathLength);
            var projectionPoint = previousWaypoint + dirAlongPath * clampedProjectionLength;

            var distanceToWaypoint = pathLength - clampedProjectionLength;

            var intersectionDistance = Math.Min(_section.MaxPathDeviation.Value, distanceToWaypoint);
            var intersectionPoint = projectionPoint + dirAlongPath * intersectionDistance;

            var dirAlongAdjusted = Vector3D.Normalize(intersectionPoint - currentPosition);
            if (dirAlongAdjusted.LengthSquared() <= 1e-6)
            {
                return intersectionPoint;
            }
            var adjustedTarget = currentPosition + dirAlongAdjusted * distanceToWaypoint;

            return adjustedTarget;
        }

        private void AlignWithGravityOnly()
        {
            var gravity = _remoteControl.GetNaturalGravity();
            if (gravity.LengthSquared() <= 1e-6)
            {
                _alignment.Stop();
                return;
            }

            var up = -Vector3D.Normalize(gravity);
            var forward = _remoteControl.WorldMatrix.Forward;
            var projectedForward = forward - Vector3D.Dot(forward, up) * up;
            if (projectedForward.LengthSquared() < 1e-6)
            {
                projectedForward = Vector3D.CalculatePerpendicularVector(up);
            }
            projectedForward.Normalize();

            var targetMatrix = MatrixD.CreateWorld(Vector3D.Zero, projectedForward, up);
            _alignment.AlignWithWorldMatrix(targetMatrix);
        }

        private void AlignWithGravityAndYaw(Vector3D targetPosition)
        {
            var gravity = _remoteControl.GetNaturalGravity();
            if (gravity.LengthSquared() <= 1e-6)
            {
                _alignment.Stop();
                return;
            }

            var up = -Vector3D.Normalize(gravity);
            var forward = Vector3D.Normalize(targetPosition - _remoteControl.GetPosition());
            var projectedForward = forward - Vector3D.Dot(forward, up) * up;
            if (projectedForward.LengthSquared() < 1e-6)
            {
                projectedForward = Vector3D.CalculatePerpendicularVector(up);
            }
            projectedForward.Normalize();

            var targetMatrix = MatrixD.CreateWorld(Vector3D.Zero, projectedForward, up);
            _alignment.AlignWithWorldMatrix(targetMatrix);
        }
    }
}
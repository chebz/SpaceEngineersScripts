using System;
using System.Collections.Generic;
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

            public PathfindingNavigationSection() : base("PathfindingNavigation")
            {
                _properties.Add(MaxSpeed);
                _properties.Add(Alignment);
                _properties.Add(MinPathDeviation);
                _properties.Add(MaxPathDeviation);
            }
        }

        #region Fields
        private const double MAX_SPEED_DEFAULT = 100.0;
        private const double MIN_PATH_DEVIATION_DEFAULT = 1.0;
        private const double MAX_PATH_DEVIATION_DEFAULT = 10.0;

        private bool _initialized;
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyRemoteControl _remoteControl;
        private Action _onStart;
        private Action _onStop;
        private Action _onNoPath;
        private PathfindingNavigationSection _section;
        private NavigationState _navigationState = NavigationState.Idle;
        private string _lastAlignmentModeWarning;
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
            _navigation = navigation;
            _alignment = alignment;
            errorMessage = string.Empty;

            _remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "TestPathfinding: No remote control found!";
                return false;
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

            var destinationProperty =
                _remoteControl.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D?>;
            if (destinationProperty == null)
            {
                _remoteControl.Log("PathfinderDestination property not found");
                return;
            }

            _remoteControl.Log($"Setting destination to {destination}");
            destinationProperty.SetValue(_remoteControl, destination);
        }

        public void Start()
        {
            _remoteControl.Log("Starting pathfinding navigation");
            if (!_initialized)
            {
                _remoteControl.Log("Error: PathfinderDestination not initialized");
                return;
            }

            if (CurrentState is IdleState)
            {
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

        public void Stop()
        {
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
        #endregion

        #region States
        private class IdleState : State<PathfindingNavigation>
        {
            public IdleState(PathfindingNavigation context, NavigationState navigationState = NavigationState.Idle) :
                base(context)
            {
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
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                var pathStatusString = pathStatus.GetValue(_context._remoteControl);
                if (pathStatusString == "Ready")
                {
                    var path = ParsePath();
                    if (path.Count > 0)
                    {
                        _context.TransitionTo(new NavigatingState(_context, path));
                    }
                    else
                    {
                        _context.TransitionTo(new IdleState(_context, NavigationState.NoPath));
                    }
                }
                else if (pathStatusString == "NoPath")
                {
                    _context.TransitionTo(new IdleState(_context, NavigationState.NoPath));
                }
            }

            private List<Vector3D> ParsePath()
            {
                var path = new List<Vector3D>();

                var pathProperty = _context._remoteControl.GetProperty("PathfinderPath") as ITerminalProperty<string>;
                if (pathProperty == null)
                {
                    _context._remoteControl.Log("PathfinderPath property not found");
                    return path;
                }

                var pathString = pathProperty.GetValue(_context._remoteControl);
                if (string.IsNullOrEmpty(pathString))
                {
                    return path;
                }

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

                if (path.Count == 0)
                {
                    _context._remoteControl.Log("No valid path coordinates found in PathfinderPath property");
                }
                else
                {
                    _context._remoteControl.Log($"Parsed {path.Count} waypoints from path");
                }

                return path;
            }
        }

        private class NavigatingState : State<PathfindingNavigation>
        {
            private List<Vector3D> _path;
            private int _currentIndex = 1;
            private Vector3D _previousWaypoint;

            public NavigatingState(PathfindingNavigation context, List<Vector3D> path) : base(context)
            {
                _path = path;
                _context._navigationState = NavigationState.Navigating;
            }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();
                _currentIndex = 1;
                _previousWaypoint = _context._remoteControl.GetPosition();
            }

            public override void Execute()
            {
                // Check if we have a valid path
                if (_path.Count < 2)
                {
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                // Check if we've reached the end of the path
                if (_currentIndex >= _path.Count)
                {
                    _context.Stop();
                    return;
                }

                // Navigate to current waypoint
                var currentWaypoint = _path[_currentIndex];
                var currentPosition = _context._remoteControl.GetPosition();
                var target = _context.GetNavigationTarget(_previousWaypoint, currentWaypoint, currentPosition);
                var distance = Vector3D.Distance(currentPosition, currentWaypoint);
                var alignmentMode = _context.GetAlignmentMode();
                switch (alignmentMode)
                {
                    case "target":
                        if (distance > _context._remoteControl.CubeGrid.GridSize * 2)
                        {
                            _context._alignment.AlignWithTarget(target);
                        }
                        else
                        {
                            _context._alignment.Stop();
                        }
                        break;
                    case "gravity":
                        _context.AlignWithGravityAndYaw(currentWaypoint);
                        break;
                    default:
                        _context.AlignWithGravityOnly();
                        break;
                }
                if (_context._navigation.NavigateTo(
                    target,
                    _context._section.MaxSpeed.Value))
                {
                    // Move to next waypoint
                    _currentIndex++;
                    _previousWaypoint = currentWaypoint;
                }
            }
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

            if (_lastAlignmentModeWarning != mode)
            {
                _lastAlignmentModeWarning = mode;
                _remoteControl.Log($"PathfindingNavigation: Unknown alignment mode '{mode}', defaulting to target");
            }

            return "target";
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
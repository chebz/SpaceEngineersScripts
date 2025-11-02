using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    public class PathfindingNavigation : Context
    {
        public class PathfindingNavigationSection : Section
        {
            public DoubleProperty MaxSpeed { get; } = new DoubleProperty("MaxSpeed", MAX_SPEED_DEFAULT);
            
            public PathfindingNavigationSection() : base("PathfindingNavigation")
            {
                _properties.Add(MaxSpeed);
            }
        }
        #region Fields
        private const double MAX_SPEED_DEFAULT = 100.0;

        private bool _initialized;
        private Program _program;
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyRemoteControl _remoteControl;
        private PathfindingNavigationSection _section;
        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, Navigation navigation, Alignment alignment, out string errorMessage)
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

            _section = new PathfindingNavigationSection();
            customDataConnector.AddSection(_section);

            // Start in Idle state
            TransitionTo(new IdleState(this));

            _initialized = true;
            return true;
        }

        public override void Execute()
        {
            if (!_initialized)
            {
                _program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            base.Execute();
        }

        public void SetDestination(Vector3D destination)
        {
            if (!_initialized)
            {
                _program.Echo("Error: TestPathfinding not initialized");
                return;
            }
            
            var destinationProperty = _remoteControl.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D>;
            if (destinationProperty == null)
            {
                _program.Echo("PathfinderDestination property not found");
                return;
            }
            destinationProperty.SetValue(_remoteControl, destination);
        }

        public void Start()
        {
            if (!_initialized)
            {
                _program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            if (CurrentState is IdleState)
            {
                TransitionTo(new WaitingForPathState(this));
                _program.Echo("Started pathfinding navigation");
            }
            else
            {
                _program.Echo("Already navigating or operation in progress");
            }
        }

        public void Start(Vector3D destination)
        {
            SetDestination(destination);
            Start();
        }

        public void Stop()
        {
            if (!_initialized)
            {
                _program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            _navigation.Stop();
            _alignment.Stop();
            TransitionTo(new IdleState(this));
            _program.Echo("Stopped pathfinding navigation");
        }
        #endregion

        private class IdleState : State<PathfindingNavigation>
        {
            public IdleState(PathfindingNavigation context) : base(context) { }

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
            public WaitingForPathState(PathfindingNavigation context) : base(context) { }

            public override void Enter()
            {
                _context._navigation.Stop();
            }

            public override void Execute()
            {
                var pathStatus = _context._remoteControl.GetProperty("PathfinderStatus") as ITerminalProperty<string>;
                if (pathStatus == null)
                {
                    _context._program.Echo("PathfinderStatus property not found");
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
                        _context.TransitionTo(new IdleState(_context));
                    }
                }
                else if (pathStatusString == "NoPath")
                {
                    _context.TransitionTo(new IdleState(_context));
                }
            }

            private List<Vector3D> ParsePath()
            {
                var path = new List<Vector3D>();

                var pathProperty = _context._remoteControl.GetProperty("PathfinderPath") as ITerminalProperty<string>;
                if (pathProperty == null)
                {
                    _context._program.Echo("PathfinderPath property not found");
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
                    _context._program.Echo("No valid path coordinates found in PathfinderPath property");
                }
                else
                {
                    _context._program.Echo($"Parsed {path.Count} waypoints from path");
                }

                return path;
            }
        }

        private class NavigatingState : State<PathfindingNavigation>
        {
            private List<Vector3D> _path;
            private int _currentIndex = 1;
            public NavigatingState(PathfindingNavigation context, List<Vector3D> path) : base(context) 
            { 
                _path = path;
            }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();
                _currentIndex = 1;
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
                    _context._navigation.Stop();
                    _context._alignment.Stop();

                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                _context._program.Echo($"Navigating to waypoint {_currentIndex} of {_path.Count}");

                // Navigate to current waypoint
                var currentWaypoint = _path[_currentIndex];
                _context._alignment.AlignWithTarget(currentWaypoint);
                if (_context._navigation.NavigateTo(currentWaypoint, _context._section.MaxSpeed.Value))
                {
                    // Move to next waypoint
                    _currentIndex++;
                }
            }            
        }
    }
}
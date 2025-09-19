using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    // Handles navigation along predefined paths with full alignment using state machine
    public class PathNavigation
    {
        #region Path class with nested Waypoint struct
        public class Path
        {
            public struct Waypoint
            {
                public Vector3D Position;
                public double Yaw;
                public double Pitch;
                public double Roll;
                public bool IsDocking;
            }

            public List<Waypoint> Waypoints { get; set; }
            public string Name { get; set; }
            public double Speed { get; set; }

            public Path(string name, double speed = 20.0)
            {
                Waypoints = new List<Waypoint>();
                Speed = speed;
                Name = name;
            }

            public override string ToString()
            {
                if (Waypoints.Count == 0) 
                {
                    return "No waypoints recorded.";
                }
                
                var result = $"Path: {Name}\n";
                for (int i = 0; i < Waypoints.Count; i++)
                {
                    var waypointString = 
                        $"{i + 1}: P: {Waypoints[i].Position}\n" +
                        $"YPR: {Waypoints[i].Yaw:F2}, {Waypoints[i].Pitch:F2}, {Waypoints[i].Roll:F2}\n" +
                        $"Is Docking: {Waypoints[i].IsDocking}";
                    result += waypointString;
                    if (i < Waypoints.Count - 1) {
                        result += "\n\n";
                    }
                }
                return result;
            }
        }
        #endregion

        public class PathNavStatus 
        {
            public bool idle;
            public bool navigating;
            public bool recording;
            public bool moving;
            public bool docking;
            public bool undocking;
            public int currentPathIndex;
            public string pathName;
        }

        #region Constants
        private const double PRECISION_DEFAULT = 1.0;
        private const double ALIGN_PRECISION_DEFAULT = 0.02;
        private const double DOCKING_NAV_PRECISION_DEFAULT = 0.1;
        private const double DOCKING_ALIGN_PRECISION_DEFAULT = 0.01;
        private const double APPROACH_DISTANCE_DEFAULT = 5.0;
        #endregion

        #region Fields
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyGridTerminalSystem _gridTerminalSystem;
        private IMyShipConnector _connector;
        private readonly Dictionary<string, Path> _navigationPaths = new Dictionary<string, Path>();
        private PathNavContext _context;
        #endregion

        #region Properties
        public PathNavStatus Status => _context.Status;
        
        // Precision settings
        public double Precision { get; set; } = PRECISION_DEFAULT;
        public double AlignPrecision { get; set; } = ALIGN_PRECISION_DEFAULT;
        public double DockingNavPrecision { get; set; } = DOCKING_NAV_PRECISION_DEFAULT;
        public double DockingAlignPrecision { get; set; } = DOCKING_ALIGN_PRECISION_DEFAULT;
        public double ApproachDistance { get; set; } = APPROACH_DISTANCE_DEFAULT;
        #endregion

        #region Methods
        public bool Initialize(IMyGridTerminalSystem gridTerminalSystem, string connectorName, Navigation navigation, Alignment alignment, out string errorMessage)
        {
            _gridTerminalSystem = gridTerminalSystem;
            _navigation = navigation;
            _alignment = alignment;
            _context = new PathNavContext(this);
            
            if (!InitializeConnector(connectorName, out errorMessage))
            {
                return false;
            }
            
            return true;
        }

        private bool InitializeConnector(string connectorName, out string errorMessage)
        {
            errorMessage = string.Empty;
            _connector = _gridTerminalSystem.GetBlockWithName(connectorName) as IMyShipConnector;
            
            if (_connector == null)
            {
                errorMessage = "PathNavigation: Connector not found!";
                return false;
            }
            
            return true;
        }

        public void StartRecording(string pathName)
        {
            if (!_context.Status.idle)
            {
                return; // Can't start recording while navigating
            }
            
            var path = new Path(pathName);
            _navigationPaths[pathName] = path;
            _context.StartRecording(path);
        }

        public void StopRecording()
        {
            _context.StopRecording();
        }

        public void AddWaypoint()
        {
            _context.AddWaypoint();
        }

        public void ClearPath(string pathName)
        {
            if (!_context.Status.idle)
            {
                return; // Can only clear paths when idle
            }
            
            _navigationPaths.Remove(pathName);
        }

        public void StartPath(string pathName)
        {
            if (!_navigationPaths.ContainsKey(pathName) || _navigationPaths[pathName].Waypoints.Count == 0)
            {
                return;
            }

            var path = _navigationPaths[pathName];
            _context.StartPath(path, path.Speed);
        }

        public void StopPath()
        {
            _context.StopPath();
        }

        public void Update()
        {
            _context.Execute();
        }

        public List<string> GetPathNames()
        {
            return new List<string>(_navigationPaths.Keys);
        }

        public int GetPathPointCount(string pathName)
        {
            return _navigationPaths.ContainsKey(pathName) ? _navigationPaths[pathName].Waypoints.Count : 0;
        }

        public bool HasPath(string pathName)
        {
            return _navigationPaths.ContainsKey(pathName) && _navigationPaths[pathName].Waypoints.Count > 0;
        }

        public bool GetPath(string pathName, out Path path)
        {
            return _navigationPaths.TryGetValue(pathName, out path);
        }
        #endregion

        #region Nested type: PathNavContext
        public class PathNavContext : Context
        {
            private readonly PathNavigation _pathNavigation;
            public PathNavStatus Status { get; private set; } = new PathNavStatus();
            public Path CurrentPath { get; private set; }
            private int _currentPathIndex;
            public int CurrentPathIndex 
            { 
                get { return _currentPathIndex; }
                set 
                { 
                    _currentPathIndex = value;
                    Status.currentPathIndex = _currentPathIndex;
                }
            }
            private double _maxSpeed;

            public PathNavContext(PathNavigation pathNavigation)
            {
                _pathNavigation = pathNavigation;
                TransitionTo(new IdleState(this));
            }   

            public void StartPath(Path path, double maxSpeed)
            {
                CurrentPath = path;
                CurrentPathIndex = 0;
                _maxSpeed = maxSpeed;
                Status.pathName = path.Name;
                TransitionTo(new StartPathState(this));
            }

            public void StartRecording(Path path)
            {
                Status.pathName = path.Name;
                TransitionTo(new PathRecordingState(this, path));
            }

            public void StopRecording()
            {
                if (CurrentState is PathRecordingState)
                {
                    TransitionTo(new IdleState(this));
                }
            }

            public void StopPath()
            {
                if (CurrentState is StartPathState || 
                    CurrentState is AligningState || 
                    CurrentState is MovingState || 
                    CurrentState is DockingState || 
                    CurrentState is UndockingState)
                {
                    TransitionTo(new StopPathState(this));
                }
            }

            public void AddWaypoint()
            {
                var recordingState = CurrentState as PathRecordingState;
                if (recordingState != null)
                {
                    recordingState.AddWaypoint();
                }
            }


            #region Nested States
            private class StartPathState : State<PathNavContext>
            {
                public StartPathState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    
                    // Always start by moving to the first point
                    if (currentPoint.IsDocking)
                    {
                        _context.TransitionTo(new UndockingState(_context));
                    }
                    else
                    {
                        _context.TransitionTo(new MovingState(_context));
                    }
                }
            }

            private class StopPathState : State<PathNavContext>
            {
                public StopPathState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    _context._pathNavigation._navigation.Stop();
                    _context._pathNavigation._alignment.Stop();
                    _context.TransitionTo(new IdleState(_context));
                }
            }

            public class IdleState : State<PathNavContext>
            {
                public IdleState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = false;
                    _context.Status.idle = true;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    // Do nothing in idle state
                }
            }

            public class PathRecordingState : State<PathNavContext>
            {
                private readonly Path _path;

                public PathRecordingState(PathNavContext context, Path path) : base(context) 
                {
                    _path = path;
                }

                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = true;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    // Recording state - just wait for waypoint commands
                }

                public void AddWaypoint()
                {
                    var pos = _context._pathNavigation._navigation.GetCurrentPosition();
                    double yaw, pitch, roll;
                    _context._pathNavigation._alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
                    
                    bool isDocking = _context._pathNavigation._connector.IsConnected;
                    
                    var waypoint = new Path.Waypoint() 
                    { 
                        Position = pos, 
                        Yaw = yaw, 
                        Pitch = pitch, 
                        Roll = roll,
                        IsDocking = isDocking
                    };

                    _path.Waypoints.Add(waypoint);
                }
            }

            private class AligningState : State<PathNavContext>
            {
                public AligningState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    bool useDockingPrecision = currentPoint.IsDocking;
                    
                    _context._pathNavigation._alignment.Precision = useDockingPrecision ? _context._pathNavigation.DockingAlignPrecision : _context._pathNavigation.AlignPrecision;
                    
                    if (_context._pathNavigation._alignment.AlignYawPitchRoll(currentPoint.Yaw, currentPoint.Pitch, currentPoint.Roll))
                    {
                        // Alignment complete, move to next point
                        _context.CurrentPathIndex++;
                        
                        if (_context.CurrentPathIndex >= _context.CurrentPath.Waypoints.Count)
                        {
                            _context.TransitionTo(new StopPathState(_context));
                        }
                        else
                        {
                            var nextPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                            if (nextPoint.IsDocking)
                            {
                                _context.TransitionTo(new DockingState(_context));
                            }
                            else
                            {
                                _context.TransitionTo(new MovingState(_context));
                            }
                        }
                    }
                }
            }

            private class MovingState : State<PathNavContext>
            {
                public MovingState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = true;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    
                    _context._pathNavigation._navigation.Precision = _context._pathNavigation.Precision;
                    
                    if (_context._pathNavigation._navigation.NavigateTo(currentPoint.Position, _context._maxSpeed))
                    {
                        // Position reached, now align with this point's orientation
                        _context.TransitionTo(new AligningState(_context));
                    }
                }
            }

            private class DockingState : State<PathNavContext>
            {
                private bool _approachingDock = true;
                private bool _alignedForDocking = false;

                public DockingState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _approachingDock = true;
                    _alignedForDocking = false;
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = true;
                    _context.Status.undocking = false;
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    
                    if (_approachingDock)
                    {
                        // Step 1: Move to approach position
                        _context._pathNavigation._navigation.Precision = _context._pathNavigation.DockingNavPrecision;
                        Vector3D approachPosition = currentPoint.Position + _context._pathNavigation._connector.WorldMatrix.Backward * _context._pathNavigation.ApproachDistance;
                        
                        if (_context._pathNavigation._navigation.NavigateTo(approachPosition, _context._maxSpeed))
                        {
                            _approachingDock = false;
                        }
                    }
                    else if (!_alignedForDocking)
                    {
                        // Step 2: Align with docking point orientation
                        _context._pathNavigation._alignment.Precision = _context._pathNavigation.DockingAlignPrecision;
                        
                        if (_context._pathNavigation._alignment.AlignYawPitchRoll(currentPoint.Yaw, currentPoint.Pitch, currentPoint.Roll))
                        {
                            _alignedForDocking = true;
                        }
                    }
                    else
                    {
                        // Step 3: Move to actual docking position
                        _context._pathNavigation._navigation.Precision = _context._pathNavigation.DockingNavPrecision;
                        
                        if (_context._pathNavigation._navigation.NavigateTo(currentPoint.Position, _context._maxSpeed))
                        {
                            // Finished docking
                            _context._pathNavigation._connector.Connect();
                            _context.CurrentPathIndex++;
                            
                            if (_context.CurrentPathIndex >= _context.CurrentPath.Waypoints.Count)
                            {
                                _context.TransitionTo(new StopPathState(_context));
                            }
                            else
                            {
                                var nextPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                                if (nextPoint.IsDocking)
                                {
                                    _context.TransitionTo(new UndockingState(_context));
                                }
                                else
                                {
                                    _context.TransitionTo(new MovingState(_context));
                                }
                            }
                        }
                    }
                }
            }

            private class UndockingState : State<PathNavContext>
            {
                public UndockingState(PathNavContext context) : base(context) { }

                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = true;
                }

                public override void Execute()
                {
                    // Check if we have a next point
                    if (_context.CurrentPathIndex + 1 >= _context.CurrentPath.Waypoints.Count)
                    {
                        // No more points, stop the path
                        _context.TransitionTo(new StopPathState(_context));
                        return;
                    }
                    
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    var nextPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex + 1];
                    
                    // Approach next docking point from behind
                    Vector3D targetPosition = nextPoint.Position + _context._pathNavigation._connector.WorldMatrix.Backward * _context._pathNavigation.ApproachDistance;
                    
                    _context._pathNavigation._navigation.Precision = _context._pathNavigation.DockingNavPrecision;
                    
                    // Disconnect if connected
                    if (_context._pathNavigation._connector.IsConnected)
                    {
                        _context._pathNavigation._connector.Disconnect();
                    }
                    
                    if (_context._pathNavigation._navigation.NavigateTo(targetPosition, _context._maxSpeed))
                    {
                        _context.CurrentPathIndex++;
                        _context.TransitionTo(new MovingState(_context));
                    }
                }
            }
            #endregion
        }
        #endregion
    }
}

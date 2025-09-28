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
        #region Fields
        private const double NORMAL_NAV_PRECISION_DEFAULT = 0.2;
        private const double NORMAL_ALIGN_PRECISION_DEFAULT = 0.01;
        private const double DOCKING_NAV_PRECISION_DEFAULT = 0.1;
        private const double DOCKING_ALIGN_PRECISION_DEFAULT = 0.01;
        private const double APPROACH_DISTANCE_DEFAULT = 2.0;
        private const double NUDGE_DISTANCE = 0.01;

        private readonly Dictionary<string, Path> _navigationPaths = new Dictionary<string, Path>();
        private Alignment _alignment;
        private IMyShipConnector _connector;
        private PathNavContext _context;
        private Navigation _navigation;
        private Program _program;
        private IMyRemoteControl _remoteControl;
        #endregion

        #region Properties
        public PathNavStatus Status => _context.Status;

        // Precision settings
        public double NormalNavPrecision { get; set; } = NORMAL_NAV_PRECISION_DEFAULT;
        public double NormalAlignPrecision { get; set; } = NORMAL_ALIGN_PRECISION_DEFAULT;
        public double DockingNavPrecision { get; set; } = DOCKING_NAV_PRECISION_DEFAULT;
        public double DockingAlignPrecision { get; set; } = DOCKING_ALIGN_PRECISION_DEFAULT;
        public double ApproachDistance { get; set; } = APPROACH_DISTANCE_DEFAULT;
        #endregion

        #region Methods
        public bool Initialize(Program program, string dockingConnectorName, Navigation navigation, Alignment alignment,
            out string errorMessage)
        {
            _program = program;
            _navigation = navigation;
            _alignment = alignment;
            _context = new PathNavContext(this);
            errorMessage = string.Empty;

            // Initialize remote control
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "PathNavigation: No remote control found!";
                return false;
            }

            // Initialize connector
            _connector = _program.GetLocalBlock<IMyShipConnector>(dockingConnectorName);
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

        public void StartPath(string pathName, bool reverse = false, int startIndex = 0)
        {
            if (!_navigationPaths.ContainsKey(pathName) || _navigationPaths[pathName].Waypoints.Count == 0)
            {
                return;
            }

            var path = _navigationPaths[pathName];
            _context.StartPath(path, path.Speed, reverse, startIndex);
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

        public string Save()
        {
            if (_navigationPaths.Count == 0)
            {
                return "";
            }

            var data = "";
            var pathNames = _navigationPaths.Keys.ToList();
            for (var i = 0; i < pathNames.Count; i++)
            {
                var pathName = pathNames[i];
                var path = _navigationPaths[pathName];
                data += SerializePath(path);
                if (i < pathNames.Count - 1)
                {
                    data += "\n---PATH---\n";
                }
            }

            return data;
        }

        public void Load(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            var pathSections = data.Split(new[] { "\n---PATH---\n" }, StringSplitOptions.None);
            foreach (var section in pathSections)
            {
                if (!string.IsNullOrEmpty(section.Trim()))
                {
                    var path = DeserializePath(section);
                    if (path != null)
                    {
                        _navigationPaths[path.Name] = path;
                    }
                }
            }
        }

        private string SerializePath(Path path)
        {
            var data = $"NAME:{path.Name}\n";
            data += $"SPEED:{path.Speed:F2}\n";
            data += $"WAYPOINTS:{path.Waypoints.Count}\n";

            for (var i = 0; i < path.Waypoints.Count; i++)
            {
                var wp = path.Waypoints[i];
                data += $"WP{i}:\n";
                data +=
                    $"  POS:{wp.WorldMatrix.Translation.X:F8},{wp.WorldMatrix.Translation.Y:F8},{wp.WorldMatrix.Translation.Z:F8}\n";
                data +=
                    $"  FWD:{wp.WorldMatrix.Forward.X:F8},{wp.WorldMatrix.Forward.Y:F8},{wp.WorldMatrix.Forward.Z:F8}\n";
                data += $"  RGT:{wp.WorldMatrix.Right.X:F8},{wp.WorldMatrix.Right.Y:F8},{wp.WorldMatrix.Right.Z:F8}\n";
                data += $"  UP:{wp.WorldMatrix.Up.X:F8},{wp.WorldMatrix.Up.Y:F8},{wp.WorldMatrix.Up.Z:F8}\n";
                data += $"  DOCK:{wp.IsDocking}\n";
                data += $"  DOCKDIR:{wp.DockingDir.X:F8},{wp.DockingDir.Y:F8},{wp.DockingDir.Z:F8}\n";
            }

            return data;
        }

        private Path DeserializePath(string data)
        {
            try
            {
                var lines = data.Split('\n');
                var path = new Path("temp");

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    if (line.StartsWith("NAME:"))
                    {
                        path.Name = line.Substring(5);
                    }
                    else if (line.StartsWith("SPEED:"))
                    {
                        double speed;
                        double.TryParse(line.Substring(6), out speed);
                        path.Speed = speed;
                    }
                    else if (line.StartsWith("WP") && line.EndsWith(":"))
                    {
                        // Start of new waypoint
                        var wp = new Path.Waypoint();

                        // Read next 6 lines for this waypoint (added DOCKDIR)
                        if (i + 6 < lines.Length)
                        {
                            // Position
                            var posLine = lines[i + 1].Trim();
                            if (posLine.StartsWith("POS:"))
                            {
                                var posParts = posLine.Substring(4).Split(',');
                                if (posParts.Length == 3)
                                {
                                    double x, y, z;
                                    double.TryParse(posParts[0], out x);
                                    double.TryParse(posParts[1], out y);
                                    double.TryParse(posParts[2], out z);
                                    wp.WorldMatrix.Translation = new Vector3D(x, y, z);
                                }
                            }

                            // Forward
                            var fwdLine = lines[i + 2].Trim();
                            if (fwdLine.StartsWith("FWD:"))
                            {
                                var fwdParts = fwdLine.Substring(4).Split(',');
                                if (fwdParts.Length == 3)
                                {
                                    double x, y, z;
                                    double.TryParse(fwdParts[0], out x);
                                    double.TryParse(fwdParts[1], out y);
                                    double.TryParse(fwdParts[2], out z);
                                    wp.WorldMatrix.Forward = new Vector3D(x, y, z);
                                }
                            }

                            // Right
                            var rgtLine = lines[i + 3].Trim();
                            if (rgtLine.StartsWith("RGT:"))
                            {
                                var rgtParts = rgtLine.Substring(4).Split(',');
                                if (rgtParts.Length == 3)
                                {
                                    double x, y, z;
                                    double.TryParse(rgtParts[0], out x);
                                    double.TryParse(rgtParts[1], out y);
                                    double.TryParse(rgtParts[2], out z);
                                    wp.WorldMatrix.Right = new Vector3D(x, y, z);
                                }
                            }

                            // Up
                            var upLine = lines[i + 4].Trim();
                            if (upLine.StartsWith("UP:"))
                            {
                                var upParts = upLine.Substring(3).Split(',');
                                if (upParts.Length == 3)
                                {
                                    double x, y, z;
                                    double.TryParse(upParts[0], out x);
                                    double.TryParse(upParts[1], out y);
                                    double.TryParse(upParts[2], out z);
                                    wp.WorldMatrix.Up = new Vector3D(x, y, z);
                                }
                            }

                            // Docking
                            var dockLine = lines[i + 5].Trim();
                            if (dockLine.StartsWith("DOCK:"))
                            {
                                bool isDocking;
                                bool.TryParse(dockLine.Substring(5), out isDocking);
                                wp.IsDocking = isDocking;
                            }

                            // Docking Direction
                            var dockDirLine = lines[i + 6].Trim();
                            if (dockDirLine.StartsWith("DOCKDIR:"))
                            {
                                var dockDirParts = dockDirLine.Substring(8).Split(',');
                                if (dockDirParts.Length == 3)
                                {
                                    double x, y, z;
                                    double.TryParse(dockDirParts[0], out x);
                                    double.TryParse(dockDirParts[1], out y);
                                    double.TryParse(dockDirParts[2], out z);
                                    wp.DockingDir = new Vector3D(x, y, z);
                                }
                            }

                            path.Waypoints.Add(wp);
                            i += 6; // Skip the waypoint data lines (added DOCKDIR)
                        }
                    }
                }

                return path.Waypoints.Count > 0 ? path : null;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Nested type: Path
        #region Path class with nested Waypoint struct
        public class Path
        {
            #region Properties
            public List<Waypoint> Waypoints { get; set; }
            public string Name { get; set; }
            public double Speed { get; set; }
            #endregion

            #region Constructors
            public Path(string name, double speed = 20.0)
            {
                Waypoints = new List<Waypoint>();
                Speed = speed;
                Name = name;
            }
            #endregion

            #region Methods
            public override string ToString()
            {
                if (Waypoints.Count == 0)
                {
                    return "No waypoints recorded.";
                }

                var result = $"Path: {Name}\n";
                for (var i = 0; i < Waypoints.Count; i++)
                {
                    var waypointString =
                        $"{i + 1}: P: {Waypoints[i].WorldMatrix.Translation:F2}\n" +
                        $"F: {Waypoints[i].WorldMatrix.Forward:F2}\n" +
                        $"R: {Waypoints[i].WorldMatrix.Right:F2}\n" +
                        $"U: {Waypoints[i].WorldMatrix.Up:F2}\n" +
                        $"Is Docking: {Waypoints[i].IsDocking}";
                    result += waypointString;
                    if (i < Waypoints.Count - 1)
                    {
                        result += "\n\n";
                    }
                }

                return result;
            }
            #endregion

            #region Nested type: Waypoint
        public struct Waypoint
        {
            public MatrixD WorldMatrix;
            public bool IsDocking;
            public Vector3D DockingDir; // Direction to approach this docking point
        }
            #endregion
        }
        #endregion
        #endregion

        #region Nested type: PathNavContext
        public class PathNavContext : Context
        {
            #region Fields
            private readonly PathNavigation _pathNavigation;
            private int _currentPathIndex;
            private bool _isReversed;
            private double _maxSpeed;
            #endregion

            #region Properties
            public PathNavStatus Status { get; } = new PathNavStatus();
            public Path CurrentPath { get; private set; }

            public int CurrentPathIndex
            {
                get { return _currentPathIndex; }
                set
                {
                    _currentPathIndex = value;
                    Status.currentPathIndex = _currentPathIndex;
                }
            }
            #endregion

            #region Constructors
            public PathNavContext(PathNavigation pathNavigation)
            {
                _pathNavigation = pathNavigation;
                TransitionTo(new IdleState(this));
            }
            #endregion

            #region Methods
            public void StartPath(Path path, double maxSpeed, bool reverse = false, int startIndex = 0)
            {
                CurrentPath = path;
                _maxSpeed = maxSpeed;
                _isReversed = reverse;
                Status.pathName = path.Name;

                // Handle start index with reverse conversion
                if (reverse)
                {
                    // Convert startIndex for reverse: 0 becomes last, 1 becomes second-to-last, etc.
                    CurrentPathIndex = startIndex == 0 ? path.Waypoints.Count - 1 : path.Waypoints.Count - 1 - startIndex;
                }
                else
                {
                    CurrentPathIndex = startIndex;
                }

                // Validate index bounds
                if (CurrentPathIndex < 0 || CurrentPathIndex >= path.Waypoints.Count)
                {
                    CurrentPathIndex = reverse ? path.Waypoints.Count - 1 : 0;
                }

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
                    _pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: Adding waypoint";
                    recordingState.AddWaypoint();
                }
            }
            #endregion


            #region Nested States
            private class StartPathState : State<PathNavContext>
            {
                #region Constructors
                public StartPathState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: StartPathState";
                    }
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
                        _context.TransitionTo(new AligningState(_context));
                    }
                }
                #endregion
            }

            private class StopPathState : State<PathNavContext>
            {
                #region Constructors
                public StopPathState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: StopPathState";
                    }
                }

                public override void Execute()
                {
                    _context._pathNavigation._navigation.Stop();
                    _context._pathNavigation._alignment.Stop();
                    _context.TransitionTo(new IdleState(_context));
                }
                #endregion
            }

            public class IdleState : State<PathNavContext>
            {
                #region Constructors
                public IdleState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = false;
                    _context.Status.idle = true;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    _context.Status.currentStateName = "IdleState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: IdleState";
                    }
                }

                public override void Execute()
                {
                    // Do nothing in idle state
                }
                #endregion
            }

            public class PathRecordingState : State<PathNavContext>
            {
                #region Fields
                private readonly Path _path;
                #endregion

                #region Constructors
                public PathRecordingState(PathNavContext context, Path path) : base(context)
                {
                    _path = path;
                }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = false;
                    _context.Status.recording = true;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    _context.Status.currentStateName = "StartPathState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: PathRecordingState";
                    }
                }

                public override void Execute()
                {
                    // Recording state - just wait for waypoint commands
                }

                public void AddWaypoint()
                {
                    var worldMatrix = _context._pathNavigation._remoteControl.WorldMatrix;
                    var isDocking = _context._pathNavigation._connector.IsConnected;
                    
                    // If docking, capture the other connector's forward direction
                    var dockingDir = Vector3D.Zero;
                    if (isDocking && _context._pathNavigation._connector.OtherConnector != null)
                    {
                        dockingDir = _context._pathNavigation._connector.OtherConnector.WorldMatrix.Forward;
                    }

                    var waypoint = new Path.Waypoint
                    {
                        WorldMatrix = worldMatrix,
                        IsDocking = isDocking,
                        DockingDir = dockingDir
                    };

                    _path.Waypoints.Add(waypoint);
                }
                #endregion
            }

            private class AligningState : State<PathNavContext>
            {
                #region Constructors
                public AligningState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    _context.Status.currentStateName = "AligningState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: AligningState";
                    }
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    var useDockingPrecision = currentPoint.IsDocking;

                    _context._pathNavigation._alignment.Precision = useDockingPrecision
                        ? _context._pathNavigation.DockingAlignPrecision
                        : _context._pathNavigation.NormalAlignPrecision;

                    // Align directly with the waypoint's world matrix
                    if (_context._pathNavigation._alignment.AlignWithWorldMatrix(currentPoint.WorldMatrix))
                    {
                        // Alignment complete, now move to current point or handle docking
                        if (currentPoint.IsDocking)
                        {
                            _context.TransitionTo(new DockingState(_context));
                        }
                        else
                        {
                            // Not a docking point, move to position
                            _context.TransitionTo(new MovingState(_context));
                        }
                    }
                }
                #endregion
            }

            private class MovingState : State<PathNavContext>
            {
                #region Constructors
                public MovingState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = true;
                    _context.Status.docking = false;
                    _context.Status.undocking = false;
                    _context.Status.currentStateName = "MovingState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: MovingState";
                    }
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];

                    _context._pathNavigation._navigation.Precision = _context._pathNavigation.NormalNavPrecision;

                    if (_context._pathNavigation._navigation.NavigateTo(currentPoint.WorldMatrix.Translation,
                            _context._maxSpeed))
                    {
                        // Position reached, move to next point
                        if (_context._isReversed)
                        {
                            _context.CurrentPathIndex--;
                        }
                        else
                        {
                            _context.CurrentPathIndex++;
                        }

                        var isPathComplete = _context._isReversed
                            ? _context.CurrentPathIndex < 0
                            : _context.CurrentPathIndex >= _context.CurrentPath.Waypoints.Count;
                        if (isPathComplete)
                        {
                            _context.TransitionTo(new StopPathState(_context));
                        }
                        else
                        {
                            // Move to next point (align first)
                            _context.TransitionTo(new AligningState(_context));
                        }
                    }
                }
                #endregion
            }

            private class DockingState : State<PathNavContext>
            {
                #region Fields
                private bool _alignedForDocking;
                private bool _approachingDock = true;
                #endregion

                #region Constructors
                public DockingState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
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
                    _context.Status.currentStateName = "DockingState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: DockingState";
                    }
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];

                    if (_approachingDock)
                    {
                        // Step 1: Move to approach position using stored docking direction
                        _context._pathNavigation._navigation.Precision = _context._pathNavigation.NormalNavPrecision;
                        var forwardDirection = currentPoint.DockingDir.LengthSquared() > 0 ? 
                                             currentPoint.DockingDir : 
                                             currentPoint.WorldMatrix.Forward;
                        var approachPosition = currentPoint.WorldMatrix.Translation +
                                               forwardDirection * _context._pathNavigation.ApproachDistance;

                        if (_context._pathNavigation._navigation.NavigateTo(approachPosition, _context._maxSpeed))
                        {
                            _approachingDock = false;
                        }
                    }
                    else if (!_alignedForDocking)
                    {
                        // Step 2: Align with docking point orientation
                        _context._pathNavigation._alignment.Precision = _context._pathNavigation.DockingAlignPrecision;

                        if (_context._pathNavigation._alignment.AlignWithWorldMatrix(currentPoint.WorldMatrix))
                        {
                            _alignedForDocking = true;
                        }
                    }
                    else
                    {
                        // Step 3: Move to actual docking position
                        _context._pathNavigation._navigation.Precision = _context._pathNavigation.DockingNavPrecision;

                        if (_context._pathNavigation._navigation.NavigateTo(currentPoint.WorldMatrix.Translation,
                                _context._maxSpeed))
                        {
                            // Move to connecting state to attempt docking
                            _context.TransitionTo(new ConnectingState(_context));
                        }
                    }
                }
                #endregion
            }

            private class ConnectingState : State<PathNavContext>
            {
                #region Fields
                // 3x3 grid pattern: [up-right, up-center, up-left, center-right, center, center-left, down-right, down-center, down-left]
                private readonly Vector3D[] _nudgePattern = new Vector3D[9];
                private int _attemptCount;
                private int _currentPatternIndex;
                private Vector3D _dockingForward;
                private Vector3D _dockingRight;
                private Vector3D _dockingUp;
                private Vector3D _originalPosition;
                #endregion

                #region Constructors
                public ConnectingState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = true;
                    _context.Status.undocking = false;
                    _context.Status.currentStateName = "ConnectingState";
                    
                    // Log state transition
                    if (_context._pathNavigation._remoteControl != null)
                    {
                        _context._pathNavigation._remoteControl.CustomData += $"\n[{DateTime.Now:HH:mm:ss}] PathNavigation: ConnectingState";
                    }

                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    _originalPosition = currentPoint.WorldMatrix.Translation;
                    _dockingForward = currentPoint.WorldMatrix.Forward;
                    _dockingRight = currentPoint.WorldMatrix.Right;
                    _dockingUp = currentPoint.WorldMatrix.Up;

                    // Initialize nudge pattern
                    InitializeNudgePattern();
                    _attemptCount = 0;
                    _currentPatternIndex = 0;

                    // Try to connect at original position first
                    TryConnect();
                }

                private void InitializeNudgePattern()
                {
                    // Create 3x3 grid pattern
                    var index = 0;
                    for (var up = 1; up >= -1; up--) // up to down
                    for (var right = -1; right <= 1; right++) // left to right
                    {
                        _nudgePattern[index] = _originalPosition +
                                               _dockingUp * up * NUDGE_DISTANCE +
                                               _dockingRight * right * NUDGE_DISTANCE;
                        index++;
                    }
                }

                public override void Execute()
                {
                    // Check if connector is connected
                    if (_context._pathNavigation._connector.IsConnected)
                    {
                        // Successfully connected, proceed to next waypoint
                        if (_context._isReversed)
                        {
                            _context.CurrentPathIndex--;
                        }
                        else
                        {
                            _context.CurrentPathIndex++;
                        }

                        var isPathComplete = _context._isReversed
                            ? _context.CurrentPathIndex < 0
                            : _context.CurrentPathIndex >= _context.CurrentPath.Waypoints.Count;
                        if (isPathComplete)
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

                        return;
                    }

                    // Try next position in pattern
                    if (_attemptCount > 0)
                    {
                        _context._pathNavigation._navigation.Precision = _context._pathNavigation.DockingNavPrecision;

                        if (_context._pathNavigation._navigation.NavigateTo(_nudgePattern[_currentPatternIndex],
                                _context._maxSpeed))
                        {
                            TryConnect();
                        }
                    }
                }

                private void TryConnect()
                {
                    _context._pathNavigation._connector.Connect();
                    _attemptCount++;

                    // If this is the first attempt (original position), try forward nudge next
                    if (_attemptCount == 1)
                    {
                        _currentPatternIndex = 4; // center position (already at original)
                        // Move forward along docking axis
                        var forwardPosition = _originalPosition + _dockingForward * NUDGE_DISTANCE;
                        _nudgePattern[_currentPatternIndex] = forwardPosition;
                        return;
                    }

                    // Move to next pattern position
                    _currentPatternIndex++;
                    if (_currentPatternIndex >= _nudgePattern.Length)
                    {
                        // All attempts failed, stop path
                        _context.TransitionTo(new StopPathState(_context));
                    }
                }
                #endregion
            }

            private class UndockingState : State<PathNavContext>
            {
                #region Constructors
                public UndockingState(PathNavContext context) : base(context) { }
                #endregion

                #region Methods
                public override void Enter()
                {
                    _context.Status.navigating = true;
                    _context.Status.recording = false;
                    _context.Status.idle = false;
                    _context.Status.moving = false;
                    _context.Status.docking = false;
                    _context.Status.undocking = true;
                    _context.Status.currentStateName = "UndockingState";
                }

                public override void Execute()
                {
                    var currentPoint = _context.CurrentPath.Waypoints[_context.CurrentPathIndex];
                    var forwardDirection = currentPoint.DockingDir.LengthSquared() > 0 ? 
                                         currentPoint.DockingDir : 
                                         currentPoint.WorldMatrix.Forward;

                    // Move away from current docking point using stored docking direction
                    var targetPosition = currentPoint.WorldMatrix.Translation +
                                         forwardDirection * _context._pathNavigation.ApproachDistance;
                    _context._pathNavigation._navigation.Precision = _context._pathNavigation.NormalNavPrecision;
                    // Disconnect if connected
                    if (_context._pathNavigation._connector.IsConnected)
                    {
                        _context._pathNavigation._connector.Disconnect();
                    }

                    if (_context._pathNavigation._navigation.NavigateTo(targetPosition, _context._maxSpeed))
                    {
                        if (_context._isReversed)
                        {
                            _context.CurrentPathIndex--;
                        }
                        else
                        {
                            _context.CurrentPathIndex++;
                        }

                        var isPathComplete = _context._isReversed
                            ? _context.CurrentPathIndex < 0
                            : _context.CurrentPathIndex >= _context.CurrentPath.Waypoints.Count;
                        if (isPathComplete)
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
                #endregion
            }
            #endregion
        }
        #endregion

        #region Nested type: PathNavStatus
        public class PathNavStatus
        {
            #region Fields
            public int currentPathIndex;
            public string currentStateName;
            public bool docking;
            public bool idle;
            public bool moving;
            public bool navigating;
            public string pathName;
            public bool recording;
            public bool undocking;
            #endregion
        }
        #endregion
    }
}
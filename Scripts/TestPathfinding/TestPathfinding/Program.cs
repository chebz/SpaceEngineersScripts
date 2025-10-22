using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private CustomDataConnector _customDataConnector;
        private Navigation _navigation;
        private Alignment _alignment;
        private TestPathfinding _testPathfinding;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            _navigation = new Navigation();
            _alignment = new Alignment();
            _testPathfinding = new TestPathfinding();

            string errorMessage;
            if (!_navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            if (!_testPathfinding.Initialize(this, _navigation, _alignment, out errorMessage))
            {
                Echo($"TestPathfinding Error: {errorMessage}");
            }

            _customDataConnector.Load();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }

            _testPathfinding.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            var command = argument.ToLower();

            switch (command)
            {
                case "start":
                    _testPathfinding.Start();
                    Echo("Starting pathfinding navigation...");
                    break;

                case "stop":
                    _testPathfinding.Stop();
                    Echo("Stopping pathfinding navigation...");
                    break;

                default:
                    Echo("Available commands: start, stop");
                    break;
            }
        }
    }

    public class TestPathfinding : Context
    {
        #region Fields
        private bool _initialized = false;
        public Program program;
        public Navigation navigation;
        public Alignment alignment;
        public IMyRemoteControl remoteControl;
        #endregion

        #region Methods
        public bool Initialize(Program program, Navigation navigation, Alignment alignment, out string errorMessage)
        {
            this.program = program;
            this.navigation = navigation;
            this.alignment = alignment;
            errorMessage = string.Empty;

            remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (remoteControl == null)
            {
                errorMessage = "TestPathfinding: No remote control found!";
                return false;
            }

            // Start in Idle state
            TransitionTo(new IdleState(this));

            _initialized = true;
            return true;
        }

        public override void Execute()
        {
            if (!_initialized)
            {
                program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            base.Execute();
        }

        public void Start()
        {
            if (!_initialized)
            {
                program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            if (CurrentState is IdleState)
            {
                TransitionTo(new NavigatingState(this));
                program.Echo("Started pathfinding navigation");
            }
            else
            {
                program.Echo("Already navigating or operation in progress");
            }
        }

        public void Stop()
        {
            if (!_initialized)
            {
                program.Echo("Error: TestPathfinding not initialized");
                return;
            }

            navigation.Stop();
            alignment.Stop();
            TransitionTo(new IdleState(this));
            program.Echo("Stopped pathfinding navigation");
        }
        #endregion
    }

    public class IdleState : State<TestPathfinding>
    {
        public IdleState(TestPathfinding context) : base(context) { }

        public override void Enter()
        {
            _context.navigation.Stop();
            _context.alignment.Stop();
        }

        public override void Execute()
        {
            // Idle state - do nothing
        }
    }

    public class NavigatingState : State<TestPathfinding>
    {
        private List<Vector3D> _path = new List<Vector3D>();
        private int _currentIndex = 0;
        private bool _waitingForPath = false;
        private Vector3D? _finalDestination = null;
        public NavigatingState(TestPathfinding context) : base(context) { }

        public override void Enter()
        {
            _context.navigation.Stop();
            _context.alignment.Stop();
            _path = ParsePath();
            _currentIndex = 0;
            _finalDestination = GetFinalDestination();
        }

        public override void Execute()
        {
            if (_waitingForPath)
            {
                _path = ParsePath();
                if (_path.Count == 0)
                {
                    return;
                }
                _currentIndex = 0;
                _finalDestination = GetFinalDestination();
                _waitingForPath = false;
            }

            // Check if we have a valid path
            if (_path.Count == 0)
            {
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            // Check if we've reached the end of the path
            if (_currentIndex >= _path.Count)
            {
                _context.navigation.Stop();
                _context.alignment.Stop();
                // Check if we're close enough to the final destination
                if (_finalDestination.HasValue)
                {
                    var distanceToFinal = Vector3D.Distance(_context.remoteControl.GetPosition(), _finalDestination.Value);
                    if (distanceToFinal > 1.0) // More than 1 meter away
                    {
                        _context.program.Echo($"Still {distanceToFinal:F1}m from final destination, recomputing path");
                        _context.remoteControl.ApplyAction("RecomputePath");
                        _waitingForPath = true;
                        return;
                    }
                }
                
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            // Navigate to current waypoint
            var currentWaypoint = _path[_currentIndex];
            _context.alignment.AlignWithTarget(currentWaypoint);
            if (_context.navigation.NavigateTo(currentWaypoint, 100))
            {
                // Move to next waypoint
                _currentIndex++;
            }
        }

        private List<Vector3D> ParsePath()
        {
            var path = new List<Vector3D>();
            var customData = _context.remoteControl.CustomData;
            if (string.IsNullOrEmpty(customData))
            {
                return path;
            }

            // Parse GPS coordinates from custom data
            var lines = customData.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("GPS:Path_"))
                {
                    // Parse GPS format: GPS:Path_1:X:Y:Z:
                    var parts = line.Split(':');
                    if (parts.Length >= 5)
                    {
                        double x, y, z;
                        if (double.TryParse(parts[2], out x) &&
                            double.TryParse(parts[3], out y) &&
                            double.TryParse(parts[4], out z))
                        {
                            path.Add(new Vector3D(x, y, z));
                        }
                    }
                }
            }

            if (path.Count == 0)
            {
                _context.program.Echo("No valid GPS coordinates found in custom data");
            }
            else
            {
                _context.program.Echo($"Parsed {path.Count} waypoints from path");
            }

            return path;
        }

        private Vector3D? GetFinalDestination()
        {
            // Get the first waypoint from remote control (the original destination)
            var waypoints = new List<Sandbox.ModAPI.Ingame.MyWaypointInfo>();
            _context.remoteControl.GetWaypointInfo(waypoints);
            
            if (waypoints.Count > 0)
            {
                return waypoints[0].Coords;
            }
            
            return null;
        }
    }
}

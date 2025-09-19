using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class Program : MyGridProgram
    {
        #region Fields
        private readonly ProgramContext _context;
        private readonly Navigation _navigation;
        private readonly Alignment _alignment;
        private readonly PathNavigation _pathNavigation;
        private readonly List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
        private readonly IMyTextPanel _lcdPanel;
        #endregion

        #region Constructors
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            GridTerminalSystem.GetBlocksOfType(_lights);

            var lcdPanels = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(lcdPanels);
            _lcdPanel = lcdPanels.Count > 0 ? lcdPanels[0] : null;

            _context = new ProgramContext(this);

            // Initialize systems
            _navigation = new Navigation();
            _alignment = new Alignment();
            
            string errorMessage;
            if (!_navigation.Initialize(GridTerminalSystem, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }
            
            if (!_alignment.Initialize(GridTerminalSystem, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }

            _pathNavigation = new PathNavigation();
            if (!_pathNavigation.Initialize(GridTerminalSystem, "Docking Connector", _navigation, _alignment, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }
        }
        #endregion

        #region Methods
        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument.ToLower().Trim());
            }

            _context.Execute();
            _pathNavigation.Update();

            UpdateLights();
            UpdateLCD();
        }

        private void HandleCommand(string command)
        {
            switch (command)
            {
                case "startrecording":
                    _pathNavigation.StartRecording("testPath");
                    Echo("Started recording path. Use 'markPoint' to record points.");
                    break;
                case "markpoint":
                    if (_pathNavigation.Status.recording)
                    {
                        _pathNavigation.AddWaypoint();
                        var count = _pathNavigation.GetPathPointCount("testPath");
                        Echo($"Marked point {count}");
                    }
                    else
                    {
                        Echo("Not recording. Use 'startRecording' first.");
                    }
                    break;
                case "stoprecording":
                    if (_pathNavigation.Status.recording)
                    {
                        _pathNavigation.StopRecording();
                        Echo("Stopped recording path.");
                    }
                    else
                    {
                        Echo("Not recording.");
                    }
                    break;
                case "runpath":
                    if (_context.CurrentState is RunPathState)
                    {
                        Echo("Path already running!");
                        break;
                    }
                    _pathNavigation.StartPath("testPath");
                    _context.TransitionTo(new RunPathState(_context));
                    Echo("Running recorded path...");
                    break;
                case "stop":
                    if (_context.CurrentState is RunPathState)
                    {
                        ((RunPathState)_context.CurrentState).Stop();
                    }
                    else
                    {
                        _context.TransitionTo(null);
                    }
                    Echo("Stopped.");
                    break;
                case "printpath":
                    PathNavigation.Path path;
                    if (_pathNavigation.GetPath("testPath", out path))
                    {
                        Echo($"Path: {path}");
                    }
                    break;
                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        private void UpdateLights()
        {
            if (_pathNavigation.Status.navigating)
            {
                foreach (var light in _lights)
                {
                    light.Color = Color.Yellow;
                }
            }
            else if (_pathNavigation.Status.recording)
            {
                foreach (var light in _lights)
                {
                    light.Color = Color.Red;
                }
            }
            else
            {
                foreach (var light in _lights)
                {
                    light.Color = Color.Green;
                }
            }
        }

        private void UpdateLCD()
        {
            string displayText;
            if (_pathNavigation.Status.navigating)
            {
                var totalPoints = _pathNavigation.HasPath(_pathNavigation.Status.pathName) ? 
                    _pathNavigation.GetPathPointCount(_pathNavigation.Status.pathName) : 0;
                displayText = $"running:\n{_pathNavigation.Status.pathName}\n{_pathNavigation.Status.currentPathIndex + 1}/{totalPoints}";
            }
            else if (_pathNavigation.Status.recording)
            {
                var recordedPoints = _pathNavigation.HasPath(_pathNavigation.Status.pathName) ? 
                    _pathNavigation.GetPathPointCount(_pathNavigation.Status.pathName) : 0;
                displayText = $"recording:\n{_pathNavigation.Status.pathName}\n{recordedPoints}";
            }
            else
            {
                displayText = "idle";
            }

            if (_lcdPanel != null)
            {
                _lcdPanel.WriteText(displayText);
            }
            else
            {
                var surface = Me.GetSurface(0);
                surface.WriteText(displayText);
            }
        }
        #endregion

        #region Nested type: ProgramContext
        private class ProgramContext : Context
        {
            public readonly Program program;
            public ProgramContext(Program program)
            {
                this.program = program;
            }
        }
        #endregion


        #region Nested type: RunPathState
        private class RunPathState : State<ProgramContext>
        {
            private const float NAVIGATION_SPEED = 5;

            public RunPathState(ProgramContext context) : base(context) { }

            public override void Enter()
            {
                if (!ParseNavigationSettings())
                {
                    _context.TransitionTo(null);
                    return;
                }

                if (!_context.program._pathNavigation.HasPath("testPath"))
                {
                    _context.program.Echo("No path recorded. Use 'startRecording' and 'markPoint' first.");
                    _context.TransitionTo(null);
                    return;
                }

                var pointCount = _context.program._pathNavigation.GetPathPointCount("testPath");
                _context.program.Echo($"Running path with {pointCount} points...");
                _context.program._pathNavigation.StartPath("testPath");
            }

            public override void Execute()
            {
                if (_context.program._pathNavigation.Status.idle)
                {
                    End();
                }
            }

            public void Stop()
            {
                _context.program._pathNavigation.StopPath();
                End();
            }

            private bool ParseNavigationSettings()
            {
                double kp, ki, kd; 
                bool factorGravity;
                if (CustomDataConnector.ParseDouble(_context.program.Me, "kp", out kp))
                {
                    _context.program._navigation.PidXPos.Kp = kp;
                    _context.program._navigation.PidYPos.Kp = kp;
                    _context.program._navigation.PidZPos.Kp = kp;
                }
                else
                {
                    _context.program.Echo("WARNING: Failed to parse kp from custom data!");
                }
                if (CustomDataConnector.ParseDouble(_context.program.Me, "ki", out ki))
                {
                    _context.program._navigation.PidXPos.Ki = ki;
                    _context.program._navigation.PidYPos.Ki = ki;
                    _context.program._navigation.PidZPos.Ki = ki;
                }
                else
                {
                    _context.program.Echo("WARNING: Failed to parse ki from custom data!");
                }
                if (CustomDataConnector.ParseDouble(_context.program.Me, "kd", out kd))
                {
                    _context.program._navigation.PidXPos.Kd = kd;
                    _context.program._navigation.PidYPos.Kd = kd;
                    _context.program._navigation.PidZPos.Kd = kd;
                }
                else
                {
                    _context.program.Echo("WARNING: Failed to parse kd from custom data!");
                }
                if (CustomDataConnector.ParseBool(_context.program.Me, "factorGravity", out factorGravity))
                {
                    _context.program._navigation.FactorGravity = factorGravity;
                }
                else
                {
                    _context.program.Echo("WARNING: Failed to parse factorGravity from custom data!");
                }
                
                _context.program.Echo($"factorGravity: {_context.program._navigation.FactorGravity}\n" +
                $"kp: {_context.program._navigation.PidXPos.Kp}, ki: {_context.program._navigation.PidXPos.Ki}, kd: {_context.program._navigation.PidXPos.Kd}\n" +
                $"brakingDistanceFactor: {_context.program._navigation.BrakingDistanceFactor}\n" +
                $"precision: {_context.program._navigation.Precision}");
                
                return true;
            }

            private void End()
            {
                _context.TransitionTo(null);
            }
        }
        #endregion
    }
}
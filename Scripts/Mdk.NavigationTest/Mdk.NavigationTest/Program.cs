using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class Program : MyGridProgram
    {
        #region Fields
        private readonly Alignment _alignment;
        private readonly ProgramContext _context;
        private readonly IMyTextPanel _lcdPanel;
        private readonly List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
        private readonly Navigation _navigation;
        private readonly PathNavigation _pathNavigation;
        private readonly IMyRemoteControl _remoteControl;
        private readonly IMySoundBlock _soundBlock;
        private MatrixD? _markedAlignment;
        #endregion

        #region Constructors
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            GridTerminalSystem.GetBlocksOfType(_lights);

            var lcdPanels = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(lcdPanels);
            _lcdPanel = lcdPanels.Count > 0 ? lcdPanels[0] : null;

            var remoteControls = new List<IMyRemoteControl>();
            GridTerminalSystem.GetBlocksOfType(remoteControls);
            _remoteControl = remoteControls.FirstOrDefault();

            var soundBlocks = new List<IMySoundBlock>();
            GridTerminalSystem.GetBlocksOfType(soundBlocks);
            _soundBlock = soundBlocks.Count > 0 ? soundBlocks[0] : null;

            _context = new ProgramContext(this);

            // Initialize systems
            _navigation = new Navigation();
            _alignment = new Alignment();

            string errorMessage;
            if (!_navigation.Initialize(this, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }

            if (!_alignment.Initialize(this, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }

            _pathNavigation = new PathNavigation();
            if (!_pathNavigation.Initialize(this, "Docking Connector", _navigation, _alignment, out errorMessage))
            {
                Echo(errorMessage);
                return;
            }

            // Load saved paths
            Load();
        }
        #endregion

        #region Methods
        public void Save()
        {
            var pathNavData = _pathNavigation.Save();
            Storage = pathNavData;
        }

        public void Load()
        {
            if (!string.IsNullOrEmpty(Storage))
            {
                _pathNavigation.Load(Storage);
            }
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

                    _context.TransitionTo(new RunPathState(_context));
                    Echo("Starting ping-pong path navigation (forward)...");
                    break;
                case "stop":
                    if (_context.CurrentState is RunPathState)
                    {
                        ((RunPathState)_context.CurrentState).Stop();
                    }
                    else if (_context.CurrentState is AligningState)
                    {
                        ((AligningState)_context.CurrentState).Stop();
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
                case "alignmark":
                    _markedAlignment = _remoteControl.WorldMatrix;
                    Echo("Marked current alignment for future reference.");
                    break;
                case "align":
                    if (_context.CurrentState is AligningState)
                    {
                        Echo("Already aligning!");
                        break;
                    }

                    _context.TransitionTo(new AligningState(_context));
                    Echo("Starting alignment to gravity-level orientation...");
                    break;
                case "printalign":
                    double yaw, pitch, roll;
                    _alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
                    var calculatedMatrix = _alignment.YawPitchRollToWorldMatrix(yaw, pitch, roll);
                    Echo($"Yaw: {yaw:F2}, Pitch: {pitch:F2}, Roll: {roll:F2}");

                    var wm = _remoteControl.WorldMatrix;
                    Echo($"WM Translation: ({wm.Translation.X:F2}, {wm.Translation.Y:F2}, {wm.Translation.Z:F2})");
                    Echo($"WM Forward: ({wm.Forward.X:F2}, {wm.Forward.Y:F2}, {wm.Forward.Z:F2})");
                    Echo($"WM Right: ({wm.Right.X:F2}, {wm.Right.Y:F2}, {wm.Right.Z:F2})");
                    Echo($"WM Up: ({wm.Up.X:F2}, {wm.Up.Y:F2}, {wm.Up.Z:F2})");

                    // Save to remote control's custom data
                    var customData = "=== ALIGNMENT DATA ===\n" +
                                     $"Yaw: {yaw:F2}, Pitch: {pitch:F2}, Roll: {roll:F2}\n" +
                                     $"WM Translation: ({wm.Translation.X:F2}, {wm.Translation.Y:F2}, {wm.Translation.Z:F2})\n" +
                                     $"WM Forward: ({wm.Forward.X:F2}, {wm.Forward.Y:F2}, {wm.Forward.Z:F2})\n" +
                                     $"WM Right: ({wm.Right.X:F2}, {wm.Right.Y:F2}, {wm.Right.Z:F2})\n" +
                                     $"WM Up: ({wm.Up.X:F2}, {wm.Up.Y:F2}, {wm.Up.Z:F2})\n\n" +
                                     $"Calculated Matrix Translation: ({calculatedMatrix.Translation.X:F2}, {calculatedMatrix.Translation.Y:F2}, {calculatedMatrix.Translation.Z:F2})\n" +
                                     $"Calculated Matrix Forward: ({calculatedMatrix.Forward.X:F2}, {calculatedMatrix.Forward.Y:F2}, {calculatedMatrix.Forward.Z:F2})\n" +
                                     $"Calculated Matrix Right: ({calculatedMatrix.Right.X:F2}, {calculatedMatrix.Right.Y:F2}, {calculatedMatrix.Right.Z:F2})\n" +
                                     $"Calculated Matrix Up: ({calculatedMatrix.Up.X:F2}, {calculatedMatrix.Up.Y:F2}, {calculatedMatrix.Up.Z:F2})\n" +
                                     $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                    _remoteControl.CustomData = customData;
                    Echo("Alignment data saved to Remote Control custom data.");
                    break;

                case "save":
                    Save();
                    Echo("All paths saved to storage!");
                    break;

                case "load":
                    Load();
                    Echo("All paths loaded from storage!");
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
            else if (_context.CurrentState is AligningState)
            {
                foreach (var light in _lights)
                {
                    light.Color = Color.Yellow;
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
                var totalPoints = _pathNavigation.HasPath(_pathNavigation.Status.pathName)
                    ? _pathNavigation.GetPathPointCount(_pathNavigation.Status.pathName)
                    : 0;
                displayText =
                    $"running:\n{_pathNavigation.Status.pathName}\n{_pathNavigation.Status.currentPathIndex + 1}/{totalPoints}";
            }
            else if (_pathNavigation.Status.recording)
            {
                var recordedPoints = _pathNavigation.HasPath(_pathNavigation.Status.pathName)
                    ? _pathNavigation.GetPathPointCount(_pathNavigation.Status.pathName)
                    : 0;
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

        #region Nested type: AligningState
        private class AligningState : State<ProgramContext>
        {
            #region Fields
            private MatrixD _targetMatrix;
            #endregion

            #region Constructors
            public AligningState(ProgramContext context) : base(context) { }
            #endregion

            #region Methods
            public override void Enter()
            {
                // Use marked alignment if available, otherwise use gravity-level orientation
                if (_context.program._markedAlignment.HasValue)
                {
                    _targetMatrix = _context.program._markedAlignment.Value;
                    _context.program.Echo("AligningState: Starting alignment to marked orientation...");
                }
                else
                {
                    _context.program.Echo("No marked alignment found. Using gravity-level orientation.");
                    _context.TransitionTo(null);
                }
            }

            public override void Execute()
            {
                if (_context.program._alignment.AlignWithWorldMatrix(_targetMatrix))
                {
                    _context.program.Echo("AligningState: Alignment complete!");
                    _context.TransitionTo(null); // Return to idle
                }
            }

            public void Stop()
            {
                _context.program._alignment.Stop();
                _context.program.Echo("AligningState: Stopped alignment.");
                _context.TransitionTo(null);
            }
            #endregion
        }
        #endregion

        #region Nested type: ProgramContext
        private class ProgramContext : Context
        {
            #region Fields
            public readonly Program program;
            #endregion

            #region Constructors
            public ProgramContext(Program program)
            {
                this.program = program;
            }
            #endregion
        }
        #endregion

        #region Nested type: RunPathState
        private class RunPathState : State<ProgramContext>
        {
            #region Fields
            private readonly bool _isReversed;
            #endregion

            #region Constructors
            public RunPathState(ProgramContext context, bool isReversed = false) : base(context)
            {
                _isReversed = isReversed;
            }
            #endregion

            #region Methods
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
                var direction = _isReversed ? "reverse" : "forward";
                _context.program.Echo($"Running path with {pointCount} points in {direction}...");
                _context.program._pathNavigation.StartPath("testPath", _isReversed);
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
                _context.TransitionTo(null);
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
                // Wait before switching direction
                _context.TransitionTo(new WaitState(_context, !_isReversed));
            }
            #endregion
        }
        #endregion

        #region Nested type: WaitState
        private class WaitState : State<ProgramContext>
        {
            #region Fields
            private const double WAIT_DURATION = 5.0; // 5 seconds
            private const double FLASH_INTERVAL = 0.5; // 0.5 seconds
            private double _lastFlashTime;
            private bool _lightsOn;
            private readonly bool _nextReversed;
            private double _startTime;
            #endregion

            #region Constructors
            public WaitState(ProgramContext context, bool nextReversed) : base(context)
            {
                _nextReversed = nextReversed;
            }
            #endregion

            #region Methods
            public override void Enter()
            {
                _startTime = DateTime.Now.TimeOfDay.TotalSeconds;
                _lastFlashTime = _startTime;
                _lightsOn = true;

                // Play completion sound
                if (_context.program._soundBlock != null)
                {
                    var sounds = new List<string>();
                    _context.program._soundBlock.GetSounds(sounds);
                    string objectiveSound = null;

                    // Search for a sound containing "objective"
                    foreach (var sound in sounds)
                    {
                        if (sound.ToLower().Contains("objective"))
                        {
                            objectiveSound = sound;
                            break;
                        }
                    }

                    if (objectiveSound != null)
                    {
                        _context.program._soundBlock.SelectedSound = objectiveSound;
                        _context.program._soundBlock.Play();
                    }
                    else
                    {
                        // Fallback: just play current sound if no objective sound found
                        _context.program._soundBlock.Play();
                    }
                }

                var direction = _nextReversed ? "reverse" : "forward";
                _context.program.Echo($"Objective Complete! Preparing to run in {direction} direction...");
            }

            public override void Execute()
            {
                var currentTime = DateTime.Now.TimeOfDay.TotalSeconds;

                // Flash lights every 0.5 seconds
                if (currentTime - _lastFlashTime >= FLASH_INTERVAL)
                {
                    _lightsOn = !_lightsOn;
                    foreach (var light in _context.program._lights)
                    {
                        light.Enabled = _lightsOn;
                    }

                    _lastFlashTime = currentTime;
                }

                // Check if wait duration is complete
                if (currentTime - _startTime >= WAIT_DURATION)
                {
                    // Turn off lights
                    foreach (var light in _context.program._lights)
                    {
                        light.Enabled = false;
                    }

                    // Transition to next direction
                    _context.TransitionTo(new RunPathState(_context, _nextReversed));
                }
            }
            #endregion
        }
        #endregion
    }
}
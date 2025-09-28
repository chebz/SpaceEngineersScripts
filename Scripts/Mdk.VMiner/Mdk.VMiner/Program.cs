using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public struct LightsSettings
    {
        public Color Color;
        public bool IsBlinking;
        public double BlinkOnDelay;
        public double BlinkOffDelay;

        public LightsSettings(Color color, bool isBlinking = false, double blinkOnDelay = 0.5, double blinkOffDelay = 0.5)
        {
            Color = color;
            IsBlinking = isBlinking;
            BlinkOnDelay = blinkOnDelay;
            BlinkOffDelay = blinkOffDelay;
        }
    }

    public partial class Program : MyGridProgram
    {
        #region Constants
        public const string DISPATCH_PATH_NAME = "DispatchPath";
        public const string DOCKING_CONNECTOR_NAME = "Docking Connector";
        #endregion

        #region Fields
        private readonly Alignment _alignment;
        private readonly Navigation _navigation;
        private readonly PathNavigation _pathNavigation;
        private readonly MinerContext _context;
        private readonly IMyTextSurface _lcdPanel;
        private readonly IMyRemoteControl _remoteControl;
        private readonly IMySensorBlock _groundSensor;
        private readonly IMyShipDrill _drill;
        private readonly IMyShipConnector _connector;
        private readonly List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
        private readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        private readonly List<IMyCargoContainer> _cargoContainers = new List<IMyCargoContainer>();
        private LightsSettings _currentLightSettings = new LightsSettings(Color.Green);
        private double _lastBlinkTime;
        private bool _lightsOn = true;
        #endregion

        #region Helper Methods
        private bool AreCargoContainersFull()
        {
            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                var maxVolume = (double)inventory.MaxVolume;
                var currentVolume = (double)inventory.CurrentVolume;
                if (currentVolume < maxVolume * 0.95) // 95% full threshold
                {
                    return false;
                }
            }
            return _cargoContainers.Count > 0; // Return true only if we have containers and they're all full
        }

        private bool AreCargoContainersEmpty()
        {
            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                var currentVolume = (double)inventory.CurrentVolume;
                if (currentVolume > 0.1) // 0.1m³ threshold for "empty"
                {
                    return false;
                }
            }
            return _cargoContainers.Count > 0; // Return true only if we have containers and they're all empty
        }
        #endregion

        #region Constructor
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            // Initialize systems
            _alignment = new Alignment();
            _navigation = new Navigation();
            _pathNavigation = new PathNavigation();

            // Find blocks
            _lights = this.GetLocalBlocks<IMyLightingBlock>();

            _gyros = this.GetLocalBlocks<IMyGyro>();
            _cargoContainers = this.GetLocalBlocks<IMyCargoContainer>();

            _lcdPanel = this.GetLocalBlock<IMyTextPanel>();
            if (_lcdPanel == null)
            {
                _lcdPanel = Me.GetSurface(0);
            }
            _lcdPanel.ContentType = ContentType.TEXT_AND_IMAGE;

            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            _groundSensor = this.GetLocalBlock<IMySensorBlock>();
            _drill = this.GetLocalBlock<IMyShipDrill>();
            _connector = this.GetLocalBlock<IMyShipConnector>(DOCKING_CONNECTOR_NAME);

            // Initialize systems
            string errorMessage;
            if (!_navigation.Initialize(this, out errorMessage))
            {
                Echo($"Navigation init failed: {errorMessage}");
                return;
            }

            if (!_alignment.Initialize(this, out errorMessage))
            {
                Echo($"Alignment init failed: {errorMessage}");
                return;
            }

            if (!_pathNavigation.Initialize(this, DOCKING_CONNECTOR_NAME, _navigation, _alignment, out errorMessage))
            {
                Echo($"PathNavigation init failed: {errorMessage}");
                return;
            }

            // Load saved paths
            Load();

            _context = new MinerContext(this);
            
            Echo("VMiner initialized successfully!");
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

            UpdateLCD();
            UpdateLights();
        }

        private void HandleCommand(string command)
        {
            switch (command)
            {
                case "startmining":
                    if (_context.CurrentState is IdleState)
                    {
                        _remoteControl.CustomData += "\nStarting mining operation...";
                        _context.TransitionTo(new DispatchingState(_context));
                    }
                    else
                    {
                        _remoteControl.CustomData += "\nMining operation already in progress!";
                    }
                    break;

                case "recordpath":
                    if (_context.CurrentState is IdleState)
                    {
                        _context.TransitionTo(new RecordPathState(_context));
                        Echo("Starting path recording...");
                    }
                    else
                    {
                        Echo("Cannot record path while operation in progress!");
                    }
                    break;

                case "markpoint":
                    if (_pathNavigation.Status.recording)
                    {
                        _pathNavigation.AddWaypoint();
                        Echo("Waypoint added!");
                    }
                    else
                    {
                        Echo("Not recording. Use 'recordpath' first.");
                    }
                    break;

                case "stoprecording":
                    if (_pathNavigation.Status.recording)
                    {
                        _pathNavigation.StopRecording();
                        Echo("Recording stopped.");
                    }
                    else
                    {
                        Echo("Not recording.");
                    }
                    break;

                case "stop":
                    _context.TransitionTo(new IdleState(_context));
                    Echo("Operation stopped.");
                    break;

                case "save":
                    Save();
                    Echo("Paths saved to storage!");
                    break;

                case "load":
                    Load();
                    Echo("Paths loaded from storage!");
                    break;

                case "printpath":
                    if (_pathNavigation.HasPath(DISPATCH_PATH_NAME))
                    {
                        PathNavigation.Path path;
                        if (_pathNavigation.GetPath(DISPATCH_PATH_NAME, out path))
                        {
                            _remoteControl.CustomData = path.ToString();
                            Echo("Path printed to remote control custom data!");
                        }
                        else
                        {
                            Echo("Error retrieving path data!");
                        }
                    }
                    else
                    {
                        Echo("No dispatch path found! Record a path first using 'recordpath'.");
                    }
                    break;

                case "align":
                    if (_context.CurrentState is IdleState)
                    {
                        _context.TransitionTo(new TestAlignState(_context));
                        Echo("Testing gravity alignment...");
                    }
                    else
                    {
                        Echo("Cannot test alignment while operation in progress!");
                    }
                    break;

                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        private void UpdateLCD()
        {
            if (_lcdPanel == null) return;

            if (_context.CurrentState is TestAlignState)
            {
                return;
            }
            if (_context.CurrentState is IdleState)
            {
                double yaw, pitch, roll;
                _context.program._alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
                _lcdPanel.WriteText($"Y: {yaw:F2}\nP: {pitch:F2}\nR: {roll:F2}");
                return;
            }
            var displayText = "=== VMiner Status ===\n";
            displayText += $"State: {_context.CurrentState?.GetType().Name ?? "Idle"}\n";
            displayText += $"PathNav: {_pathNavigation.Status.currentStateName}\n";

            if (_pathNavigation.Status.navigating)
            {
                displayText += $"Path: {_pathNavigation.Status.pathName}\n";
                displayText += $"Waypoint: {_pathNavigation.Status.currentPathIndex + 1}\n";
            }

            _lcdPanel.ContentType = ContentType.TEXT_AND_IMAGE;
            _lcdPanel.WriteText(displayText);
        }

        public void SetLightSettings(LightsSettings settings)
        {
            _currentLightSettings = settings;
            _lastBlinkTime = DateTime.Now.TimeOfDay.TotalSeconds;
            _lightsOn = true;
        }

        private void UpdateLights()
        {
            var currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
            
            // Handle blinking logic
            if (_currentLightSettings.IsBlinking)
            {
                var blinkCycle = _currentLightSettings.BlinkOnDelay + _currentLightSettings.BlinkOffDelay;
                var timeInCycle = (currentTime - _lastBlinkTime) % blinkCycle;
                
                if (timeInCycle < _currentLightSettings.BlinkOnDelay)
                {
                    _lightsOn = true;
                }
                else
                {
                    _lightsOn = false;
                }
            }
            else
            {
                _lightsOn = true;
            }

            // Apply settings to all lights
            foreach (var light in _lights)
            {
                light.Color = _currentLightSettings.Color;
                light.Enabled = _lightsOn;
            }
        }
        #endregion

        #region Nested Types
        private class MinerContext : Context
        {
            public readonly Program program;
            public Vector3D? OriginalMiningPosition { get; set; }
            public Vector3D? MiningPosition { get; set; }
            public Vector3D? CurrentMiningPosition { get; set; }

            public MinerContext(Program program)
            {
                this.program = program;
                TransitionTo(new IdleState(this));
            }
        }

        private class IdleState : State<MinerContext>
        {
            public IdleState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                // Stop all systems
                _context.program._alignment.Stop();
                _context.program._pathNavigation.StopPath();
                
                // Turn off drill if it's running
                if (_context.program._drill != null)
                {
                    _context.program._drill.Enabled = false;
                }
                
                // Set lights to solid green
                _context.program.SetLightSettings(new LightsSettings(Color.Green));
                
                // Turn off ground sensor
                if (_context.program._groundSensor != null)
                {
                    _context.program._groundSensor.Enabled = false;
                }

                _context.program.Echo("VMiner ready. Commands: 'startmining', 'recordpath', 'printpath', 'align', 'stop', 'save', 'load'");
            }

            public override void Execute()
            {
                // Wait for commands
            }
        }

        private class RecordPathState : State<MinerContext>
        {
            public RecordPathState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Recording dispatch path. Use 'markpoint' to add waypoints, 'stoprecording' to finish.");
                _context.program._pathNavigation.StartRecording(DISPATCH_PATH_NAME);
                
                // Set lights to solid red
                _context.program.SetLightSettings(new LightsSettings(Color.Red));
            }

            public override void Execute()
            {
                if (!_context.program._pathNavigation.Status.recording)
                {
                    _context.program.Echo("Path recording complete!");
                    _context.TransitionTo(new IdleState(_context));
                }
            }
        }

        private class TestAlignState : State<MinerContext>
        {
            private MatrixD _targetMatrix;
            public TestAlignState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Starting gravity alignment test...");
                double yaw, pitch, roll;
                _context.program._alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
                
                if (_context.program._lcdPanel != null)
                {
                    _context.program._lcdPanel.WriteText($"Y: {yaw:F2}\nP: {pitch:F2}\nR: {roll:F2}");
                }
                
                var currentMatrix = _context.program._remoteControl.WorldMatrix;
                var yprMatrix = _context.program._alignment.YawPitchRollToWorldMatrix(yaw, pitch, roll);
                _targetMatrix = _context.program._alignment.YawPitchRollToWorldMatrix(0, 0, 0);
                
                // Write detailed comparison to remote control's custom data
                var debugData = "=== ALIGNMENT DEBUG ===\n";
                debugData += $"Current YPR: Y:{yaw:F2} P:{pitch:F2} R:{roll:F2}\n";
                debugData += $"Target YPR: Y:0.00 P:0.00 R:0.00\n\n";
                
                debugData += "CURRENT MATRIX:\n";
                debugData += $"Fwd: ({currentMatrix.Forward.X:F2}, {currentMatrix.Forward.Y:F2}, {currentMatrix.Forward.Z:F2})\n";
                debugData += $"Rgt: ({currentMatrix.Right.X:F2}, {currentMatrix.Right.Y:F2}, {currentMatrix.Right.Z:F2})\n";
                debugData += $"Up:  ({currentMatrix.Up.X:F2}, {currentMatrix.Up.Y:F2}, {currentMatrix.Up.Z:F2})\n\n";
                
                debugData += "TARGET MATRIX (0,0,0):\n";
                debugData += $"Fwd: ({_targetMatrix.Forward.X:F2}, {_targetMatrix.Forward.Y:F2}, {_targetMatrix.Forward.Z:F2})\n";
                debugData += $"Rgt: ({_targetMatrix.Right.X:F2}, {_targetMatrix.Right.Y:F2}, {_targetMatrix.Right.Z:F2})\n";
                debugData += $"Up:  ({_targetMatrix.Up.X:F2}, {_targetMatrix.Up.Y:F2}, {_targetMatrix.Up.Z:F2})\n\n";
                
                debugData += "YPR RECONSTRUCTED MATRIX:\n";
                debugData += $"Fwd: ({yprMatrix.Forward.X:F2}, {yprMatrix.Forward.Y:F2}, {yprMatrix.Forward.Z:F2})\n";
                debugData += $"Rgt: ({yprMatrix.Right.X:F2}, {yprMatrix.Right.Y:F2}, {yprMatrix.Right.Z:F2})\n";
                debugData += $"Up:  ({yprMatrix.Up.X:F2}, {yprMatrix.Up.Y:F2}, {yprMatrix.Up.Z:F2})\n";
                
                //_context.program._remoteControl.CustomData = debugData;
                
            }

            public override void Execute()
            {
                if (_context.program._alignment.AlignWithWorldMatrix(_targetMatrix))
                {
                    _context.program.Echo("Gravity alignment complete!");
                    _context.TransitionTo(new IdleState(_context));
                }
            }
        }

        private class DispatchingState : State<MinerContext>
        {
            public DispatchingState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program._remoteControl.CustomData += "\nDispatching: Running dispatch path forward...";
                
                // Set lights to solid yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow));
                
                if (!_context.program._pathNavigation.HasPath(DISPATCH_PATH_NAME))
                {
                    _context.program._remoteControl.CustomData += "\nERROR: No dispatch path found! Record a dispatch path first using 'recordpath'.";
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                // Check if we're already undocked (not connected)
                bool isUndocked = _context.program._connector == null || !_context.program._connector.IsConnected;

                if (isUndocked)
                {
                    _context.program._remoteControl.CustomData += "\nAlready undocked - starting from waypoint 1 (mining location).";
                    _context.program._pathNavigation.StartPath(DISPATCH_PATH_NAME, false, 1); // start at index 1
                }
                else
                {
                    _context.program._remoteControl.CustomData += "\nConnected to dock - following full dispatch path.";
                    _context.program._pathNavigation.StartPath(DISPATCH_PATH_NAME, false, 0); // start at index 0
                }
            }

            public override void Execute()
            {
                if (_context.program._pathNavigation.Status.idle)
                {
                    // Path navigation complete, set original mining position and move to ground alignment
                    _context.OriginalMiningPosition = _context.program._remoteControl.GetPosition();
                    _context.TransitionTo(new AligningState(_context));
                }
            }
        }


        private class AligningState : State<MinerContext>
        {
            private double _originalBottomExtent;
            private double _minExtent;
            private double _maxExtent;
            private double _currentExtent;
            private bool _gravityAligned;
            private bool _scanningGround;
            private bool _foundGround;
            private bool _loweringToGround;
            private Vector3D _loweringTarget;
            private MatrixD _gravityLevelMatrix;
            private int _searchIteration;

            public AligningState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                // Set lights to blinking yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow, true, 0.5, 0.5));
                
                if (_context.program._groundSensor == null)
                {
                    _context.program.Echo("ERROR: No ground sensor found!");
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                // Calculate gravity-level target matrix once
                _gravityLevelMatrix = _context.program._alignment.YawPitchRollToWorldMatrix(0, 0, 0);

                // Initialize binary search parameters
                _originalBottomExtent = _context.program._groundSensor.BottomExtend;
                _minExtent = _originalBottomExtent;
                _maxExtent = _originalBottomExtent + 50.0; // 50m max search range
                _currentExtent = _originalBottomExtent;
                _searchIteration = 0;
                
                _context.program._groundSensor.Enabled = true;
                _gravityAligned = false;
                _scanningGround = false;
                _foundGround = false;
                _loweringToGround = false;
            }

            public override void Execute()
            {
                if (!_gravityAligned)
                {
                    // First align with gravity to level the ship using stored matrix
                    if (_context.program._alignment.AlignWithWorldMatrix(_gravityLevelMatrix))
                    {
                        _gravityAligned = true;
                        _scanningGround = true;
                        StartBinarySearch();
                    }
                }
                else if (_loweringToGround)
                {
                    // Navigate to the pre-calculated lowering target
                    if (_context.program._navigation.NavigateTo(_loweringTarget, 5.0))
                    {
                        // Navigation complete, restart search
                        _loweringToGround = false;
                        _scanningGround = true;
                        _minExtent = _originalBottomExtent;
                        _maxExtent = _originalBottomExtent + 50.0;
                        _currentExtent = _originalBottomExtent;
                        _foundGround = false;
                        _searchIteration = 0;
                    }
                }
                else if (_scanningGround)
                {
                    // Check if sensor detects something
                    if (_context.program._groundSensor.IsActive)
                    {
                        // Ground detected! Update search bounds
                        _foundGround = true;
                        _maxExtent = _currentExtent;
                    }
                    else
                    {
                        // No ground detected, update search bounds
                        _minExtent = _currentExtent;
                    }
                    
                    // Continue binary search
                    ContinueBinarySearch();
                }
            }
            
            private void StartBinarySearch()
            {
                // First check: original extent
                _currentExtent = _originalBottomExtent;
                _context.program._groundSensor.BottomExtend = (float)_currentExtent;
                _searchIteration = 1;
            }
            
            private void ContinueBinarySearch()
            {
                // Check if we've found ground within 0.1m precision
                if (_foundGround && (_maxExtent - _minExtent) <= 0.1)
                {
                    // Found precise ground distance! Calculate mining position
                    var distance = _maxExtent - _originalBottomExtent;
                    double elevation;
                    _context.program._remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation);
                    var debugInfo = $"Distance: {distance}\nElevation: {elevation}\nMaxExtent: {_maxExtent}\nBottomExtent: {_originalBottomExtent}";
                    _context.program._remoteControl.CustomData = debugInfo;
                    //distance = 1;
                    
                    // Calculate mining position (descend by ground distance from current position)
                    var gravityVector = _context.program._remoteControl.GetNaturalGravity();
                    if (gravityVector.LengthSquared() == 0)
                    {
                        _context.program.Echo("ERROR: No gravity detected for mining direction!");
                        CleanupAndAbort();
                        return;
                    }
                    
                    gravityVector.Normalize();
                    
                    // Mining position = current position + ground distance down
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var miningPosition = currentPos + (gravityVector * distance);
                    _context.CurrentMiningPosition = miningPosition;
                    
                    // Clean up sensor - reset to original extent and turn off
                    _context.program._groundSensor.BottomExtend = (float)_originalBottomExtent;
                    _context.program._groundSensor.Enabled = false;
                    
                    _context.TransitionTo(new LoweringDrillState(_context));
                    return;
                }
                
                // Continue binary search - calculate next extent to test
                _currentExtent = (_minExtent + _maxExtent) / 2.0;
                _context.program._groundSensor.BottomExtend = (float)_currentExtent;
                _searchIteration++;
                
                // Check if we need to move ship down
                if (!_foundGround && _currentExtent >= _maxExtent)
                {
                    // No ground found in 50m range, move ship down 40m and restart search
                    var gravity = _context.program._remoteControl.GetNaturalGravity();
                    if (gravity.LengthSquared() > 0)
                    {
                        gravity.Normalize();
                        var currentPos = _context.program._remoteControl.GetPosition();
                        _loweringTarget = currentPos + (gravity * 40.0);
                        
                        // Switch to lowering mode
                        _loweringToGround = true;
                        _scanningGround = false;
                        return;
                    }
                    else
                    {
                        _context.program.Echo("ERROR: No gravity detected. Cannot move ship down. Aborting.");
                        CleanupAndAbort();
                        return;
                    }
                }
            }
            
            private void CleanupAndAbort()
            {
                // Clean up sensor - reset to original extent and turn off
                _context.program._groundSensor.BottomExtend = (float)_originalBottomExtent;
                _context.program._groundSensor.Enabled = false;
                _context.TransitionTo(new IdleState(_context));
            }
        }

        private class LoweringDrillState : State<MinerContext>
        {
            public LoweringDrillState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Lowering drill to mining depth...");
                
                // Set lights to blinking yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow, true, 0.5, 0.5));
            }

            public override void Execute()
            {
                // Check if we have a valid mining position
                if (!_context.CurrentMiningPosition.HasValue)
                {
                    _context.program.Echo("ERROR: No mining position calculated!");
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                var miningPosition = _context.CurrentMiningPosition.Value;

                // Check if we're close enough to start drilling (1m away)
                var currentPos = _context.program._remoteControl.GetPosition();
                var distanceToTarget = Vector3D.Distance(currentPos, miningPosition);
                
                // Navigate to mining depth - returns true when complete
                // OR transition if we're within 1m of target
                if (_context.program._navigation.NavigateTo(miningPosition, 5.0))
                {
                    _context.TransitionTo(new MiningState(_context));
                }
                if (distanceToTarget < 1.0)
                {
                    _context.program._drill.Enabled = true;
                }
            }
        }

        private class MiningState : State<MinerContext>
        {
            private const double CIRCLE_DURATION = 30.0;
            private const double YAW_SPEED = 0.5; // radians per second
            private const double PITCH_ANGLE = 10.0 * Math.PI / 180.0; // 10 degrees in radians
            private const double PITCH_ERROR_FACTOR = 0.25;
            private double _circleStartTime;
            private bool _miningStarted;

            public MiningState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _miningStarted = false;
                
                // Set lights to blinking yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow, true, 0.5, 0.5));
            }

            public override void Execute()
            {
                if (!_miningStarted)
                {
                    // Start mining - align with gravity first
                    var gravityLevelMatrix = _context.program._alignment.YawPitchRollToWorldMatrix(0, 0, 0);
                    if (!_context.program._alignment.AlignWithWorldMatrix(gravityLevelMatrix)) {
                        return;
                    }
                    
                    _circleStartTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    _miningStarted = true;
                }

                // Perform circular mining with constant pitch and roll
                if (DateTime.Now.TimeOfDay.TotalSeconds - _circleStartTime >= CIRCLE_DURATION)
                {
                    // Stop gyros and transition to AligningState to recalculate depth
                    foreach (var gyro in _context.program._gyros)
                    {
                        gyro.GyroOverride = true;
                        gyro.Yaw = 0;
                        gyro.Pitch = 0;
                        gyro.Roll = 0;
                    }
                    _miningStarted = false;
                    _context.TransitionTo(new AligningState(_context));
                    return;
                }

                // Check if cargo containers are full
                if (_context.program.AreCargoContainersFull())
                {
                    // Stop gyros and transition to AligningState to recalculate depth
                    foreach (var gyro in _context.program._gyros)
                    {
                        gyro.GyroOverride = true;
                        gyro.Yaw = 0;
                        gyro.Pitch = 0;
                        gyro.Roll = 0;
                    }
                    // Stop drilling and return to surface
                    _context.program._drill.Enabled = false;
                    _context.TransitionTo(new RaisingDrillState(_context));
                    return;
                }

                // Apply circular mining forces
                double yaw, pitch, roll;
                _context.program._alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
                var pitchError = (pitch - PITCH_ANGLE) * PITCH_ERROR_FACTOR; // dampen pitch error
                var pitchOutput = _context.program._alignment.PidVectorPitch.Control(pitchError);

                foreach (var gyro in _context.program._gyros)
                {
                    gyro.GyroOverride = true;
                    gyro.Yaw = (float)YAW_SPEED;
                    gyro.Pitch = (float)pitchOutput;
                    gyro.Roll = (float)roll;
                }
            }
        }

        private class RaisingDrillState : State<MinerContext>
        {
            public RaisingDrillState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Raising drill to surface...");
                
                // Set lights to blinking yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow, true, 0.5, 0.5));
            }

            public override void Execute()
            {
                // Check if we have a valid original position
                if (!_context.OriginalMiningPosition.HasValue)
                {
                    _context.program.Echo("ERROR: No original position stored!");
                    _context.TransitionTo(new IdleState(_context));
                    return;
                }

                var surfacePosition = _context.OriginalMiningPosition.Value;

                // Navigate back to surface - returns true when complete
                if (_context.program._navigation.NavigateTo(surfacePosition, 5.0))
                {
                    // At surface, start returning along path
                    _context.TransitionTo(new ReturningState(_context));
                }
            }
        }

        private class UnloadingState : State<MinerContext>
        {
            public UnloadingState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Waiting for cargo to be unloaded...");
                
                // Set lights to blinking green (waiting for unloading)
                _context.program.SetLightSettings(new LightsSettings(Color.Green, true, 0.5, 0.5));
            }

            public override void Execute()
            {
                // Check if cargo containers are empty
                if (_context.program.AreCargoContainersEmpty())
                {
                    _context.program.Echo("Cargo unloaded! Restarting mining cycle...");
                    _context.TransitionTo(new DispatchingState(_context));
                }
            }
        }

        private class ReturningState : State<MinerContext>
        {
            public ReturningState(MinerContext context) : base(context) { }

            public override void Enter()
            {
                _context.program.Echo("Returning along dispatch path...");
                
                // Set lights to solid yellow
                _context.program.SetLightSettings(new LightsSettings(Color.Yellow));
                
                // Start dispatch path in reverse to return home
                _context.program._pathNavigation.StartPath(DISPATCH_PATH_NAME, true);
            }

            public override void Execute()
            {
                // Wait for dispatch path to complete
                if (_context.program._pathNavigation.Status.idle)
                {
                    _context.program.Echo("Returned to base! Waiting for cargo unloading...");
                    _context.TransitionTo(new UnloadingState(_context));
                }
            }
        }
        #endregion
    }
}

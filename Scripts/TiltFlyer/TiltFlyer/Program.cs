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
        #region Constants
        private const double HOVER_HEIGHT = 5.0; // meters above ground
        private const double MOVE_DISTANCE = 20.0; // meters forward
        #endregion

        #region Fields
        private CustomDataConnector _customDataConnector;
        private TiltNavigation _tiltNavigation;
        private Alignment _alignment;
        private Navigation _navigation;
        private IMyRemoteControl _remoteControl;
        private List<IMyLightingBlock> _lights;
        private TiltFlyerContext _context;
        private IMyEmotionControllerBlock _emotionController;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Initialize systems
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _tiltNavigation = new TiltNavigation();
            _alignment = new Alignment();
            _navigation = new Navigation();
            _lights = this.GetLocalBlocks<IMyLightingBlock>();
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            _emotionController = this.GetLocalBlock<IMyEmotionControllerBlock>();
            // Initialize systems
            string errorMessage;
            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }
            _alignment.Precision = 0.1;
            // Parse PID values from custom data
            ParseAlignmentPidValues();

            if (!_navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_tiltNavigation.Initialize(this, _alignment, _navigation, out errorMessage))
            {
                Echo($"TiltNavigation Error: {errorMessage}");
            }

            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
            }

            Echo($"Found {_lights.Count} lights");
            
            _context = new TiltFlyerContext(this);
        }

        private void ParseAlignmentPidValues()
        {
            // Parse PID values from custom data for alignment and navigation
            // Format: One key=value per line
            // KP=10
            // KI=0
            // KD=0
            // NAV_KP=3.0
            // NAV_KI=1.0
            // NAV_KD=0.0
            // MAX_SPEED_ERROR=10.0
            // ACCEL=0.25
            var customData = Me.CustomData;
            if (string.IsNullOrEmpty(customData))
            {
                Echo("TiltFlyer: No custom data found, using default PID values");
                return;
            }

            var lines = customData.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                    continue;

                var keyValue = trimmedLine.Split('=');
                if (keyValue.Length != 2)
                    continue;

                var key = keyValue[0].Trim().ToUpper();
                var value = keyValue[1].Trim();

                double pidValue;
                if (double.TryParse(value, out pidValue))
                {
                    switch (key)
                    {
                        case "KP":
                            _alignment.PidVectorYaw.Kp = pidValue;
                            _alignment.PidVectorPitch.Kp = pidValue;
                            _alignment.PidVectorRoll.Kp = pidValue;
                            Echo($"TiltFlyer: Set Alignment KP = {pidValue}");
                            break;
                        case "KI":
                            _alignment.PidVectorYaw.Ki = pidValue;
                            _alignment.PidVectorPitch.Ki = pidValue;
                            _alignment.PidVectorRoll.Ki = pidValue;
                            Echo($"TiltFlyer: Set Alignment KI = {pidValue}");
                            break;
                        case "KD":
                            _alignment.PidVectorYaw.Kd = pidValue;
                            _alignment.PidVectorPitch.Kd = pidValue;
                            _alignment.PidVectorRoll.Kd = pidValue;
                            Echo($"TiltFlyer: Set Alignment KD = {pidValue}");
                            break;
                        case "NAV_KP":
                            _navigation.PidXPos.Kp = pidValue;
                            _navigation.PidYPos.Kp = pidValue;
                            _navigation.PidZPos.Kp = pidValue;
                            Echo($"TiltFlyer: Set Navigation KP = {pidValue}");
                            break;
                        case "NAV_KI":
                            _navigation.PidXPos.Ki = pidValue;
                            _navigation.PidYPos.Ki = pidValue;
                            _navigation.PidZPos.Ki = pidValue;
                            Echo($"TiltFlyer: Set Navigation KI = {pidValue}");
                            break;
                            case "NAV_KD":
                                _navigation.PidXPos.Kd = pidValue;
                                _navigation.PidYPos.Kd = pidValue;
                                _navigation.PidZPos.Kd = pidValue;
                                Echo($"TiltFlyer: Set Navigation KD = {pidValue}");
                                break;
                            case "MAX_SPEED_ERROR":
                                _tiltNavigation.MaxSpeedError = pidValue;
                                Echo($"TiltFlyer: Set Max Speed Error = {pidValue}");
                                break;
                            case "ACCEL":
                                _tiltNavigation.Accel = pidValue;
                                Echo($"TiltFlyer: Set Acceleration = {pidValue}");
                                break;
                    }
                }
            }
        }

        public void Save()
        {
            // Called when the program needs to save its state
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }

            _context.Execute();
        }

        private void HandleCommand(string argument)
        {
            var command = argument.ToLower();

            switch (command)
            {
                case "toggle":
                    _context.Toggle();
                    break;

                case "stop":
                    _context.Stop();
                    break;

                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        public void SetLightSettings(Color color, bool isBlinking = false, double blinkOnDelay = 0.5, double blinkOffDelay = 0.5)
        {
            foreach (var light in _lights)
            {
                light.Color = color;
                if (isBlinking)
                {
                    light.BlinkIntervalSeconds = (float)(blinkOnDelay + blinkOffDelay);
                    light.BlinkLength = (float)(blinkOnDelay / (blinkOnDelay + blinkOffDelay));
                }
                else
                {
                    light.BlinkIntervalSeconds = 0;
                    light.BlinkLength = 0;
                }
                light.Enabled = true;
            }
        }

        public class TiltFlyerContext : Context
        {
            public Program program;

            public TiltFlyerContext(Program program)
            {
                this.program = program;
                TransitionTo(new IdleState(this));
            }

            public void Toggle()
            {
                if (CurrentState is IdleState)
                {
                    TransitionTo(new MovingState(this));
                }
                else
                {
                    TransitionTo(new IdleState(this));
                }
            }

            public void Stop()
            {
                program.Echo("Stopping all operations...");
                TransitionTo(new IdleState(this));
            }

            private class IdleState : State<TiltFlyerContext>
            {
                private Vector3D _hoverPosition;

                public IdleState(TiltFlyerContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program._alignment.Stop();
                    _context.program.SetLightSettings(Color.Green, false); // Solid green
                    _context.program.Echo("Idle State - Maintaining hover");
                
                    // Calculate hover position 10m above ground
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var gravity = _context.program._remoteControl.GetNaturalGravity();
                    double elevation;
                    _context.program._remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation);
                    var height = HOVER_HEIGHT - elevation;
                    
                    if (gravity.LengthSquared() > 0)
                    {
                        // Use gravity direction to find "down"
                        var gravityNormalized = Vector3D.Normalize(gravity);
                        _hoverPosition = currentPos + (-gravityNormalized * height);
                    }
                    else
                    {
                        // No gravity, just hover at current position
                        _hoverPosition = currentPos;
                    }
                    
                    _context.program.Echo($"Hover position set: {_hoverPosition}");
                }

                public override void Execute()
                {
                    // Navigate to hover position using TiltNavigation with original algorithm
                    if (_context.program._tiltNavigation.NavigateTo(_hoverPosition, 5.0))
                    {
                        _context.program.Echo("At hover position - maintaining");
                    }
                }
            }

            private class MovingState : State<TiltFlyerContext>
            {
                private List<Vector3D> _path;
                private int _currentPoint = 0;
                private const double SQUARE_SIZE = 20.0; // 20m per side
                private bool _aligned = false;
                private bool _aligning = false;
                private MatrixD _targetMatrix;

                public MovingState(TiltFlyerContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Yellow, true, 0.5, 0.5); // Blinking yellow
                    
                    // Create the square path
                    CreateSquarePath();
                    _currentPoint = 0;

                    _context.program._navigation.Precision = 1.0;
                }
                
                private void CreateSquarePath()
                {
                    var startPosition = _context.program._remoteControl.GetPosition();
                    var gravity = _context.program._remoteControl.GetNaturalGravity();
                    var up = -Vector3D.Normalize(gravity);
                    
                    // Get forward and right vectors perpendicular to gravity
                    var forward = _context.program._remoteControl.WorldMatrix.Forward;
                    var right = _context.program._remoteControl.WorldMatrix.Right;
                    
                    // Project onto horizontal plane (perpendicular to gravity)
                    var forwardH = Vector3D.Normalize(forward - Vector3D.Dot(forward, up) * up);
                    var rightH = Vector3D.Normalize(right - Vector3D.Dot(right, up) * up);
                    
                    // Create the square path: Right → Forward → Left → Back
                    _path = new List<Vector3D>
                    {
                        startPosition + (rightH * SQUARE_SIZE), // Point 1: Right 20m
                        startPosition + (rightH * SQUARE_SIZE) + (forwardH * SQUARE_SIZE), // Point 2: Right + Forward
                        startPosition + (forwardH * SQUARE_SIZE), // Point 3: Forward 20m
                        startPosition // Point 4: Back to start
                    };
                    
                }

                public override void Execute()
                {
                    // Check if we've completed all points in the path
                    if (_currentPoint >= _path.Count)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }


                    // Get current target from path
                    var currentTarget = _path[_currentPoint];

                    if (!_aligned)
                    {
                        _context.program._navigation.Stop();
                        if (!_aligning) 
                        {
                            var targetYaw = -_context.program._alignment.CalculateYawToTarget(currentTarget);
                            _targetMatrix = _context.program._alignment.YawPitchRollToWorldMatrix(targetYaw, 0, 0);
                            _aligning = true;
                        }
                        else if (_context.program._alignment.AlignWithWorldMatrix(_targetMatrix))
                        {
                            _aligned = true;
                        }
                        return;
                    }
                    
                    
                    // Navigate to current target using original algorithm
                    if (_context.program._tiltNavigation.NavigateTo(currentTarget, 10.0))
                    {
                        _aligned = false;
                        _aligning = false;
                        _currentPoint++;
                    }
                }
            }
        }
    }
}
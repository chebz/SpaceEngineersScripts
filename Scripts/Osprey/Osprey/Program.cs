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
        private const double HOVER_HEIGHT = 10.0; // meters above ground
        private const double MOVE_DISTANCE = 20.0; // meters forward
        #endregion

        #region Fields
        private CustomDataConnector _customDataConnector;
        private TiltRotorNav _tiltRotorNav;
        private Alignment _alignment;
        private Navigation _navigation;
        private IMyRemoteControl _remoteControl;
        private List<IMyLightingBlock> _lights;
        private OspreyContext _context;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            // Initialize systems
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _tiltRotorNav = new TiltRotorNav();
            _alignment = new Alignment();
            _navigation = new Navigation();
            _lights = this.GetLocalBlocks<IMyLightingBlock>();
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();

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

            if (!_tiltRotorNav.Initialize(this, _alignment, _navigation, out errorMessage))
            {
                Echo($"TiltRotorNav Error: {errorMessage}");
            }

            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
            }

            Echo($"Found {_lights.Count} lights");

            _context = new OspreyContext(this);
        }

        private void ParseAlignmentPidValues()
        {
            // Parse PID values from custom data using CustomDataConnector
            // Format: One key:value per line
            // KP:10
            // KI:0
            // KD:0
            // NAV_KP:3.0
            // NAV_KI:1.0
            // NAV_KD:0.0
            // MAX_SPEED_ERROR:10.0
            // ACCEL:0.25

            double pidValue;

            // Parse alignment PID values
            if (CustomDataConnector.ParseDouble(Me, "KP", out pidValue))
            {
                _alignment.PidVectorYaw.Kp = pidValue;
                _alignment.PidVectorPitch.Kp = pidValue;
                _alignment.PidVectorRoll.Kp = pidValue;
                Echo($"Osprey: Set Alignment KP = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KI", out pidValue))
            {
                _alignment.PidVectorYaw.Ki = pidValue;
                _alignment.PidVectorPitch.Ki = pidValue;
                _alignment.PidVectorRoll.Ki = pidValue;
                Echo($"Osprey: Set Alignment KI = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KD", out pidValue))
            {
                _alignment.PidVectorYaw.Kd = pidValue;
                _alignment.PidVectorPitch.Kd = pidValue;
                _alignment.PidVectorRoll.Kd = pidValue;
                Echo($"Osprey: Set Alignment KD = {pidValue}");
            }

            // Parse navigation PID values
            if (CustomDataConnector.ParseDouble(Me, "NAV_KP", out pidValue))
            {
                _navigation.PidXPos.Kp = pidValue;
                _navigation.PidYPos.Kp = pidValue;
                _navigation.PidZPos.Kp = pidValue;
                Echo($"Osprey: Set Navigation KP = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "NAV_KI", out pidValue))
            {
                _navigation.PidXPos.Ki = pidValue;
                _navigation.PidYPos.Ki = pidValue;
                _navigation.PidZPos.Ki = pidValue;
                Echo($"Osprey: Set Navigation KI = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "NAV_KD", out pidValue))
            {
                _navigation.PidXPos.Kd = pidValue;
                _navigation.PidYPos.Kd = pidValue;
                _navigation.PidZPos.Kd = pidValue;
                Echo($"Osprey: Set Navigation KD = {pidValue}");
            }

            // Parse other configuration values
            if (CustomDataConnector.ParseDouble(Me, "MAX_SPEED_ERROR", out pidValue))
            {
                _tiltRotorNav.MaxSpeedError = pidValue;
                Echo($"Osprey: Set Max Speed Error = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "ACCEL", out pidValue))
            {
                _tiltRotorNav.Accel = pidValue;
                Echo($"Osprey: Set Acceleration = {pidValue}");
            }

            // Parse rotor control values
            if (CustomDataConnector.ParseDouble(Me, "ROTOR_SPEED", out pidValue))
            {
                _tiltRotorNav.RotorRotationSpeed = pidValue;
                Echo($"Osprey: Set Rotor Rotation Speed = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "ROTOR_EXP", out pidValue))
            {
                _tiltRotorNav.RotorRotationSpeedExp = pidValue;
                Echo($"Osprey: Set Rotor Rotation Speed Exp = {pidValue}");
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

                case "hover":
                    _context.Hover();
                    break;

                case "patrol":
                    _context.Patrol();
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

        public class OspreyContext : Context
        {
            public Program program;

            public OspreyContext(Program program)
            {
                this.program = program;
                TransitionTo(new HoverState(this));
            }

            public void Toggle()
            {
                if (CurrentState is IdleState)
                {
                    TransitionTo(new HoverState(this));
                }
                else if (CurrentState is HoverState)
                {
                    TransitionTo(new MoveRightState(this));
                }
                else if (CurrentState is MoveRightState)
                {
                    TransitionTo(new MoveForwardState(this));
                }
                else if (CurrentState is MoveForwardState)
                {
                    TransitionTo(new MoveLeftState(this));
                }
                else if (CurrentState is MoveLeftState)
                {
                    TransitionTo(new MoveBackState(this));
                }
                else if (CurrentState is MoveBackState)
                {
                    TransitionTo(new IdleState(this));
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

            public void Hover()
            {
                program.Echo("Transitioning to Hover State...");
                TransitionTo(new HoverState(this));
            }

            public void Patrol()
            {
                program.Echo("Starting Patrol State...");
                TransitionTo(new PatrolState(this));
            }

            private class IdleState : State<OspreyContext>
            {
                public IdleState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program._alignment.Stop();
                    _context.program._tiltRotorNav.Stop();
                    _context.program._tiltRotorNav.PowerOff();
                    _context.program.SetLightSettings(Color.Red, false);
                    _context.program.Echo("Idle State - Doing nothing");
                }

                public override void Execute()
                {
                }
            }

            private class AlignToBeaconState : State<OspreyContext>
            {
                public int _beaconIndex;
                public List<IMyBeacon> _beacons = new List<IMyBeacon>();
                private IMyBeacon _currentBeacon;

                public AlignToBeaconState(int beaconIndex, OspreyContext context) : base(context) 
                { 
                    _beaconIndex = beaconIndex;
                }

                public override void Enter()
                {
                    _context.program._tiltRotorNav.PowerOff();
                    _context.program.SetLightSettings(Color.Yellow, true, 0.5, 0.5);

                    _context.program.GridTerminalSystem.GetBlocksOfType(_beacons);
                    foreach (var beacon in _beacons)
                    {
                        beacon.Enabled = false;
                    }
                    
                    if (_beaconIndex < _beacons.Count)
                    {
                        _currentBeacon = _beacons[_beaconIndex];
                        _currentBeacon.Enabled = true;
                        _context.program.Echo($"AlignToBeacon State - Beacon {_beaconIndex + 1}/{_beacons.Count}: {_currentBeacon.CustomName}");
                    }
                    else
                    {
                        _context.program.Echo("Warning: Invalid beacon index");
                    }
                }

                public override void Execute()
                {
                    if (_currentBeacon == null)
                    {
                        _context.program.Echo("AlignToBeacon: No beacon available");
                        return;
                    }
                    //_context.program._tiltRotorNav.NavigateTo(_currentBeacon.GetPosition(), 10.0, true);
                    var beaconPosition = _currentBeacon.GetPosition();
                    var yaw =_context.program._alignment.CalculateYawToTarget(beaconPosition);
                    _context.program.Echo($"AlignToBeacon: Yaw to target: {yaw:F2}");
                    _context.program._alignment.AlignWithYawPitchRoll(yaw, 0, 0);
                }
            }

            private class HoverState : State<OspreyContext>
            {
                private Vector3D _hoverPosition;
                private double _targetYaw;

                public HoverState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program._tiltRotorNav.PowerOn();
                    _context.program.SetLightSettings(Color.Green, false); // Solid green
                    _context.program.Echo("Hover State - Maintaining hover");

                    // Get current ship yaw to maintain current orientation
                    double currentYaw, currentPitch, currentRoll;
                    _context.program._alignment.CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);
                    _targetYaw = currentYaw;

                    // Calculate hover position 5m above ground
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

                    _context.program.Echo($"Hover position set: {_hoverPosition}, maintaining yaw: {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    // Navigate to hover position using TiltRotorNav, maintaining current yaw
                    if (_context.program._tiltRotorNav.NavigateTo(_hoverPosition, _targetYaw, 5.0))
                    {
                        _context.program.Echo("At hover position - maintaining");
                    }
                }
            }

            private class MoveForwardState : State<OspreyContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;
                private const double MOVE_DISTANCE = 20.0;

                public MoveForwardState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Cyan, true, 0.3, 0.3); // Blinking cyan
                    _context.program._tiltRotorNav.PowerOn();

                    // Calculate target position 20m forward
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var forward = _context.program._remoteControl.WorldMatrix.Forward;
                    _targetPosition = currentPos + (forward * MOVE_DISTANCE);

                    // Calculate target yaw using existing alignment method
                    _targetYaw = _context.program._alignment.CalculateYawToTarget(_targetPosition);

                    _context.program.Echo($"MoveFWD State - Moving forward 20m to {_targetPosition}, yaw: {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    // Navigate to target position using TiltRotorNav
                    if (_context.program._tiltRotorNav.NavigateTo(_targetPosition, 0, 10.0))
                    {
                        _context.program.Echo("MoveForward State - Reached target");
                    }
                }
            }

            private class MoveRightState : State<OspreyContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;
                private const double MOVE_DISTANCE = 20.0;

                public MoveRightState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Magenta, true, 0.3, 0.3); // Blinking magenta
                    _context.program._tiltRotorNav.PowerOn();
                    // Calculate target position 20m right
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var right = _context.program._remoteControl.WorldMatrix.Right;
                    _targetPosition = currentPos + (right * MOVE_DISTANCE);

                    // Calculate target yaw using existing alignment method
                    _targetYaw = _context.program._alignment.CalculateYawToTarget(_targetPosition);

                    _context.program.Echo($"MoveRight State - Moving right 20m to {_targetPosition}, yaw: {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    // Navigate to target position using TiltRotorNav
                    if (_context.program._tiltRotorNav.NavigateTo(_targetPosition, 0, 10.0))
                    {
                        _context.program.Echo("MoveRight State - Reached target");
                    }
                }
            }

            private class MoveBackState : State<OspreyContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;
                private const double MOVE_DISTANCE = 20.0;

                public MoveBackState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Orange, true, 0.3, 0.3); // Blinking orange
                    _context.program._tiltRotorNav.PowerOn();
                    // Calculate target position 20m backward
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var forward = _context.program._remoteControl.WorldMatrix.Forward;
                    _targetPosition = currentPos - (forward * MOVE_DISTANCE);

                    // Calculate target yaw using existing alignment method
                    _targetYaw = _context.program._alignment.CalculateYawToTarget(_targetPosition);

                    _context.program.Echo($"MoveBack State - Moving backward 20m to {_targetPosition}, yaw: {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    // Navigate to target position using TiltRotorNav
                    if (_context.program._tiltRotorNav.NavigateTo(_targetPosition, 0, 10.0))
                    {
                        _context.program.Echo("MoveBack State - Reached target");
                    }
                }
            }

            private class MoveLeftState : State<OspreyContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;
                private const double MOVE_DISTANCE = 20.0;

                public MoveLeftState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Purple, true, 0.3, 0.3); // Blinking purple
                    _context.program._tiltRotorNav.PowerOn();
                    // Calculate target position 20m left
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var right = _context.program._remoteControl.WorldMatrix.Right;
                    _targetPosition = currentPos - (right * MOVE_DISTANCE);

                    // Calculate target yaw using existing alignment method
                    _targetYaw = _context.program._alignment.CalculateYawToTarget(_targetPosition);

                    _context.program.Echo($"MoveLeft State - Moving left 20m to {_targetPosition}, yaw: {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    // Navigate to target position using TiltRotorNav
                    if (_context.program._tiltRotorNav.NavigateTo(_targetPosition, 0, 10.0))
                    {
                        _context.program.Echo("MoveLeft State - Reached target");
                    }
                }
            }

            private class PatrolState : State<OspreyContext>
            {
                private List<Vector3D> _patrolPoints = new List<Vector3D>();
                private int _currentPointIndex = 0;
                private Vector3D _hoverPosition;
                private bool _patrolInitialized = false;
                private const double PATROL_DISTANCE = 200.0; // 200m sides for rectangle
                private const double HOVER_HEIGHT = 50.0; // 20m above surface
                private bool _calculatedTargetYaw = false;
                private double _targetYaw = 0;
                public PatrolState(OspreyContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Yellow, true, 0.5, 0.5); // Blinking yellow
                    _context.program._tiltRotorNav.PowerOn();
                    _context.program.Echo("Patrol State - Initializing patrol pattern");

                    // Calculate patrol center position at HOVER_HEIGHT above surface
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

                    // Initialize all 4 patrol points at HOVER_HEIGHT, perpendicular to gravity
                    InitializePatrolPoints();

                    _patrolInitialized = false;
                    _currentPointIndex = 0;

                    _context.program.Echo($"Patrol pattern initialized at altitude {HOVER_HEIGHT}m");
                    _context.program.Echo($"Center position: {_hoverPosition}");
                }

                public override void Execute()
                {
                    if (!_patrolInitialized)
                    {
                        // First, navigate to patrol altitude and maintain current yaw
                        double currentYaw, currentPitch, currentRoll;
                        _context.program._alignment.CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);

                        var distanceToAltitude = Vector3D.Distance(_context.program._remoteControl.GetPosition(), _hoverPosition);
                        _context.program.Echo($"Climbing to patrol altitude... Distance: {distanceToAltitude:F1}m");

                        if (_context.program._tiltRotorNav.NavigateTo(_hoverPosition, currentYaw, 10.0))
                        {
                            // Reached patrol altitude, start patrol pattern
                            _patrolInitialized = true;
                            _context.program.Echo("✓ Patrol altitude reached - starting patrol pattern");
                        }
                    }
                    else
                    {
                        // Execute patrol pattern
                        if (_patrolPoints.Count == 0)
                            return;

                        var currentTarget = _patrolPoints[_currentPointIndex];
                        var distanceToTarget = Vector3D.Distance(_context.program._remoteControl.GetPosition(), currentTarget);
                        
                        if (!_calculatedTargetYaw)
                        {
                            // Calculate yaw to face the target
                            _targetYaw = _context.program._alignment.CalculateYawToTarget(currentTarget);
                            var targetYawDegrees = _targetYaw * 180.0 / Math.PI;
                            _calculatedTargetYaw = true;
                            _context.program.Echo($"Patrol Point {_currentPointIndex + 1}/4 - Distance: {distanceToTarget:F1}m, Target Yaw: {targetYawDegrees:F1}°");
                        }

                        // Navigate to current patrol point
                        if (_context.program._tiltRotorNav.NavigateTo(currentTarget, _targetYaw, 15.0))
                        {
                            _context.program._navigation.Stop();
                            // Point reached, move to next point
                            _calculatedTargetYaw = false;
                            var nextIndex = (_currentPointIndex + 1) % _patrolPoints.Count;
                            var nextTarget = _patrolPoints[nextIndex];
                            _context.program.Echo($"✓ Reached Point {_currentPointIndex + 1}! Moving to Point {nextIndex + 1}");
                            _context.program.Echo($"Next target: {nextTarget}");
                            _currentPointIndex = nextIndex;
                        }
                    }
                }

                private void InitializePatrolPoints()
                {
                    // Get gravity and current orientation
                    var gravity = _context.program._remoteControl.GetNaturalGravity();
                    var upVector = gravity.LengthSquared() > 0 ? -Vector3D.Normalize(gravity) : Vector3D.Up;
                    
                    // Project current forward and right vectors onto horizontal plane (perpendicular to gravity)
                    var currentForward = _context.program._remoteControl.WorldMatrix.Forward;
                    var currentRight = _context.program._remoteControl.WorldMatrix.Right;
                    
                    var forwardHorizontal = currentForward - Vector3D.Dot(currentForward, upVector) * upVector;
                    var rightHorizontal = currentRight - Vector3D.Dot(currentRight, upVector) * upVector;
                    
                    // Normalize the horizontal vectors
                    if (forwardHorizontal.LengthSquared() > 1e-6)
                        forwardHorizontal = Vector3D.Normalize(forwardHorizontal);
                    else
                        forwardHorizontal = new Vector3D(1, 0, 0); // Fallback to world X-axis
                        
                    if (rightHorizontal.LengthSquared() > 1e-6)
                        rightHorizontal = Vector3D.Normalize(rightHorizontal);
                    else
                        rightHorizontal = new Vector3D(0, 1, 0); // Fallback to world Y-axis

                    // Create rectangle points perpendicular to gravity
                    _patrolPoints.Clear();
                    _patrolPoints.Add(_hoverPosition + (forwardHorizontal * PATROL_DISTANCE)); // Forward
                    _patrolPoints.Add(_hoverPosition + (forwardHorizontal * PATROL_DISTANCE) + (rightHorizontal * PATROL_DISTANCE)); // Forward-Right
                    _patrolPoints.Add(_hoverPosition + (rightHorizontal * PATROL_DISTANCE)); // Right
                    _patrolPoints.Add(_hoverPosition); // Back to start

                    _context.program.Echo($"Patrol course plotted with {_patrolPoints.Count} waypoints at {HOVER_HEIGHT}m altitude");
                    _context.program.Echo("Points positioned perpendicular to gravity:");
                    for (int i = 0; i < _patrolPoints.Count; i++)
                    {
                        _context.program.Echo($"  Point {i + 1}: {_patrolPoints[i]}");
                    }
                }
            }
        }
    }
}

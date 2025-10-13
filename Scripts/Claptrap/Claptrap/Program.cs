using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Weapons;
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
        #region Fields
        private CustomDataConnector _customDataConnector;
        private Alignment _alignment;
        private RoverNavigation _roverNavigation;
        private IMyRemoteControl _remoteControl;
        private List<IMyLightingBlock> _lights;
        private ClaptrapContext _context;
        private IMyShipWelder _welder;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _alignment = new Alignment();
            _roverNavigation = new RoverNavigation();
            _lights = this.GetLocalBlocks<IMyLightingBlock>();
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();

            string errorMessage;
            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }
            _alignment.Precision = 0.05;

            ParseAlignmentPidValues();

            if (!_roverNavigation.Initialize(this, out errorMessage))
            {
                Echo($"RoverNavigation Error: {errorMessage}");
            }

            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
            }

            Echo($"Found {_lights.Count} lights");

            _context = new ClaptrapContext(this);
        }

        private void ParseAlignmentPidValues()
        {
            double pidValue;

            if (CustomDataConnector.ParseDouble(Me, "KP", out pidValue))
            {
                _alignment.PidVectorYaw.Kp = pidValue;
                _alignment.PidVectorPitch.Kp = pidValue;
                _alignment.PidVectorRoll.Kp = pidValue;
                Echo($"Claptrap: Set Alignment KP = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KI", out pidValue))
            {
                _alignment.PidVectorYaw.Ki = pidValue;
                _alignment.PidVectorPitch.Ki = pidValue;
                _alignment.PidVectorRoll.Ki = pidValue;
                Echo($"Claptrap: Set Alignment KI = {pidValue}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KD", out pidValue))
            {
                _alignment.PidVectorYaw.Kd = pidValue;
                _alignment.PidVectorPitch.Kd = pidValue;
                _alignment.PidVectorRoll.Kd = pidValue;
                Echo($"Claptrap: Set Alignment KD = {pidValue}");
            }
        }

        public void Save()
        {
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
                case "stop":
                    _context.Stop();
                    break;

                case "start":
                    _context.Start();
                    break;

                case "wonder":
                    _context.Wonder();
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

        public class ClaptrapContext : Context
        {
            public Program program;

            public ClaptrapContext(Program program)
            {
                this.program = program;
                TransitionTo(new IdleState(this));
            }

            public void Stop()
            {
                program.Echo("Stopping Claptrap...");
                TransitionTo(new IdleState(this));
            }

            public void Start()
            {
                program.Echo("Starting Claptrap...");
                TransitionTo(new IdleState(this));
            }

            public void Wonder()
            {
                program.Echo("Starting Wonder mode...");
                TransitionTo(new RandomPatrolState(this));
            }

            private double NormalizeAngle(double angle)
            {
                while (angle > Math.PI)
                    angle -= 2 * Math.PI;
                while (angle < -Math.PI)
                    angle += 2 * Math.PI;
                return angle;
            }

            private class IdleState : State<ClaptrapContext>
            {
                private double _targetYaw;
                public IdleState(ClaptrapContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Green, true, 0.5, 0.5);
                    double currentYaw, currentPitch, currentRoll;
                    _context.program._alignment.CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);
                    _targetYaw = currentYaw;
                    _context.program.Echo("Idle State: Maintaining alignment to 0,0,0 YPR");
                }

                public override void Execute()
                {
                    _context.program._alignment.AlignWithYawPitchRoll(-_targetYaw, 0, 0);
                }
            }

            private class RandomPatrolState : State<ClaptrapContext>
            {
                private Vector3D _targetPosition;

                public RandomPatrolState(ClaptrapContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Yellow, true, 0.3, 0.3);
                    _context.program.Echo("RandomPatrol State: Selecting random target within 50m");
                    
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var gravity = _context.program._remoteControl.GetNaturalGravity();
                    var upVector = gravity.LengthSquared() > 0 ? -Vector3D.Normalize(gravity) : Vector3D.Up;
                    
                    var random = new Random();
                    var angle = random.NextDouble() * 2 * Math.PI;
                    var distance = random.NextDouble() * 50.0;
                    
                    var currentForward = _context.program._remoteControl.WorldMatrix.Forward;
                    var currentRight = _context.program._remoteControl.WorldMatrix.Right;
                    
                    var forwardHorizontal = currentForward - Vector3D.Dot(currentForward, upVector) * upVector;
                    var rightHorizontal = currentRight - Vector3D.Dot(currentRight, upVector) * upVector;
                    
                    if (forwardHorizontal.LengthSquared() > 1e-6)
                        forwardHorizontal = Vector3D.Normalize(forwardHorizontal);
                    else
                        forwardHorizontal = new Vector3D(1, 0, 0);
                        
                    if (rightHorizontal.LengthSquared() > 1e-6)
                        rightHorizontal = Vector3D.Normalize(rightHorizontal);
                    else
                        rightHorizontal = new Vector3D(0, 1, 0);
                    
                    _targetPosition = currentPos + (forwardHorizontal * Math.Cos(angle) + rightHorizontal * Math.Sin(angle)) * distance;
                    
                    _context.program.Echo($"RandomPatrol: Target selected at {distance:F1}m - {_targetPosition}");
                    _context.TransitionTo(new AlignYawState(_targetPosition, _context));
                }

                public override void Execute()
                {
                }
            }

            private class AlignYawState : State<ClaptrapContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;

                public AlignYawState(Vector3D targetPosition, ClaptrapContext context) : base(context)
                {
                    _targetPosition = targetPosition;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Cyan, true, 0.2, 0.2);
                    _context.program.Echo("AlignYaw State: Calculating target yaw and aligning");
                    _targetYaw = _context.program._alignment.CalculateYawToTarget(_targetPosition);
                    _context.program.Echo($"AlignYaw: Aligning to yaw {_targetYaw * 180.0 / Math.PI:F1}°");
                }

                public override void Execute()
                {
                    if (_context.program._alignment.AlignWithYawPitchRoll(_targetYaw, 0, 0))
                    {
                        _context.program.Echo("AlignYaw: Yaw aligned, moving to target");
                        _context.TransitionTo(new MoveState(_targetPosition, _targetYaw, _context));
                    }
                }
            }

            private class MoveState : State<ClaptrapContext>
            {
                private Vector3D _targetPosition;
                private double _targetYaw;
                private const double ACCELERATION = 0.5;
                private const double OPTIMAL_SPEED = 5.0;
                private const double ARRIVAL_DISTANCE = 5.0;
                private const double PROP_OVERRIDE_FACTOR = 0.15;

                public MoveState(Vector3D targetPosition, double targetYaw, ClaptrapContext context) : base(context)
                {
                    _targetPosition = targetPosition;
                    _targetYaw = targetYaw;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Magenta, true, 0.1, 0.1);
                    var distance = Vector3D.Distance(_context.program._remoteControl.GetPosition(), _targetPosition);
                    _context.program.Echo("Move State: Starting movement to target");
                    _context.program.Echo($"Move: Target is {distance:F1}m away");
                }

                public override void Execute()
                {
                    var currentPos = _context.program._remoteControl.GetPosition();
                    var distance = Vector3D.Distance(currentPos, _targetPosition);
                    var velocity = _context.program._remoteControl.GetShipVelocities().LinearVelocity;
                    var speed = velocity.Length();
                    
                    _context.program._alignment.AlignWithYawPitchRoll(_targetYaw, 0, 0);
                    
                    if (distance <= ARRIVAL_DISTANCE)
                    {
                        _context.program.Echo("Move: Arrived at target");
                        _context.program._roverNavigation.Stop();
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }
                    
                    var direction = Vector3D.Normalize(_targetPosition - currentPos);
                    var forward = _context.program._remoteControl.WorldMatrix.Forward;
                    var forwardDot = Vector3D.Dot(direction, forward);
                    
                    var brakingDistance = (speed * speed) / (2 * ACCELERATION);
                    var shouldBrake = distance <= brakingDistance + 2.0;
                    
                    double propulsionOverride = 0;
                    
                    if (forwardDot > 0.5)
                    {
                        if (shouldBrake)
                        {
                            var brakeSpeed = Math.Min(OPTIMAL_SPEED, Math.Sqrt(2 * ACCELERATION * distance));
                            var speedError = brakeSpeed - speed;
                            propulsionOverride = MathHelper.Clamp(speedError / OPTIMAL_SPEED * PROP_OVERRIDE_FACTOR, -1.0, 1.0);
                        }
                        else
                        {
                            var speedError = OPTIMAL_SPEED - speed;
                            propulsionOverride = MathHelper.Clamp(speedError / OPTIMAL_SPEED * PROP_OVERRIDE_FACTOR, 0.1, 1.0);
                        }
                        
                        _context.program._roverNavigation.NavigateForward(-propulsionOverride);
                    }
                    else if (forwardDot < -0.5)
                    {
                        if (shouldBrake)
                        {
                            var brakeSpeed = Math.Min(OPTIMAL_SPEED, Math.Sqrt(2 * ACCELERATION * distance));
                            var speedError = brakeSpeed - speed;
                            propulsionOverride = MathHelper.Clamp(speedError / OPTIMAL_SPEED * PROP_OVERRIDE_FACTOR, -1.0, 1.0);
                        }
                        else
                        {
                            var speedError = OPTIMAL_SPEED - speed;
                            propulsionOverride = MathHelper.Clamp(speedError / OPTIMAL_SPEED * PROP_OVERRIDE_FACTOR, 0.1, 1.0);
                        }
                        
                        _context.program._roverNavigation.NavigateBackward(-propulsionOverride);
                    }
                    else
                    {
                        _context.program._roverNavigation.Stop();
                    }
                    
                    _context.program.Echo($"Move: Distance={distance:F1}m, Speed={speed:F1}m/s, Brake={shouldBrake}, Prop={propulsionOverride:F2}");
                }
            }
        }
    }
}

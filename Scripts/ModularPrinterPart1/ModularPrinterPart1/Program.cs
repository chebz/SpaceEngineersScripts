using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private ModularPrinterPart1Context _context;
        private CustomDataConnector _customDataConnector;
        private bool _initialized = false;
        private IMyBroadcastListener _broadcastListener;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _broadcastListener = IGC.RegisterBroadcastListener("ModularPrinter");
            
            _context = new ModularPrinterPart1Context();
            string errorMessage;
            if (!_context.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Initialization failed: {errorMessage}");
                _context = null;
                return;
            }
            
            _customDataConnector.Load();
            _initialized = true;
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!_initialized)
                return;

            HandleBroadcastMessages();
            
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }
            
            _context.Execute();
            _customDataConnector.Update();
        }

        private void HandleBroadcastMessages()
        {
            while (_broadcastListener.HasPendingMessage)
            {
                var message = _broadcastListener.AcceptMessage();
                if (message.Data is string)
                {
                    var data = message.Data.ToString();
                    if (data.StartsWith("mpp1|"))
                    {
                        var command = data.Substring(5); // Remove "mpp1|" prefix
                        HandleCommand(command);
                    }
                }
            }
        }

        private void HandleCommand(string argument)
        {
            var parts = argument.Split(' ');
            if (parts.Length == 0) return;

            switch (parts[0].ToLower())
            {
                case "move":
                    if (parts.Length >= 2)
                    {
                        double x;
                        if (double.TryParse(parts[1], out x))
                        {
                            _context.MoveTo(x);
                        }
                        else
                        {
                            Echo("Usage: move <x>");
                        }
                    }
                    else
                    {
                        Echo("Usage: move <x>");
                    }
                    break;
                case "stop":
                    _context.Stop();
                    break;
                default:
                    Echo($"Unknown command: {parts[0]}");
                    break;
            }
        }

    }

    public class ModularPrinterPart1Context : Context
    {
        #region Constants
        public const double SUSPENSION_SPEED_DEFAULT = 0.1;
        public const double POSITION_TOLERANCE_DEFAULT = 0.1;
        public const double MAX_X_DISTANCE_DEFAULT = 15.0;
        public const double FWD_OFFSET_DEFAULT = 5.0;
        #endregion

        #region Fields
        public Program _program;
        private List<IMyMotorSuspension> _suspensionsL;
        private List<IMyMotorSuspension> _suspensionsR;
        private List<IMyInteriorLight> _interiorLights;
        private List<IMyReflectorLight> _reflectorLights;
        private IMyCameraBlock _fwdCamera;
        private IMyRemoteControl _remoteControl;
        private IMyTextSurface _displayPanel;
        public CustomDataConnector _customDataConnector;
        public MPP1Section _section;
        public double _xPosition;
        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, out string errorMessage)
        {
            _program = program;
            errorMessage = string.Empty;

            _customDataConnector = customDataConnector;

            _remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "No remote control found!";
                return false;
            }

            _interiorLights = program.GetLocalBlocks<IMyInteriorLight>();
            _reflectorLights = program.GetLocalBlocks<IMyReflectorLight>();
            _fwdCamera = program.GetLocalBlock<IMyCameraBlock>();
            _fwdCamera.EnableRaycast = true;
            _displayPanel = program.Me.GetSurface(0);

            _section = new MPP1Section(this);
            _customDataConnector.AddSection(_section);

            Utils.ClearLog(_remoteControl);
            InitializeSuspensions();

            TransitionTo(new IdleState(this));

            return true;
        }

        private void InitializeSuspensions()
        {
            var allSuspensions = _program.GetLocalBlocks<IMyMotorSuspension>();
            _suspensionsL = new List<IMyMotorSuspension>();
            _suspensionsR = new List<IMyMotorSuspension>();

            var centerOfMass = _remoteControl.CenterOfMass;
            var rightVector = _remoteControl.WorldMatrix.Right;

            foreach (var suspension in allSuspensions)
            {
                var suspensionPos = suspension.GetPosition();
                var toSuspension = suspensionPos - centerOfMass;
                var dotProduct = Vector3D.Dot(toSuspension, rightVector);
                
                if (dotProduct >= 0)
                {
                    _suspensionsR.Add(suspension);
                }
                else
                {
                    _suspensionsL.Add(suspension);
                }
            }

            // Rename suspensions
            for (int i = 0; i < _suspensionsL.Count; i++)
            {
                _suspensionsL[i].CustomName = $"Suspension L {i + 1}";
            }

            for (int i = 0; i < _suspensionsR.Count; i++)
            {
                _suspensionsR[i].CustomName = $"Suspension R {i + 1}";
            }
        }

        public void MoveTo(double x)
        {
            if (CurrentState is IdleState)
            {
                TransitionTo(new MoveState(this, x));
            }
        }

        public void Stop()
        {
            if (CurrentState is MoveState)
            {
                TransitionTo(new IdleState(this));
            }
        }


        public void SetSuspensionVelocity(double speed)
        {
            foreach (var suspension in _suspensionsL)
            {
                suspension.PropulsionOverride = (float)speed;
            }
            foreach (var suspension in _suspensionsR)
            {
                suspension.PropulsionOverride = (float)-speed;
            }
        }

        public void SetBrake(bool enabled)
        {
            _remoteControl.HandBrake = enabled;
        }


        private void UpdatePositions()
        {
            if (_fwdCamera != null)
            {
                var hitInfo = _fwdCamera.Raycast(100, Vector3D.Forward);
                if (!hitInfo.IsEmpty())
                {
                    _xPosition = Vector3D.Distance(hitInfo.HitPosition.Value, _fwdCamera.GetPosition());
                    _xPosition -= _section.FwdOffset.Value;
                    return;
                }
            }
            _xPosition = 0;
        }

        private void UpdateDisplay()
        {
            if (_displayPanel != null)
            {
                var stateName = CurrentState?.GetType().Name ?? "None";
                var displayText = $"{stateName}\nX: {_xPosition:F2}";
                _displayPanel.WriteText(displayText);
            }
        }

        private void UpdateLights()
        {
            bool isIdle = CurrentState is IdleState;
            
            // Update reflector lights
            foreach (var light in _reflectorLights)
            {
                light.Enabled = !isIdle;
            }
            
            // Update interior lights
            foreach (var light in _interiorLights)
            {
                if (isIdle)
                {
                    light.Color = Color.Green;
                }
                else
                {
                    light.Color = Color.Yellow;
                }
            }
        }

        public override void Execute()
        {
            UpdatePositions();
            UpdateDisplay();
            UpdateLights();
            base.Execute();
        }
        #endregion
    }

    #region States

    public class IdleState : State<ModularPrinterPart1Context>
    {
        public IdleState(ModularPrinterPart1Context context) : base(context) { }

        public override void Enter()
        {
            // Stop all suspensions
            _context.SetSuspensionVelocity(0);
            // Set brake to hold position
            _context.SetBrake(true);
            // Notify controller that we're ready
            _context._program.IGC.SendBroadcastMessage("ModularPrinter", "mpp1_ready");
        }

        public override void Execute()
        {
            // Idle state - do nothing
        }
    }

    public class MoveState : State<ModularPrinterPart1Context>
    {
        private double _targetX;
        private double _tolerance;

        public MoveState(ModularPrinterPart1Context context, double targetX) : base(context)
        {
            _targetX = targetX;
            _tolerance = _context._section.PositionTolerance.Value;
        }

        public override void Enter()
        {
            _context._program.Echo($"Moving to X: {_targetX:F2}");
            // Release brake to allow movement
            _context.SetBrake(false);
        }

        public override void Execute()
        {
            var currentX = _context._xPosition;
            var difference = _targetX - currentX;

            if (Math.Abs(difference) <= _tolerance)
            {
                // Set brake before transitioning to idle
                _context.SetBrake(true);
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            var speed = _context._section.SuspensionSpeed.Value;
            var velocity = Math.Sign(difference) * speed;

            // Apply velocity to all suspensions
            _context.SetSuspensionVelocity(velocity);
        }
    }

    #endregion

    #region Custom Data Section

    public class MPP1Section : Section
    {
        public DoubleProperty SuspensionSpeed { get; private set; }
        public DoubleProperty PositionTolerance { get; private set; }
        public DoubleProperty MaxXDistance { get; private set; }
        public DoubleProperty FwdOffset { get; private set; }

        public MPP1Section(ModularPrinterPart1Context context) : base("MPP1")
        {
            SuspensionSpeed = new DoubleProperty("SuspensionSpeed", ModularPrinterPart1Context.SUSPENSION_SPEED_DEFAULT);
            PositionTolerance = new DoubleProperty("PositionTolerance", ModularPrinterPart1Context.POSITION_TOLERANCE_DEFAULT);
            MaxXDistance = new DoubleProperty("MaxXDistance", ModularPrinterPart1Context.MAX_X_DISTANCE_DEFAULT);
            FwdOffset = new DoubleProperty("FwdOffset", ModularPrinterPart1Context.FWD_OFFSET_DEFAULT);

            _properties.Add(SuspensionSpeed);
            _properties.Add(PositionTolerance);
            _properties.Add(MaxXDistance);
            _properties.Add(FwdOffset);
        }
    }

    #endregion
}
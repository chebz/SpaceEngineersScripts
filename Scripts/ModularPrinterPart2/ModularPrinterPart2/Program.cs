using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private ModularPrinterPart2Context _context;
        private CustomDataConnector _customDataConnector;
        private bool _initialized = false;
        private IMyBroadcastListener _broadcastListener;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            _customDataConnector.Load();
            
            _broadcastListener = IGC.RegisterBroadcastListener("ModularPrinter");
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (_context != null)
            {
                HandleBroadcastMessages();
                
                if (!string.IsNullOrEmpty(argument))
                {
                    HandleCommand(argument);
                }
                
                _context.Execute();
                _customDataConnector.Update();
            }
        }

        private void HandleBroadcastMessages()
        {
            while (_broadcastListener.HasPendingMessage)
            {
                var message = _broadcastListener.AcceptMessage();
                if (message.Data is string)
                {
                    var data = message.Data.ToString();
                    if (data.StartsWith("mpp2|"))
                    {
                        var command = data.Substring(5); // Remove "mpp2|" prefix
                        HandleCommand(command);
                    }
                }
            }
        }

        private void HandleCommand(string argument)
        {
            switch (argument.ToLower())
            {
                case "status":
                    Echo($"Current State: {_context.CurrentState?.GetType().Name ?? "None"}");
                    break;
                case "abort":
                case "stop":
                    _context.AbortMovement();
                    break;
                case "pattern":
                    _context.StartPattern();
                    break;
                default:
                    if (argument.StartsWith("move "))
                    {
                        var parts = argument.Split(' ');
                        if (parts.Length == 3)
                        {
                            double x, y;
                            if (double.TryParse(parts[1], out x) && double.TryParse(parts[2], out y))
                            {
                                _context.MoveTo(x, y);
                            }
                            else
                            {
                                Echo("Invalid move command format. Use: move <x> <y>");
                            }
                        }
                        else
                        {
                            Echo("Invalid move command format. Use: move <x> <y>");
                        }
                    }
                    else if (argument.StartsWith("print "))
                    {
                        var printParts = argument.Split(' ');
                        if (printParts.Length >= 3)
                        {
                            double width, height;
                            if (double.TryParse(printParts[1], out width) && double.TryParse(printParts[2], out height))
                            {
                                _context.StartPrint(width, height);
                            }
                            else
                            {
                                Echo("Invalid print dimensions");
                            }
                        }
                        else
                        {
                            Echo("Usage: print <width> <height>");
                        }
                    }
                    else
                    {
                        Echo($"Unknown command: {argument}");
                        Echo("Available commands: status, move <x> <y>, pattern, print <width> <height>, abort/stop");
                    }
                    break;
            }
        }

        private void Initialize()
        {
            _context = new ModularPrinterPart2Context();
            string errorMessage;
            if (_context.Initialize(this, _customDataConnector, out errorMessage))
            {
                _customDataConnector.Load();
                _initialized = true;
                Echo("ModularPrinterPart2 initialized successfully");
            }
            else
            {
                Echo($"ModularPrinterPart2 initialization failed: {errorMessage}");
            }
        }
    }

    public class ModularPrinterPart2Context : Context
    {
        #region Constants
        public const double SUSPENSION_SPEED_DEFAULT = 0.1;
        public const double PISTON_SPEED_DEFAULT = 0.2;
        public const double FWD_OFFSET_DEFAULT = 3.0;
        public const double POSITION_TOLERANCE_DEFAULT = 0.1;
        public const double MAX_FWD_DISTANCE_DEFAULT = 15.0;
        public const double MAX_PISTON_EXTENSION_DEFAULT = 15.0;
        
        
        // Pattern defaults
        public const double PATTERN_STEP_DEFAULT = 1.0;
        public const double PATTERN_WAIT_DEFAULT = 0.5;
        public const double PATTERN_Y_MAX_DEFAULT = 10.0;
        #endregion

        #region Fields
        public Program _program;
        private List<IMyPistonBase> _pistons;
        private List<IMyMotorSuspension> _suspensionsFwdL;
        private List<IMyMotorSuspension> _suspensionsFwdR;
        private List<IMyMotorSuspension> _suspensionsRevL;
        private List<IMyMotorSuspension> _suspensionsRevR;
        private List<IMyShipWelder> _welders;
        private List<IMyInteriorLight> _interiorLights;
        private List<IMyReflectorLight> _reflectorLights;
        private IMyCameraBlock _fwdCamera;
        private IMyRemoteControl _remoteControl;
        private IMyTextSurface _displayPanel;
        public CustomDataConnector _customDataConnector;
        public MPP2Section _section;
        public double _xPosition;
        public double _yPosition;
        public double _printWidth = 0.0;
        public double _printHeight = 0.0;
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

            _welders = program.GetLocalBlocks<IMyShipWelder>();
            _interiorLights = program.GetLocalBlocks<IMyInteriorLight>();
            _reflectorLights = program.GetLocalBlocks<IMyReflectorLight>();
            _displayPanel = program.Me.GetSurface(0);

            _section = new MPP2Section(this);
            _customDataConnector.AddSection(_section);

            Utils.ClearLog(_remoteControl);
            InitializePistons();
            InitializeSuspensions();
            InitializeCameras();

            TransitionTo(new IdleState(this));

            return true;
        }

        private void InitializePistons()
        {
            var allPistons = _program.GetLocalBlocks<IMyPistonBase>();
            _pistons = new List<IMyPistonBase>();

            foreach (var piston in allPistons)
            {
                _pistons.Add(piston);
            }

            for (int i = 0; i < _pistons.Count; i++)
            {
                _pistons[i].CustomName = $"Piston {i + 1}";
            }
        }

        private void InitializeSuspensions()
        {
            var allSuspensions = _program.GetLocalBlocks<IMyMotorSuspension>();
            _suspensionsFwdL = new List<IMyMotorSuspension>();
            _suspensionsFwdR = new List<IMyMotorSuspension>();
            _suspensionsRevL = new List<IMyMotorSuspension>();
            _suspensionsRevR = new List<IMyMotorSuspension>();

            foreach (var suspension in allSuspensions)
            {
                var directionToSuspension = suspension.GetPosition() - _remoteControl.GetPosition();
                var dotProductForward = Vector3D.Dot(_remoteControl.WorldMatrix.Forward, directionToSuspension);
                var dotProductRight = Vector3D.Dot(_remoteControl.WorldMatrix.Right, directionToSuspension);
                
                if (dotProductForward > 0)
                {
                    if (dotProductRight > 0)
                    {
                        _suspensionsFwdR.Add(suspension);
                    }
                    else
                    {
                        _suspensionsFwdL.Add(suspension);
                    }
                }
                else
                {
                    if (dotProductRight > 0)
                    {
                        _suspensionsRevR.Add(suspension);
                    }
                    else
                    {
                        _suspensionsRevL.Add(suspension);
                    }
                }
            }

            for (int i = 0; i < _suspensionsFwdL.Count; i++)
            {
                _suspensionsFwdL[i].CustomName = $"Forward Suspension L {i + 1}";
            }

            for (int i = 0; i < _suspensionsFwdR.Count; i++)
            {
                _suspensionsFwdR[i].CustomName = $"Forward Suspension R {i + 1}";
            }

            for (int i = 0; i < _suspensionsRevL.Count; i++)
            {
                _suspensionsRevL[i].CustomName = $"Reverse Suspension L {i + 1}";
            }

            for (int i = 0; i < _suspensionsRevR.Count; i++)
            {
                _suspensionsRevR[i].CustomName = $"Reverse Suspension R {i + 1}";
            }
        }

        private void InitializeCameras()
        {
            var allCameras = _program.GetLocalBlocks<IMyCameraBlock>();
            foreach (var camera in allCameras)
            {
                var dotProductForward = Vector3D.Dot(_remoteControl.WorldMatrix.Forward, camera.WorldMatrix.Forward);
                var dotProductUp = Vector3D.Dot(_remoteControl.WorldMatrix.Up, camera.WorldMatrix.Forward);

                if (Math.Abs(dotProductForward) > 0.7)
                {
                    _fwdCamera = camera;
                    _fwdCamera.EnableRaycast = true;
                    camera.CustomName = "Forward Camera";
                }
            }
        }


        public void SetWeldersEnabled(bool enabled)
        {
            foreach (var welder in _welders)
            {
                welder.Enabled = enabled;
            }
        }

        public bool IsAnyWelderActivated()
        {
            foreach (var welder in _welders)
            {
                var isWeldingProperty = welder.GetProperty("IsWelding");
                if (isWeldingProperty != null && isWeldingProperty.AsBool().GetValue(welder))
                {
                    _program.Echo($"Welder {welder.CustomName} is welding");
                    return true;
                }
            }
            return false;
        }

        public void MoveTo(double targetX, double targetY)
        {
            targetX = MathHelper.Clamp(targetX, 0, _section.MaxFwdDistance.Value);
            TransitionTo(new MovingState(this, new Vector2D(targetX, targetY), null));
        }

        public void StartPattern()
        {
            // Start by moving to home position (0,0)
            TransitionTo(new MovingState(this, new Vector2D(0, 0), 0));
        }

        public void StartPrint(double width, double height)
        {
            // Store print dimensions in context
            _printWidth = width;
            _printHeight = height;
            // Start by moving to home position (0,0) for printing
            TransitionTo(new PrintState(this));
        }

        public void AbortMovement()
        {
            TransitionTo(new IdleState(this));
        }

        public bool IsAtTargetPosition(double targetX, double targetY)
        {
            return Math.Abs(_xPosition - targetX) <= _section.PositionTolerance.Value && 
                   Math.Abs(_yPosition - targetY) <= _section.PositionTolerance.Value;
        }

        public bool IsAtTargetPosition(Vector2D targetPosition)
        {
            return Math.Abs(_xPosition - targetPosition.X) <= _section.PositionTolerance.Value && 
                   Math.Abs(_yPosition - targetPosition.Y) <= _section.PositionTolerance.Value;
        }

        public void SetSuspensionVelocity(double speed)
        {
            foreach (var suspension in _suspensionsFwdL)
            {
                suspension.PropulsionOverride = (float)speed;
            }
            foreach (var suspension in _suspensionsFwdR)
            {
                suspension.PropulsionOverride = (float)-speed;
            }
            foreach (var suspension in _suspensionsRevL)
            {
                suspension.PropulsionOverride = (float)speed;
            }
            foreach (var suspension in _suspensionsRevR)
            {
                suspension.PropulsionOverride = (float)-speed;
            }
        }

        public void SetBrake(bool enabled)
        {
            _remoteControl.HandBrake = enabled;
        }

        public void SetPistonVelocity(double speed)
        {
            foreach (var piston in _pistons)
            {
                piston.Velocity = (float)speed;
            }
        }


        public void UpdatePositions()
        {
            UpdateXPosition();
            UpdateYPosition();
        }

        private void UpdateXPosition()
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
                _program.Echo("No hit info found");
            }
            _program.Echo("No forward camera found");
            _xPosition = 0;
        }

        private void UpdateYPosition()
        {
            double totalExtension = 0;
            foreach (var piston in _pistons)
            {
                totalExtension += piston.CurrentPosition;
            }
            
            // Y position is max extension minus current extension (0 = fully extended, MaxExtension = fully retracted)
            _yPosition = _section.MaxPistonExtension.Value - totalExtension;
        }

        public void UpdateDisplay()
        {
            if (_displayPanel != null)
            {
                var stateName = CurrentState?.GetType().Name ?? "None";
                var displayText = $"{stateName}\nX: {_xPosition:F2}\nY: {_yPosition:F2}";
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

    public class IdleState : State<ModularPrinterPart2Context>
    {
        public IdleState(ModularPrinterPart2Context context) : base(context) { }

        public override void Enter()
        {
            _context.SetWeldersEnabled(false);
            _context.SetSuspensionVelocity(0);
            _context.SetPistonVelocity(0);
            _context.SetBrake(true);
            _context._program.Echo("Idle State - Ready");
        }

        public override void Execute()
        {
        }
    }

    public class MovingState : State<ModularPrinterPart2Context>
    {
        private Vector2D _targetPosition;
        private int? _nextPatternIndex;
        private int _transitionType; // 0 = idle, 1 = pattern, 2 = printing
        private Vector2D? _nextPosition; // For printing mode - the position to move to after welding

        public MovingState(ModularPrinterPart2Context context, Vector2D targetPosition, int? nextPatternIndex, int transitionType = 0, Vector2D? nextPosition = null) : base(context) 
        {
            _targetPosition = targetPosition;
            _nextPatternIndex = nextPatternIndex;
            _transitionType = transitionType;
            _nextPosition = nextPosition;
        }

        public override void Enter()
        {
            _context.SetBrake(false);
            _context._program.Echo($"Moving State - Target: X:{_targetPosition.X:F2} Y:{_targetPosition.Y:F2}");
        }

        public override void Execute()
        {
            // Check for welding if transition type is 2 (printing) - regardless of position
            if (_transitionType == 2 && _context.IsAnyWelderActivated())
            {
                _context.SetBrake(true);
                _context.SetPistonVelocity(0);
                if (_nextPosition.HasValue)
                {
                    _context.TransitionTo(new WeldingState(_context, _nextPosition.Value, _nextPatternIndex));
                }
                return;
            }
            
            if (_context.IsAtTargetPosition(_targetPosition))
            {
                _context.SetBrake(true);
                _context.SetPistonVelocity(0);
                
                // Handle transitions based on type
                switch (_transitionType)
                {
                    case 0: // Idle
                        _context.TransitionTo(new IdleState(_context));
                        break;
                    case 1: // Pattern
                        if (_nextPatternIndex.HasValue)
                        {
                            _context.TransitionTo(new PatternState(_context, _nextPatternIndex.Value));
                        }
                        else
                        {
                            _context.TransitionTo(new IdleState(_context));
                        }
                        break;
                    case 2: // Printing
                        if (_nextPatternIndex.HasValue)
                        {
                            _context.TransitionTo(new PrintState(_context, _nextPatternIndex.Value));
                        }
                        else
                        {
                            _context.TransitionTo(new IdleState(_context));
                        }
                        break;
                }
                return;
            }

            double xError = _targetPosition.X - _context._xPosition;
            double yError = _context._yPosition - _targetPosition.Y;

            if (Math.Abs(xError) > _context._section.PositionTolerance.Value)
            {
                double xSpeed = Math.Sign(xError) * _context._section.SuspensionSpeed.Value;
                _context.SetSuspensionVelocity(xSpeed);
            }
            else
            {
                _context.SetBrake(true);
                _context.SetSuspensionVelocity(0);
            }

            if (Math.Abs(yError) > _context._section.PositionTolerance.Value)
            {
                double ySpeed = Math.Sign(yError) * _context._section.PistonSpeed.Value;
                _context.SetPistonVelocity(ySpeed);
            }
            else
            {
                _context.SetPistonVelocity(0);
            }
        }
    }

    public class PatternState : State<ModularPrinterPart2Context>
    {
        public int _currentIndex;
        private DateTime _waitStartTime;
        private List<Vector2D> _patternPositions;

        public PatternState(ModularPrinterPart2Context context, int startIndex) : base(context) 
        {
            _currentIndex = startIndex;
        }

        public override void Enter()
        {
            _context.SetWeldersEnabled(false);
            _context.SetBrake(true);
            InitializePatternPositions();
            _waitStartTime = DateTime.Now;
            _context._program.Echo($"Pattern State - Waiting at position {_currentIndex}");
        }

        public override void Execute()
        {
            if (DateTime.Now - _waitStartTime > TimeSpan.FromSeconds(_context._section.PatternWait.Value))
            {
                _currentIndex++;
                MoveToNextPosition();
            }
        }

        private void InitializePatternPositions()
        {
            _patternPositions = new List<Vector2D>();
            var maxFwd = _context._section.MaxFwdDistance.Value;
            var step = _context._section.PatternStep.Value;
            var maxExtension = _context._section.MaxPistonExtension.Value;

            bool movingForward = true;
            
            // Nested loop: for each Y level, go through all X positions
            // Y starts at 0 (fully extended) and increases by step each row (more retracted)
            for (double y = 0; y <= 10; y += step)
            {
                if (movingForward)
                {
                    // Forward: 0 to MaxFwdDistance
                    for (double x = 0; x <= 10; x += step)
                    {
                        _patternPositions.Add(new Vector2D(x, y));
                    }
                }
                else
                {
                    // Backward: MaxFwdDistance to 0
                    for (double x = 10; x >= 0; x -= step)
                    {
                        _patternPositions.Add(new Vector2D(x, y));
                    }
                }
                
                // Alternate direction for next Y level
                movingForward = !movingForward;
            }
        }


        private void MoveToNextPosition()
        {
            if (_currentIndex >= _patternPositions.Count)
            {
                // Pattern complete - notify controller
                _context._program.IGC.SendBroadcastMessage("ModularPrinter", "mpp2_ready");
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            var nextPosition = _patternPositions[_currentIndex];
            _context.TransitionTo(new MovingState(_context, nextPosition, _currentIndex));
        }
    }

    #endregion

    public class MPP2Section : Section
    {
        private ModularPrinterPart2Context _context;
        
        public DoubleProperty SuspensionSpeed { get; } = new DoubleProperty("SuspensionSpeed", ModularPrinterPart2Context.SUSPENSION_SPEED_DEFAULT);
        public DoubleProperty PistonSpeed { get; } = new DoubleProperty("PistonSpeed", ModularPrinterPart2Context.PISTON_SPEED_DEFAULT);
        public DoubleProperty FwdOffset { get; } = new DoubleProperty("FwdOffset", ModularPrinterPart2Context.FWD_OFFSET_DEFAULT);
        public DoubleProperty PositionTolerance { get; } = new DoubleProperty("PositionTolerance", ModularPrinterPart2Context.POSITION_TOLERANCE_DEFAULT);
        public DoubleProperty MaxFwdDistance { get; } = new DoubleProperty("MaxFwdDistance", ModularPrinterPart2Context.MAX_FWD_DISTANCE_DEFAULT);
        public DoubleProperty MaxPistonExtension { get; } = new DoubleProperty("MaxPistonExtension", ModularPrinterPart2Context.MAX_PISTON_EXTENSION_DEFAULT);
        
        // Pattern Properties
        public DoubleProperty PatternStep { get; } = new DoubleProperty("PatternStep", ModularPrinterPart2Context.PATTERN_STEP_DEFAULT);
        public DoubleProperty PatternWait { get; } = new DoubleProperty("PatternWait", ModularPrinterPart2Context.PATTERN_WAIT_DEFAULT);
        public DoubleProperty PatternYMax { get; } = new DoubleProperty("PatternYMax", ModularPrinterPart2Context.PATTERN_Y_MAX_DEFAULT);

        public MPP2Section(ModularPrinterPart2Context context) : base("MPP2Section")
        {
            _context = context;
            _properties.Add(SuspensionSpeed);
            _properties.Add(PistonSpeed);
            _properties.Add(FwdOffset);
            _properties.Add(PositionTolerance);
            _properties.Add(MaxFwdDistance);
            _properties.Add(MaxPistonExtension);
            _properties.Add(PatternStep);
            _properties.Add(PatternWait);
            _properties.Add(PatternYMax);
        }
    }

    public class PrintState : State<ModularPrinterPart2Context>
    {
        private int _currentIndex;
        public List<Vector2D> _patternPositions;

        public PrintState(ModularPrinterPart2Context context, int startIndex = 0) : base(context) 
        {
            _currentIndex = startIndex;
        }

        public override void Enter()
        {
            _context.SetWeldersEnabled(true);
            _context.SetBrake(true);
            InitializePatternPositions();
            _context._program.Echo($"Print State - Starting at position {_currentIndex}");
        }

        public override void Execute()
        {
            if (_currentIndex >= _patternPositions.Count)
            {
                // Print complete - notify controller
                _context._program.IGC.SendBroadcastMessage("ModularPrinter", "mpp2_ready");
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            var nextPosition = _patternPositions[_currentIndex];
            var nextNextPosition = _currentIndex + 1 < _patternPositions.Count ? _patternPositions[_currentIndex + 1] : (Vector2D?)null;
            _context.TransitionTo(new MovingState(_context, nextPosition, _currentIndex + 1, 2, nextNextPosition));
        }

        private void InitializePatternPositions()
        {
            _patternPositions = new List<Vector2D>();
            
            double step = _context._section.PatternStep.Value;
            
            for (double y = 0; y <= _context._printHeight; y += step)
            {
                if (y % (step * 2) == 0)
                {
                    // Even rows: left to right
                    _patternPositions.Add(new Vector2D(0, y));
                    _patternPositions.Add(new Vector2D(_context._printWidth, y));
                }
                else
                {
                    // Odd rows: right to left
                    _patternPositions.Add(new Vector2D(_context._printWidth, y));
                    _patternPositions.Add(new Vector2D(0, y));
                }
            }
        }
    }

    public class WeldingState : State<ModularPrinterPart2Context>
    {
        private Vector2D _nextPosition;
        private int? _nextPatternIndex;

        public WeldingState(ModularPrinterPart2Context context, Vector2D nextPosition, int? nextPatternIndex) : base(context) 
        {
            _nextPosition = nextPosition;
            _nextPatternIndex = nextPatternIndex;
        }

        public override void Enter()
        {
            _context._program.Echo("Welding State - Waiting for welders to finish");
        }

        public override void Execute()
        {
            // Wait until all welders are no longer activated
            if (!_context.IsAnyWelderActivated())
            {
                // Welding complete, continue to next position
                _context.TransitionTo(new MovingState(_context, _nextPosition, _nextPatternIndex, 2));
            }
        }
    }
}

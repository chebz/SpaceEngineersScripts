using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private ModularPrinterContext _context;
        private CustomDataConnector _customDataConnector;
        private bool _initialized = false;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _context = new ModularPrinterContext();
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

            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }
            
            _context.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            var parts = argument.Split(' ');
            if (parts.Length == 0) return;

            switch (parts[0].ToLower())
            {
                case "print":
                    _context.StartPrint();
                    break;
                case "abort":
                    _context.AbortPrint();
                    break;
                default:
                    Echo($"Unknown command: {parts[0]}");
                    break;
            }
        }
    }

    public class ModularPrinterContext : Context
    {
        #region Constants
        public const string BROADCAST_TAG = "ModularPrinter";
        public const double LENGTH_DEFAULT = 15.0;
        public const double WIDTH_DEFAULT = 10.0;
        public const double HEIGHT_DEFAULT = 5.0;
        public const double X_STEP = 1.0;
        #endregion

        #region Fields
        public Program _program;
        private IMyBroadcastListener _broadcastListener;
        public CustomDataConnector _customDataConnector;
        public MPSection _section;
        public double _currentXPosition = 0.0;
        public bool _printingInProgress = false;
        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, out string errorMessage)
        {
            _program = program;
            _customDataConnector = customDataConnector;
            errorMessage = string.Empty;

            _broadcastListener = program.IGC.RegisterBroadcastListener(BROADCAST_TAG);

            _section = new MPSection(this);
            _customDataConnector.AddSection(_section);

            TransitionTo(new IdleState(this));

            return true;
        }

        public void StartPrint()
        {
            if (CurrentState is IdleState)
            {
                _currentXPosition = 0.0;
                _printingInProgress = true;
                TransitionTo(new PrintingState(this));
            }
        }

        public void AbortPrint()
        {
            _printingInProgress = false;
            
            // Send abort commands to both platforms
            SendToMPP1("stop");
            SendToMPP2("stop");
            
            TransitionTo(new IdleState(this));
        }

        public void HandleMessages()
        {
            while (_broadcastListener.HasPendingMessage)
            {
                var message = _broadcastListener.AcceptMessage();
                if (message.Data is string)
                {
                    var data = message.Data.ToString();
                    HandleMessage(data, message.Source);
                }
            }
        }

        private void HandleMessage(string message, long source)
        {
            if (message == "mpp1_ready" && CurrentState is PrintingState)
            {
                var printingState = CurrentState as PrintingState;
                printingState.OnMPP1Ready();
            }
            else if (message == "mpp2_ready" && CurrentState is PrintingState)
            {
                var printingState = CurrentState as PrintingState;
                printingState.OnMPP2Ready();
            }
        }

        public void SendToMPP1(string command)
        {
            _program.IGC.SendBroadcastMessage(BROADCAST_TAG, $"mpp1|{command}");
        }

        public void SendToMPP2(string command)
        {
            _program.IGC.SendBroadcastMessage(BROADCAST_TAG, $"mpp2|{command}");
        }

        public override void Execute()
        {
            HandleMessages();
            base.Execute();
        }
        #endregion
    }

    #region States

    public class IdleState : State<ModularPrinterContext>
    {
        public IdleState(ModularPrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context._program.Echo("ModularPrinter: Idle");
        }

        public override void Execute()
        {
            // Idle state - do nothing
        }
    }

    public class PrintingState : State<ModularPrinterContext>
    {
        private enum PrintingPhase
        {
            HomingMPP1,
            PrintingMPP2,
            MovingMPP1,
            WaitingForCompletion
        }

        private PrintingPhase _currentPhase = PrintingPhase.HomingMPP1;
        private bool _mpp1Ready = false;
        private bool _mpp2Ready = false;

        public PrintingState(ModularPrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context._program.Echo("ModularPrinter: Starting print sequence");
            _currentPhase = PrintingPhase.HomingMPP1;
            _mpp1Ready = false;
            _mpp2Ready = false;
            
            // Start by homing MPP1
            _context.SendToMPP1("move 0");
        }

        public override void Execute()
        {
            if (!_context._printingInProgress)
            {
                _context.TransitionTo(new IdleState(_context));
                return;
            }

            switch (_currentPhase)
            {
                case PrintingPhase.HomingMPP1:
                    if (_mpp1Ready)
                    {
                        _currentPhase = PrintingPhase.PrintingMPP2;
                        _mpp2Ready = false;
                        _context.SendToMPP2($"print {_context._section.Width.Value} {_context._section.Height.Value}");
                    }
                    break;

                case PrintingPhase.PrintingMPP2:
                    if (_mpp2Ready)
                    {
                        // Move MPP1 to next position
                        _context._currentXPosition += ModularPrinterContext.X_STEP;
                        
                        if (_context._currentXPosition > _context._section.Length.Value)
                        {
                            // Print sequence complete
                            _context._printingInProgress = false;
                            _context.TransitionTo(new IdleState(_context));
                            return;
                        }

                        _currentPhase = PrintingPhase.MovingMPP1;
                        _mpp1Ready = false;
                        _context.SendToMPP1($"move {_context._currentXPosition}");
                        _context._program.Echo($"Moving MPP1 to position {_context._currentXPosition}");
                    }
                    break;

                case PrintingPhase.MovingMPP1:
                    if (_mpp1Ready)
                    {
                        _currentPhase = PrintingPhase.PrintingMPP2;
                        _mpp2Ready = false;
                        _context.SendToMPP2($"print {_context._section.Width.Value} {_context._section.Height.Value}");
                    }
                    break;
            }
        }

        public void OnMPP1Ready()
        {
            _mpp1Ready = true;
        }

        public void OnMPP2Ready()
        {
            _mpp2Ready = true;
        }
    }

    #endregion

    #region Custom Data Section

    public class MPSection : Section
    {
        public DoubleProperty Length { get; private set; }
        public DoubleProperty Width { get; private set; }
        public DoubleProperty Height { get; private set; }

        public MPSection(ModularPrinterContext context) : base("MP")
        {
            Length = new DoubleProperty("Length", ModularPrinterContext.LENGTH_DEFAULT);
            Width = new DoubleProperty("Width", ModularPrinterContext.WIDTH_DEFAULT);
            Height = new DoubleProperty("Height", ModularPrinterContext.HEIGHT_DEFAULT);

            _properties.Add(Length);
            _properties.Add(Width);
            _properties.Add(Height);
        }
    }

    #endregion
}
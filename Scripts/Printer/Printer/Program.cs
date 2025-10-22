using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private PrinterContext _context;
        private bool _initialized = false;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (_context != null)
            {
                if (!string.IsNullOrEmpty(argument))
                {
                    HandleCommand(argument);
                }
                
                _context.Execute();
            }
        }

        private void HandleCommand(string argument)
        {
            switch (argument.ToLower())
            {
                case "start":
                case "print":
                    _context.StartPrint();
                    break;
                case "stop":
                case "abort":
                    _context.AbortPrint();
                    break;
                case "status":
                    Echo($"Current State: {_context.CurrentState?.GetType().Name ?? "None"}");
                    break;
                default:
                    Echo($"Unknown command: {argument}");
                    Echo("Available commands: start/print, stop/abort, status");
                    break;
            }
        }

        private void Initialize()
        {
            _context = new PrinterContext();
            string errorMessage;
            if (_context.Initialize(this, out errorMessage))
            {
                _initialized = true;
                Echo("Printer initialized successfully");
            }
            else
            {
                Echo($"Printer initialization failed: {errorMessage}");
            }
        }
    }

    public class PrinterContext : Context
    {
        #region Constants
        public const double PISTON_PRINTING_SPEED = 0.1;
        public const double PISTON_NO_PRINTING_SPEED = 1;
        #endregion

        #region Fields
        public Program _program;
        private IMyProjector _projector;
        private List<IMyPistonBase> _pistonsFwd;
        private List<IMyPistonBase> _pistonsRev;
        private List<IMyShipWelder> _welders;
        private List<IMySensorBlock> _stopSensors;
        private IMyRemoteControl _remoteControl;
        private IMyTextSurface _displayPanel;
        private SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock _mergeBlock;
        #endregion

        #region Methods
        public bool Initialize(Program program, out string errorMessage)
        {
            _program = program;
            _pistonsFwd = new List<IMyPistonBase>();
            _pistonsRev = new List<IMyPistonBase>();
            _welders = new List<IMyShipWelder>();
            _stopSensors = new List<IMySensorBlock>();
            errorMessage = string.Empty;

            _remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "No remote control found!";
                return false;
            }


            _welders = program.GetLocalBlocks<IMyShipWelder>();
            _stopSensors = program.GetLocalBlocks<IMySensorBlock>();
            _displayPanel = program.Me.GetSurface(0);
            _mergeBlock = program.GetLocalBlock<SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock>();

            InitializePistons();

            TransitionTo(new ContractingState(this));

            return true;
        }

        private void InitializePistons()
        {
            var allPistons = _program.GetLocalBlocks<IMyPistonBase>();
            
            _pistonsFwd.Clear();
            _pistonsRev.Clear();

            foreach (var piston in allPistons)
            {
                var dotProduct = Vector3D.Dot(_remoteControl.WorldMatrix.Forward, piston.WorldMatrix.Up);
                
                if (dotProduct < 0)
                {
                    _pistonsRev.Add(piston);
                }
                else
                {
                    _pistonsFwd.Add(piston);
                }
            }

            for (int i = 0; i < _pistonsFwd.Count; i++)
            {
                _pistonsFwd[i].CustomName = $"Piston Fwd {i + 1}";
            }

            for (int i = 0; i < _pistonsRev.Count; i++)
            {
                _pistonsRev[i].CustomName = $"Piston Rev {i + 1}";
            }
        }

        public void StartPrint()
        {
            if (CurrentState is ContractedState)
            {
                TransitionTo(new ExtendingState(this));
            }
            else
            {
                _program.Echo("Cannot start print - not in contracted state");
            }
        }

        public void AbortPrint()
        {
            if (CurrentState is ContractedState)
            {
                _program.Echo("Already contracted");
                return;
            }

            SetWeldersEnabled(false);
            TransitionTo(new ContractingState(this));
        }

        public bool IsFullyExtended()
        {
            return _stopSensors.Any(sensor => sensor.IsActive);
        }

        public bool IsFullyContracted()
        {
            bool fwdContracted = _pistonsFwd.All(piston => piston.CurrentPosition <= piston.MinLimit + 0.01f);
            bool revExtended = _pistonsRev.All(piston => piston.CurrentPosition >= piston.MaxLimit - 0.01f);
            return fwdContracted && revExtended;
        }

        public void SetPistonVelocities(bool extend)
        {
            var speed = CurrentState is PrintingState ? PISTON_PRINTING_SPEED : PISTON_NO_PRINTING_SPEED;
            speed = extend ? speed : -speed;
            
            foreach (var piston in _pistonsFwd)
            {
                piston.Velocity = (float)speed;
            }

            foreach (var piston in _pistonsRev)
            {
                piston.Velocity = (float)-speed;
            }
        }

        public void SetWeldersEnabled(bool enabled)
        {
            foreach (var welder in _welders)
            {
                welder.Enabled = enabled;
            }
        }

        public void UpdateDisplay()
        {
            if (_displayPanel != null)
            {
                var stateName = CurrentState?.GetType().Name ?? "None";
                _displayPanel.WriteText(stateName);
            }
        }

        public void SetMergeBlockEnabled(bool enabled)
        {
            _mergeBlock.Enabled = enabled;
        }

        public override void Execute()
        {
            UpdateDisplay();
            base.Execute();
        }
        #endregion
    }

    #region States

    public class ContractedState : State<PrinterContext>
    {
        public ContractedState(PrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context.SetWeldersEnabled(false);
            _context._program.Echo("Contracted State - Ready to print");
        }

        public override void Execute()
        {
        }
    }

    public class ExtendingState : State<PrinterContext>
    {
        public ExtendingState(PrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context.SetPistonVelocities(true);
            _context.SetWeldersEnabled(false);
            _context._program.Echo("Extending State");
        }

        public override void Execute()
        {
            if (_context.IsFullyExtended())
            {
                _context.TransitionTo(new ExtendedState(_context));
            }
        }
    }

    public class ExtendedState : State<PrinterContext>
    {
        public ExtendedState(PrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context.SetWeldersEnabled(false);
            _context._program.Echo("Extended State - Starting print");
        }

        public override void Execute()
        {
            _context.TransitionTo(new PrintingState(_context));
        }
    }

    public class PrintingState : State<PrinterContext>
    {
        public PrintingState(PrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context.SetPistonVelocities(false);
            _context.SetWeldersEnabled(true);
            _context.SetMergeBlockEnabled(true);
            _context._program.Echo("Printing State");
        }

        public override void Execute()
        {
            if (_context.IsFullyContracted())
            {
                _context.SetMergeBlockEnabled(false);
                _context.TransitionTo(new ContractedState(_context));
            }
        }
    }

    public class ContractingState : State<PrinterContext>
    {
        public ContractingState(PrinterContext context) : base(context) { }

        public override void Enter()
        {
            _context.SetPistonVelocities(false);
            _context.SetWeldersEnabled(false);
            _context._program.Echo("Contracting State");
        }

        public override void Execute()
        {
            if (_context.IsFullyContracted())
            {
                _context.TransitionTo(new ContractedState(_context));
            }
        }
    }

    #endregion
}

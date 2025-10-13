using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
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
        private const string BROADCAST_TAG = "TestAlign";
        
        private CustomDataConnector _customDataConnector;
        private Alignment _alignment;
        private IMyUnicastListener _unicastListener;
        private TestAlignContext _context;
        private double _kp = 10.0;
        private double _ki = 0.0;
        private double _kd = 0.0;
        private double _precision = 0.01;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);
            
            _alignment = new Alignment();
            string errorMessage;
            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }
            
            LoadFromStorage();
            
            InitializeCustomData();
            
            ParseCustomData();
            
            _unicastListener = IGC.UnicastListener;
            _unicastListener.SetMessageCallback(BROADCAST_TAG);
            
            _context = new TestAlignContext(this, _alignment);
        }

        private void InitializeCustomData()
        {
            Me.CustomData = "# TestAlignClient Configuration\n" +
                               "# Alignment PID Settings\n" +
                               $"KP: {_kp}\n" +
                               $"KI: {_ki}\n" +
                               $"KD: {_kd}\n" +
                               "\n" +
                               "# Alignment Precision (radians)\n" +
                               $"PRECISION: {_precision}\n";
        }

        private void LoadFromStorage()
        {
            if (string.IsNullOrWhiteSpace(Storage))
            {
                return;
            }

            var parts = Storage.Split(';');
            foreach (var part in parts)
            {
                var keyValue = part.Split(':');
                if (keyValue.Length == 2)
                {
                    double value;
                    if (double.TryParse(keyValue[1], out value))
                    {
                        switch (keyValue[0])
                        {
                            case "KP":
                                _kp = value;
                                _alignment.PidVectorYaw.Kp = value;
                                _alignment.PidVectorPitch.Kp = value;
                                _alignment.PidVectorRoll.Kp = value;
                                break;
                            case "KI":
                                _ki = value;
                                _alignment.PidVectorYaw.Ki = value;
                                _alignment.PidVectorPitch.Ki = value;
                                _alignment.PidVectorRoll.Ki = value;
                                break;
                            case "KD":
                                _kd = value;
                                _alignment.PidVectorYaw.Kd = value;
                                _alignment.PidVectorPitch.Kd = value;
                                _alignment.PidVectorRoll.Kd = value;
                                break;
                            case "PRECISION":
                                _precision = value;
                                _alignment.Precision = value;
                                break;
                        }
                    }
                }
            }
            Echo("Loaded values from storage");
        }

        private void SaveToStorage()
        {
            Storage = $"KP:{_kp};KI:{_ki};KD:{_kd};PRECISION:{_precision}";
        }

        private void ParseCustomData()
        {
            double value;

            if (CustomDataConnector.ParseDouble(Me, "KP", out value))
            {
                _kp = value;
                _alignment.PidVectorYaw.Kp = value;
                _alignment.PidVectorPitch.Kp = value;
                _alignment.PidVectorRoll.Kp = value;
                Echo($"Set KP to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KI", out value))
            {
                _ki = value;
                _alignment.PidVectorYaw.Ki = value;
                _alignment.PidVectorPitch.Ki = value;
                _alignment.PidVectorRoll.Ki = value;
                Echo($"Set KI to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "KD", out value))
            {
                _kd = value;
                _alignment.PidVectorYaw.Kd = value;
                _alignment.PidVectorPitch.Kd = value;
                _alignment.PidVectorRoll.Kd = value;
                Echo($"Set KD to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "PRECISION", out value))
            {
                _precision = value;
                _alignment.Precision = value;
                Echo($"Set Precision to {value}");
            }
        }

        public void Save()
        {
            SaveToStorage();
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.IGC) > 0)
            {
                _context.HandleIGCMessages();
            }
            else if (!string.IsNullOrEmpty(argument))
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
                case "align":
                    _context.StartAlignment();
                    break;
                    
                case "stop":
                    _context.Stop();
                    break;
                    
                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        public class TestAlignContext : Context
        {
            private Program _program;
            private Alignment _alignment;
            private IMyUnicastListener _unicastListener;

            public TestAlignContext(Program program, Alignment alignment)
            {
                _program = program;
                _alignment = alignment;
                _unicastListener = program.IGC.UnicastListener;
                TransitionTo(new IdleState(this));
            }

            public void HandleIGCMessages()
            {
                while (_unicastListener.HasPendingMessage)
                {
                    var message = _unicastListener.AcceptMessage();
                    if (message.Tag == BROADCAST_TAG && message.Data is string)
                    {
                        var data = message.Data.ToString();
                        if (data.StartsWith("alignmentmatrix|"))
                        {
                            HandleAlignmentMatrixResponse(data);
                        }
                    }
                }
            }

            private void HandleAlignmentMatrixResponse(string message)
            {
                var parts = message.Split('|');
                if (parts.Length >= 17)
                {
                    try
                    {
                        var matrix = new MatrixD(
                            double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3]), double.Parse(parts[4]),
                            double.Parse(parts[5]), double.Parse(parts[6]), double.Parse(parts[7]), double.Parse(parts[8]),
                            double.Parse(parts[9]), double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]),
                            double.Parse(parts[13]), double.Parse(parts[14]), double.Parse(parts[15]), double.Parse(parts[16])
                        );
                        
                        _program.Echo($"Received alignment matrix: {matrix.Translation}");
                        
                        if (CurrentState is WaitingForMatrixState)
                        {
                            TransitionTo(new AligningState(this, matrix));
                        }
                    }
                    catch (Exception ex)
                    {
                        _program.Echo($"Error parsing alignment matrix: {ex.Message}");
                    }
                }
            }

            public void StartAlignment()
            {
                if (CurrentState is IdleState)
                {
                    _program.Echo("Broadcasting request for alignment matrix...");
                    _program.IGC.SendBroadcastMessage(BROADCAST_TAG, "getalignmentmatrix");
                    TransitionTo(new WaitingForMatrixState(this));
                }
                else
                {
                    _program.Echo("Already aligning!");
                }
            }

            public void Stop()
            {
                _program.Echo("Stopping alignment...");
                TransitionTo(new IdleState(this));
            }

            public Alignment GetAlignment() { return _alignment; }
            public Program GetProgram() { return _program; }

            private class IdleState : State<TestAlignContext>
            {
                public IdleState(TestAlignContext context) : base(context) { }

                public override void Enter()
                {
                    _context.GetAlignment().Stop();
                    _context.GetProgram().Echo("Idle - Type 'align' to start");
                }

                public override void Execute()
                {
                }
            }

            private class WaitingForMatrixState : State<TestAlignContext>
            {
                public WaitingForMatrixState(TestAlignContext context) : base(context) { }

                public override void Enter()
                {
                    _context.GetProgram().Echo("Waiting for alignment matrix response...");
                }

                public override void Execute()
                {
                }
            }

            private class AligningState : State<TestAlignContext>
            {
                private MatrixD _targetMatrix;

                public AligningState(TestAlignContext context, MatrixD targetMatrix) : base(context)
                {
                    _targetMatrix = targetMatrix;
                }

                public override void Enter()
                {
                    _context.GetProgram().Echo("Starting alignment...");
                }

                public override void Execute()
                {
                    _context.GetProgram().ParseCustomData();
                    
                    if (_context.GetAlignment().AlignWithWorldMatrix(_targetMatrix))
                    {
                        _context.GetProgram().Echo("Alignment complete!");
                        _context.TransitionTo(new IdleState(_context));
                    }
                    else
                    {
                        _context.GetProgram().Echo("Aligning...");
                    }
                }
            }
        }
    }
}

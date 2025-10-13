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
        private const string MINING_PISTON_GROUP_NAME = "Mining Pistons";
        private const double LOWERING_SPEED_DEFAULT = 0.01;
        private const double RAISING_SPEED_DEFAULT = 0.1;
        private const double ROTATION_SPEED_DEFAULT = 0.5;
        private const double CLEARING_PISTON_SPEED_DEFAULT = 0.02;
        private const double CLEARING_ROTOR_SPEED_DEFAULT = 1.0;
        #endregion

        #region Fields
        private List<IMyPistonBase> _upPistons;
        private List<IMyPistonBase> _downPistons;
        private IMyMotorStator _rotor;
        private List<IMyShipDrill> _drills;
        private IMyRemoteControl _remoteControl;
        private IMySensorBlock _drillSensor;
        private IMyTextSurface _surface;
        private SpinningDrillContext _context;
        private double _loweringSpeed = LOWERING_SPEED_DEFAULT;
        private double _raisingSpeed = RAISING_SPEED_DEFAULT;
        private double _rotationSpeed = ROTATION_SPEED_DEFAULT;
        private double _clearingPistonSpeed = CLEARING_PISTON_SPEED_DEFAULT;
        private double _clearingRotorSpeed = CLEARING_ROTOR_SPEED_DEFAULT;
        private bool _terrainClearMode = false;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            _surface = Me.GetSurface(0);
            _surface.ContentType = ContentType.TEXT_AND_IMAGE;

            InitializeComponents();
            ParseCustomData();
            _context = new SpinningDrillContext(this);
        }

        private void InitializeComponents()
        {
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
                return;
            }

            _drillSensor = this.GetLocalBlock<IMySensorBlock>();
            if (_drillSensor == null)
            {
                Echo("Warning: No sensor found!");
            }

            _upPistons = new List<IMyPistonBase>();
            _downPistons = new List<IMyPistonBase>();
            _drills = this.GetLocalBlocks<IMyShipDrill>();

            var allPistons = this.GetLocalBlocksInGroup<IMyPistonBase>(MINING_PISTON_GROUP_NAME);

            if (allPistons.Count == 0)
            {
                Echo($"Warning: No pistons found in group '{MINING_PISTON_GROUP_NAME}'");
            }

            int upCount = 1;
            int downCount = 1;

            foreach (var piston in allPistons)
            {
                if (IsUpPiston(piston))
                {
                    piston.CustomName = $"Up Piston {upCount}";
                    _upPistons.Add(piston);
                    upCount++;
                }
                else
                {
                    piston.CustomName = $"Down Piston {downCount}";
                    _downPistons.Add(piston);
                    downCount++;
                }
            }

            _rotor = this.GetLocalBlock<IMyMotorStator>();
            if (_rotor == null)
            {
                Echo("Error: No rotor found!");
            }

            Echo($"Initialized: {_upPistons.Count} up pistons, {_downPistons.Count} down pistons, {_drills.Count} drills");
        }

        private bool IsUpPiston(IMyPistonBase piston)
        {
            var remoteUp = _remoteControl.WorldMatrix.Up;
            var pistonUp = piston.WorldMatrix.Up;
            
            var dot = Vector3D.Dot(remoteUp, pistonUp);
            
            return dot > 0.5;
        }

        private void ParseCustomData()
        {
            double value;

            if (CustomDataConnector.ParseDouble(Me, "LOWERING_SPEED", out value))
            {
                _loweringSpeed = value;
                Echo($"Set lowering speed to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "RAISING_SPEED", out value))
            {
                _raisingSpeed = value;
                Echo($"Set raising speed to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "ROTATION_SPEED", out value))
            {
                _rotationSpeed = value;
                Echo($"Set rotation speed to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "CLEARING_PISTON_SPEED", out value))
            {
                _clearingPistonSpeed = value;
                Echo($"Set clearing piston speed to {value}");
            }

            if (CustomDataConnector.ParseDouble(Me, "CLEARING_ROTOR_SPEED", out value))
            {
                _clearingRotorSpeed = value;
                Echo($"Set clearing rotor speed to {value}");
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
                case "start":
                    _context.Start();
                    break;

                case "clear":
                    _terrainClearMode = !_terrainClearMode;
                    Echo($"Terrain Clear Mode: {(_terrainClearMode ? "ON" : "OFF")}");
                    break;

                case "stop":
                    _context.Stop();
                    break;

                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        private void UpdateDisplay(string state)
        {
            if (_surface != null)
            {
                _surface.WriteText($"Drill State: {state}\n" +
                $"Terrain Clear: {(_terrainClearMode ? "ON" : "OFF")}\n\n" +
                $"Mining Speeds:\n" +
                $"  Lowering: {_loweringSpeed:F3}\n" +
                $"  Raising: {_raisingSpeed:F2}\n" +
                $"  Rotation: {_rotationSpeed:F2} RPM\n\n" +
                $"Clearing Speeds:\n" +
                $"  Piston: {_clearingPistonSpeed:F3}\n" +
                $"  Rotor: {_clearingRotorSpeed:F2} RPM");
            }
        }

        public class SpinningDrillContext : Context
        {
            private Program _program;

            public SpinningDrillContext(Program program)
            {
                _program = program;
                TransitionTo(new IdleState(this));
            }

            public void Start()
            {
                _program.Echo("Starting drilling operation...");
                if (CurrentState is IdleState)
                {
                    TransitionTo(new MiningState(this));
                }
            }

            public void Stop()
            {
                _program.Echo("Stopping drilling operation...");
                if (CurrentState is MiningState)
                {
                    TransitionTo(new RaisingState(this));
                }
            }

            private class IdleState : State<SpinningDrillContext>
            {
                public IdleState(SpinningDrillContext context) : base(context) { }

                public override void Enter()
                {
                    foreach (var drill in _context._program._drills)
                    {
                        drill.Enabled = false;
                    }

                    if (_context._program._rotor != null)
                    {
                        _context._program._rotor.TargetVelocityRPM = 0;
                    }

                    foreach (var piston in _context._program._upPistons)
                    {
                        piston.Velocity = 0;
                    }

                    foreach (var piston in _context._program._downPistons)
                    {
                        piston.Velocity = 0;
                    }

                    _context._program.Echo("Idle State: All drills raised and stopped");
                }

                public override void Execute()
                {
                    _context._program.UpdateDisplay("Idle");
                }
            }

            private class MiningState : State<SpinningDrillContext>
            {
                public MiningState(SpinningDrillContext context) : base(context) { }

                public override void Enter()
                {
                    _context._program.ParseCustomData();

                    foreach (var drill in _context._program._drills)
                    {
                        drill.Enabled = true;
                    }

                    _context._program.Echo("Mining State: Drilling operation started");
                }

                private double GetPistonSpeed()
                {
                    return _context._program._terrainClearMode ? _context._program._clearingPistonSpeed : _context._program._loweringSpeed;
                }

                private double GetRotorSpeed()
                {
                    return _context._program._terrainClearMode ? _context._program._clearingRotorSpeed : _context._program._rotationSpeed;
                }

                public override void Execute()
                {
                    _context._program.ParseCustomData();

                    bool upPistonsContracted = _context._program._upPistons.All(p => p.CurrentPosition <= 0.1);
                    bool downPistonsExtended = _context._program._downPistons.All(p => p.CurrentPosition >= p.MaxLimit - 0.1);

                    if (upPistonsContracted && downPistonsExtended)
                    {
                        _context._program.Echo("Mining complete, raising drill");
                        _context.TransitionTo(new RaisingState(_context));
                        return;
                    }

                    foreach (var drill in _context._program._drills)
                    {
                        drill.TerrainClearingMode = _context._program._terrainClearMode;
                    }

                    _context._program._rotor.TargetVelocityRPM = (float)GetRotorSpeed();

                    bool sensorInactive = !_context._program._drillSensor.IsActive;
                    float speed = sensorInactive ? (float)_context._program._raisingSpeed : (float)GetPistonSpeed();

                    foreach (var piston in _context._program._upPistons)
                    {
                        piston.Velocity = -speed;
                    }

                    foreach (var piston in _context._program._downPistons)
                    {
                        piston.Velocity = speed;
                    }

                    string displayState = _context._program._terrainClearMode ? "Clearing" : "Mining";
                    _context._program.UpdateDisplay(displayState);
                }
            }

            private class RaisingState : State<SpinningDrillContext>
            {
                public RaisingState(SpinningDrillContext context) : base(context) { }

                public override void Enter()
                {
                    if (_context._program._rotor != null)
                    {
                        _context._program._rotor.TargetVelocityRPM = 0;
                    }

                    foreach (var piston in _context._program._upPistons)
                    {
                        piston.Velocity = (float)_context._program._raisingSpeed;
                    }

                    foreach (var piston in _context._program._downPistons)
                    {
                        piston.Velocity = (float)(-_context._program._raisingSpeed);
                    }

                    _context._program.Echo("Raising State: Raising drill assembly");
                }

                public override void Execute()
                {
                    bool upPistonsExtended = _context._program._upPistons.All(p => p.CurrentPosition >= p.MaxLimit - 0.1);
                    bool downPistonsContracted = _context._program._downPistons.All(p => p.CurrentPosition <= 0.1);

                    if (upPistonsExtended && downPistonsContracted)
                    {
                        foreach (var drill in _context._program._drills)
                        {
                            drill.Enabled = false;
                        }
                        _context._program.Echo("Drill raised, returning to idle");
                        _context.TransitionTo(new IdleState(_context));
                    }

                    _context._program.UpdateDisplay("Raising");
                }
            }
        }
    }
}

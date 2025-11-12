using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRage.Game;
using VRageMath;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private CustomDataConnector _customDataConnector;
        private Navigation _navigation;
        private Alignment _alignment;
        private SpiralMinerController _spiralMiner;
        private List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            _navigation = new Navigation();
            _alignment = new Alignment();

            string errorMessage;
            if (!_navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            var remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (remoteControl == null)
            {
                Echo("Error: No remote control found on this grid");
            }

            var statusLCDs = this.GetLocalBlocks<IMyTextPanel>();
            foreach (var lcd in statusLCDs)
            {
                if (lcd.CustomName.Contains("[SM]"))
                {
                    lcd.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                    _statusSurfaces.Add(lcd);
                }
            }

            var cockpit = this.GetLocalBlock<IMyCockpit>();
            if (cockpit != null && cockpit.SurfaceCount > 0)
            {
                var surface = cockpit.GetSurface(0);
                surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                _statusSurfaces.Add(surface);
            }

            _spiralMiner = new SpiralMinerController(this, _customDataConnector, _navigation, _alignment, remoteControl, _statusSurfaces);
            _customDataConnector.Load();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                HandleCommand(argument);
            }

            _spiralMiner.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            var trimmed = argument.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();

            Echo($"SpiralMiner: HandleCommand: {command}");
            switch (command)
            {
                case "start":
                    if (_spiralMiner.StartMining())
                    {
                        Echo("Spiral miner starting...");
                    }
                    break;

                case "stop":
                    _spiralMiner.StopMining();
                    Echo("Spiral miner stopping...");
                    break;

                case "home":
                    _spiralMiner.ReturnHome();
                    Echo("Spiral miner returning home...");
                    break;

                case "pf_start":
                    _spiralMiner.OnPathfinderEvent("start");
                    break;

                case "pf_stop":
                    _spiralMiner.OnPathfinderEvent("stop");
                    break;

                case "pf_nopath":
                    _spiralMiner.OnPathfinderEvent("nopath");
                    break;

                case "ad_docked":
                    _spiralMiner.OnAutoDockEvent("docked");
                    break;

                case "ad_undocked":
                    _spiralMiner.OnAutoDockEvent("undocked");
                    break;

                default:
                    Echo("Commands: start, stop");
                    break;
            }
        }

        private class SpiralMinerController : Context
        {
            private readonly Program _program;
            private readonly CustomDataConnector _customDataConnector;
            private readonly Navigation _navigation;
            private readonly Alignment _alignment;
            private readonly IMyRemoteControl _remoteControl;
            private List<IMyShipDrill> _drills = new List<IMyShipDrill>();
            private List<IMyCargoContainer> _cargoBlocks = new List<IMyCargoContainer>();
            private List<IMyBatteryBlock> _batteryBlocks = new List<IMyBatteryBlock>();
            private IMySensorBlock _forwardSensor;
            private readonly List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();
            private IMyProgrammableBlock _pathfinderProgrammableBlock;
            private IMyProgrammableBlock _autodockProgrammableBlock;

            private readonly SpiralMinerSection _section;
            private bool _firstExecution = true;
            private bool _initialPositionCaptured;
            private Vector3D _planarReference;
            private bool _planarReferenceInitialized;
            private bool _initialized;
            private bool _isDocked;
            private const double SENSOR_FALLBACK_LOWER_STEP = 25.0;

            public SpiralMinerController(Program program, CustomDataConnector customDataConnector, Navigation navigation, Alignment alignment, IMyRemoteControl remoteControl, List<IMyTextSurface> statusSurfaces)
            {
                _program = program;
                _customDataConnector = customDataConnector;
                _navigation = navigation;
                _alignment = alignment;
                _remoteControl = remoteControl;
                if (statusSurfaces != null)
                {
                    _statusSurfaces.AddRange(statusSurfaces);
                }

                if (_remoteControl != null)
                {
                    _drills = _program.GetLocalBlocks<IMyShipDrill>();
                }

                _batteryBlocks = _program.GetLocalBlocks<IMyBatteryBlock>();
                var forwardSensors = _program.GetLocalBlocksNameContains<IMySensorBlock>("[SM]");
                if (forwardSensors.Count > 0)
                {
                    _forwardSensor = forwardSensors[0];
                }
                else
                {
                    _forwardSensor = _program.GetLocalBlock<IMySensorBlock>();
                }

                var programmableBlocks = _program.GetLocalBlocks<IMyProgrammableBlock>();
                foreach (var programmableBlock in programmableBlocks)
                {
                    if (programmableBlock.EntityId == _program.Me.EntityId)
                    {
                        continue;
                    }

                    var lowerName = programmableBlock.CustomName.ToLower();
                    if (_pathfinderProgrammableBlock == null && lowerName.Contains("[pf]"))
                    {
                        _pathfinderProgrammableBlock = programmableBlock;
                    }
                    else if (_autodockProgrammableBlock == null && lowerName.Contains("[ad]"))
                    {
                        _autodockProgrammableBlock = programmableBlock;
                    }
                }

                var dockingConnectors = _program.GetLocalBlocksNameContains<IMyShipConnector>("[AD]");
                if (dockingConnectors.Count > 0)
                {
                    _isDocked = dockingConnectors[0].IsConnected;
                }

                _section = new SpiralMinerSection();
                _customDataConnector.AddSection(_section);

                _initialized = _remoteControl != null &&
                               _pathfinderProgrammableBlock != null &&
                               _autodockProgrammableBlock != null &&
                               _forwardSensor != null;

                if (!_initialized)
                {
                    if (_remoteControl == null)
                    {
                        _program.Echo("SpiralMiner: Remote control not found");
                    }
                    if (_pathfinderProgrammableBlock == null)
                    {
                        _program.Echo("SpiralMiner: Pathfinder programmable block [PF] not found");
                    }
                    if (_autodockProgrammableBlock == null)
                    {
                        _program.Echo("SpiralMiner: AutoDock programmable block [AD] not found");
                    }
                    if (_forwardSensor == null)
                    {
                        _program.Echo("SpiralMiner: Forward sensor not found");
                    }
                }

                _program.Echo($"SpiralMiner: Initialized: {_initialized}");

                TransitionTo(new IdleState(this));
            }

            public override void Execute()
            {
                if (!_initialized)
                {
                    return;
                }

                base.Execute();
            }

            public bool StartMining()
            {
                if (!_initialized)
                {
                    _program.Echo("SpiralMiner: Not initialized");
                    return false;
                }

                if (!(CurrentState is IdleState))
                {
                    _program.Echo("SpiralMiner: Already running");
                    return false;
                }

                if (_drills.Count == 0)
                {
                    _drills = _program.GetLocalBlocks<IMyShipDrill>();
                }

                if (_drills.Count == 0)
                {
                    _program.Echo("SpiralMiner: No drills found");
                    return false;
                }

                if (!HasValidGps(_section.MiningSiteGPS) || !HasValidGps(_section.BaseGPS))
                {
                    _program.Echo("SpiralMiner: Configure BaseGPS and MiningSiteGPS");
                    return false;
                }

                _initialPositionCaptured = false;
                _planarReferenceInitialized = false;
                _section.StartOnLoad.Value = true;

                if (_isDocked)
                {
                    TransitionTo(new WaitForCargoEmptyState(this));
                }
                else
                {
                    if (GetBatteryChargeRatio() < _section.BatteryReturnThreshold.Value)
                    {
                        TransitionTo(new NavigateToBaseState(this));
                        return true;
                    }
                    ProceedAfterUndock();
                }

                return true;
            }

            public void StopMining()
            {
                _section.StartOnLoad.Value = false;
                _section.OrbitRadiusState.Value = 0;
                _section.ContractingState.Value = false;
                _section.MiningDepthState.Value = 0;
                _section.ContractingState.Value = false;

                TransitionTo(new IdleState(this));
                _pathfinderProgrammableBlock.TryRun("stop");
                _autodockProgrammableBlock.TryRun("stop");
                _navigation.Stop();
                _alignment.Stop();
            }

            public void ReturnHome()
            {
                _section.StartOnLoad.Value = false;
                if (CurrentState is RaisingState || 
                    CurrentState is NavigateToBaseState || 
                    CurrentState is DockingState)
                {
                    return;
                }
                if (CurrentState is WaitForCargoEmptyState)
                {
                    TransitionTo(new IdleState(this));
                    return;
                }
                if (CurrentState is IdleState)
                {
                    // if not docked, return home
                    if (!_isDocked)
                    {
                        TransitionTo(new NavigateToBaseState(this));
                    }
                    return;
                }
            }

            public void OnPathfinderEvent(string eventName)
            {
                _program.Echo($"SpiralMiner: OnPathfinderEvent: {eventName}");
                var state = CurrentState as SpiralMinerState;
                if (state == null)
                {
                    return;
                }

                switch (eventName)
                {
                    case "start":
                        state.OnPathfinderStarted();
                        break;
                    case "stop":
                        state.OnPathfinderStopped();
                        break;
                    case "nopath":
                        state.OnPathfinderNoPath();
                        break;
                }
            }

            public void OnAutoDockEvent(string eventName)
            {
                var state = CurrentState as SpiralMinerState;
                if (state == null)
                {
                    return;
                }

                if (eventName == "docked")
                {
                    _isDocked = true;
                    state.OnAutoDockDocked();
                }
                else if (eventName == "undocked")
                {
                    _isDocked = false;
                    state.OnAutoDockUndocked();
                }
            }

            public Vector3D GetUpDirection()
            {
                var gravity = _remoteControl?.GetNaturalGravity() ?? Vector3D.Zero;
                if (gravity.LengthSquared() > 1e-6)
                {
                    return -Vector3D.Normalize(gravity);
                }
                return _remoteControl?.WorldMatrix.Up ?? Vector3D.Up;
            }

            public Vector3D GetPlanarReference()
            {
                if (!_planarReferenceInitialized)
                {
                    var up = GetUpDirection();
                    var forward = _remoteControl?.WorldMatrix.Forward ?? Vector3D.Forward;
                    var planar = forward - Vector3D.Dot(forward, up) * up;
                    if (planar.LengthSquared() < 1e-6)
                    {
                        planar = Vector3D.CalculatePerpendicularVector(up);
                    }
                    planar.Normalize();
                    _planarReference = planar;
                    _planarReferenceInitialized = true;
                }
                return _planarReference;
            }

            private bool IsCargoFull()
            {
                return GetCargoFillRatio() >= _section.CargoFullThreshold.Value;
            }

            private bool IsCargoEmpty()
            {
                return GetCargoFillRatio() <= _section.CargoEmptyThreshold.Value;
            }

            private double GetBatteryChargeRatio()
            {
                if (_batteryBlocks.Count == 0)
                {
                    _batteryBlocks = _program.GetLocalBlocks<IMyBatteryBlock>();
                }

                double stored = 0;
                double maxStored = 0;

                foreach (var battery in _batteryBlocks)
                {
                    stored += battery.CurrentStoredPower;
                    maxStored += battery.MaxStoredPower;
                }

                if (maxStored <= 0)
                {
                    return 0;
                }

                return stored / maxStored;
            }

            private void SetDrills(bool enabled, bool terrainClearingMode)
            {
                foreach (var drill in _drills)
                {
                    drill.Enabled = enabled;
                    drill.TerrainClearingMode = terrainClearingMode;
                }
            }

            private void SetBatteryRechargeMode(bool recharge)
            {
                if (_batteryBlocks.Count == 0)
                {
                    _batteryBlocks = _program.GetLocalBlocks<IMyBatteryBlock>();
                }

                foreach (var battery in _batteryBlocks)
                {
                    if (battery == null)
                    {
                        continue;
                    }

                    battery.ChargeMode = recharge ? ChargeMode.Recharge : ChargeMode.Auto;
                }
            }

            private void UpdateStatus(string state, params string[] details)
            {
                if (_statusSurfaces.Count == 0)
                {
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("=== Spiral Miner ===");
                builder.AppendLine($"State: {state}");
                if (details != null)
                {
                    foreach (var line in details)
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            builder.AppendLine(line);
                        }
                    }
                }
                var text = builder.ToString();
                foreach (var surface in _statusSurfaces)
                {
                    surface.WriteText(text);
                }
            }

            private string BuildGpsString(GPSProperty property, string defaultName)
            {
                if (property == null)
                {
                    return null;
                }

                var destination = property.Value;
                if (destination.LengthSquared() < 1e-3)
                {
                    return null;
                }

                var gpsString = property.ValueToString();
                if (string.IsNullOrEmpty(gpsString) || gpsString.StartsWith(":"))
                {
                    gpsString = $"{defaultName}:{destination.X}:{destination.Y}:{destination.Z}";
                }

                return gpsString;
            }

            private bool SendPathfinderCommand(GPSProperty property, string defaultName)
            {
                if (_pathfinderProgrammableBlock == null)
                {
                    _program.Echo("SpiralMiner: Pathfinder programmable block not linked");
                    return false;
                }

                var gpsString = BuildGpsString(property, defaultName);
                if (string.IsNullOrEmpty(gpsString))
                {
                    _program.Echo($"SpiralMiner: GPS '{defaultName}' not configured");
                    return false;
                }

                var command = $"start {gpsString} {_program.Me.CustomName}";
                if (!_pathfinderProgrammableBlock.TryRun(command))
                {
                    _program.Echo($"SpiralMiner: Failed to start pathfinding with '{command}'");
                    return false;
                }

                return true;
            }

            private bool SendAutoDockCommand(string command, string connectorArg, bool includeCallback)
            {
                if (_autodockProgrammableBlock == null)
                {
                    _program.Echo("SpiralMiner: AutoDock programmable block not linked");
                    return false;
                }

                var builder = new StringBuilder(command);
                if (!string.IsNullOrEmpty(connectorArg))
                {
                    builder.Append(' ');
                    builder.Append(connectorArg);
                }

                if (includeCallback)
                {
                    var callbackName = _program.Me.CustomName;
                    if (!string.IsNullOrEmpty(callbackName))
                    {
                        builder.Append(' ');
                        builder.Append(callbackName);
                    }
                }

                var commandString = builder.ToString();
                if (!_autodockProgrammableBlock.TryRun(commandString))
                {
                    _program.Echo($"SpiralMiner: Failed to run AutoDock command '{commandString}'");
                    return false;
                }

                return true;
            }

            private bool RequestDock()
            {
                return SendAutoDockCommand("dock", _section.DockingConnectorName.Value, true);
            }

            private bool RequestUndock()
            {
                return SendAutoDockCommand("undock", null, true);
            }

            private double GetCargoFillRatio()
            {
                if (_cargoBlocks.Count == 0)
                {
                    _cargoBlocks = _program.GetLocalBlocks<IMyCargoContainer>();
                }

                double totalVolume = 0;
                double usedVolume = 0;

                foreach (var block in _cargoBlocks)
                {
                    for (int i = 0; i < block.InventoryCount; i++)
                    {
                        var inventory = block.GetInventory(i);
                        totalVolume += (double)inventory.MaxVolume;
                        usedVolume += (double)inventory.CurrentVolume;
                    }
                }

                if (totalVolume <= 0)
                {
                    return 0;
                }

                return usedVolume / totalVolume;
            }

            private bool HasValidGps(GPSProperty property)
            {
                if (property == null)
                {
                    return false;
                }

                return property.Value.LengthSquared() >= 1e-3;
            }

            private void ProceedAfterUndock()
            {
                if (IsCargoFull())
                {
                    TransitionTo(new NavigateToBaseState(this));
                }
                else
                {
                    TransitionTo(new NavigateToSiteState(this));
                }
            }

            private abstract class SpiralMinerState : State<SpiralMinerController>
            {
                protected SpiralMinerState(SpiralMinerController context) : base(context) { }

                public virtual void OnPathfinderStarted() { }
                public virtual void OnPathfinderStopped() { }
                public virtual void OnPathfinderNoPath() { }
                public virtual void OnAutoDockDocked() { }
                public virtual void OnAutoDockUndocked() { }
            }

            private class IdleState : SpiralMinerState
            {
                public IdleState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.SetDrills(false, false);
                    _context._navigation.Stop();
                    _context._alignment.Stop();

                    if (_context._initialized)
                    {
                        _context.UpdateStatus("Idle");
                    }
                    else
                    {
                        _context.UpdateStatus("Idle", "Initialization incomplete");
                    }
                }

                public override void Execute()
                {
                    if (_context._firstExecution && _context._section.StartOnLoad.Value)
                    {
                        _context._firstExecution = false;
                        _context.StartMining();
                    }
                }
            }

            private class UndockingState : SpiralMinerState
            {
                public UndockingState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.SetBatteryRechargeMode(false);
                    _context.SetDrills(false, false);
                    _context._navigation.Stop();
                    _context._alignment.Stop();
                    _context.UpdateStatus("Undocking");
                    if (!_context.RequestUndock())
                    {
                        _context.TransitionTo(new IdleState(_context));
                    }
                }

                public override void OnAutoDockUndocked()
                {
                    _context.ProceedAfterUndock();
                }
            }

            private class NavigateToSiteState : SpiralMinerState
            {
                private bool _commandIssued;

                public NavigateToSiteState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context._program.Echo("SpiralMiner: NavigateToSiteState: Enter");
                    _context.UpdateStatus("NavigateToSite");
                    _commandIssued = true;
                    if (!_context.SendPathfinderCommand(_context._section.MiningSiteGPS, "MiningSite"))
                    {
                        _commandIssued = false;
                        _context.TransitionTo(new IdleState(_context));
                    }
                }

                public override void OnPathfinderStopped()
                {
                    if (_commandIssued)
                    {
                        _context.TransitionTo(new LoweringState(_context));
                    }
                }

                public override void OnPathfinderNoPath()
                {
                    _context.UpdateStatus("NavigateToSite", "Path not found");
                    _context.TransitionTo(new IdleState(_context));
                }
            }

            private class LoweringState : SpiralMinerState
            {
                private Vector3D _target;
                private bool _aligned;
                private bool _repositioned;
                private bool _sensorInitialized;
                private bool _sensorScanning;
                private int _sensorWaiting;
                private bool _sensorFound;
                private bool _targetComputed;
                private bool _rescanAfterLower;
                private double _sensorMinRange;
                private double _sensorMaxRange;
                private double _sensorCurrentRange;
                private double _sensorResult;
                private int _sensorIteration;
                private float _originalSensorFront;

                public LoweringState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    if (_context._remoteControl == null)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    _context._program.Echo("Lowering to mining depth using forward sensor");
                    _context.UpdateStatus("Lowering");
                    _sensorInitialized = false;
                    _sensorScanning = false;
                    _sensorFound = false;
                    _targetComputed = false;
                    _originalSensorFront = _context._forwardSensor.FrontExtend;
                    _repositioned = false;
                }

                public override void Execute()
                {
                    if (_context._remoteControl == null)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    // Align with the mining site first
                    if (!_aligned)
                    {
                        if (_context._alignment.AlignWithYawPitchRoll(0, 90, 0))
                        {
                            _aligned = true;
                        }
                        return;
                    }

                    // Reposition to the start position
                    if (!_repositioned)
                    {
                        if (_context._navigation.NavigateTo(_context._section.MiningSiteGPS.Value, _context._section.RepositioningMovementSpeed.Value))
                        {
                            _repositioned = true;
                        }
                        return;
                    }

                    if (!_sensorInitialized)
                    {
                        if (!InitializeSensorScan())
                        {
                            _context._program.Echo("SpiralMiner: Forward sensor scan initialization failed");
                            _context.TransitionTo(new IdleState(_context));
                        }
                        return;
                    }

                    if (_sensorScanning)
                    {
                        ContinueSensorScan();
                        return;
                    }

                    if (!_sensorFound && !_targetComputed)
                    {
                        LowerToSensorRange();
                    }

                    if (!_targetComputed)
                    {
                        var down = -_context.GetUpDirection();
                        var distance = Math.Max(0, _sensorResult);
                        _target = _context._remoteControl.GetPosition() + down * distance;
                        _targetComputed = true;
                    }

                    // Navigate to the target position
                    var arrived = _context._navigation.NavigateTo(_target, _context._section.RepositioningMovementSpeed.Value);
                    var remaining = Vector3D.Distance(_context._remoteControl.GetPosition(), _target);
                    _context.UpdateStatus("Lowering", $"Remaining: {remaining:F1} m");

                    if (arrived)
                    {
                        if (_rescanAfterLower)
                        {
                            _rescanAfterLower = false;
                            _sensorInitialized = false;
                            _sensorScanning = false;
                            _sensorFound = false;
                            _targetComputed = false;
                            return;
                        }

                        if (!_context._initialPositionCaptured)
                        {
                            _context._initialPositionCaptured = true;
                        }
                        _context._planarReferenceInitialized = false;
                        _context.TransitionTo(new MiningState(_context));
                    }
                }

                private void LowerToSensorRange()
                {
                    var down = -_context.GetUpDirection();
                    if (down.LengthSquared() < 1e-6)
                    {
                        _context._program.Echo("SpiralMiner: Unable to determine direction for fallback lowering");
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    var fallbackTarget = _context._remoteControl.GetPosition() + down * SENSOR_FALLBACK_LOWER_STEP;
                    _target = fallbackTarget;
                    _targetComputed = true;
                    _rescanAfterLower = true;
                    _sensorResult = 0;
                }

                private bool InitializeSensorScan()
                {
                    _context._forwardSensor.Enabled = true;

                    _sensorMinRange = _originalSensorFront;
                    _sensorMaxRange = 50;
                    _sensorIteration = 0;
                    _sensorResult = 0;
                    _sensorFound = false;

                    _sensorCurrentRange = _sensorMaxRange;
                    _context._forwardSensor.FrontExtend = (float)_sensorCurrentRange;
                    _sensorWaiting = 10;
                    _sensorScanning = true;
                    _sensorInitialized = true;
                    return true;
                }

                private void ContinueSensorScan()
                {
                    if (_sensorWaiting > 0)
                    {
                        _sensorWaiting--;
                        return;
                    }

                    if (_context._forwardSensor.IsActive)
                    {
                        _sensorFound = true;
                        _sensorMaxRange = _sensorCurrentRange;
                    }
                    else
                    {
                        _sensorMinRange = _sensorCurrentRange;
                    }

                    _sensorIteration++;
                    if ((_sensorFound && Math.Abs(_sensorMaxRange - _sensorMinRange) <= 0.1) ||
                        _sensorIteration >= 100 ||
                        _sensorCurrentRange >= 50 && !_sensorFound)
                    {
                        CleanupSensor();
                        _sensorScanning = false;

                        if (_sensorFound)
                        {
                            _sensorResult = Math.Max(0, (_sensorMaxRange + _sensorMinRange) * 0.5 - _originalSensorFront);
                        }
                        else
                        {
                            LowerToSensorRange();
                        }
                        return;
                    }

                    _sensorCurrentRange = (_sensorMinRange + _sensorMaxRange) * 0.5;
                    _context._forwardSensor.FrontExtend = (float)_sensorCurrentRange;
                    _sensorWaiting = 10;
                }

                private void CleanupSensor()
                {
                    _context._forwardSensor.FrontExtend = _originalSensorFront;
                    _context._forwardSensor.Enabled = false;
                }
            }

            private class MiningState : SpiralMinerState
            {
                private double _lastAngle;
                private double _accumulatedAngle;
                private Vector3D _miningStart;

                private bool IsTerrainClearing
                {
                    get
                    {
                        return _context._section.MiningDepthState.Value < _context._section.TerrainClearingDepth.Value;
                    }
                }

                private bool IsMiningCompleted
                {
                    get
                    {
                        return _context._section.MiningDepthState.Value >= _context._section.Depth.Value;
                    }
                }

                public MiningState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _lastAngle = 0;
                    _accumulatedAngle = 0;
                    _miningStart = _context._remoteControl.GetPosition();
                    _context._program.Echo($"Mining start: {_miningStart}");
                }

                public override void Execute()
                {
                    var upDirection = _context.GetUpDirection();
                    var mode = IsTerrainClearing ? "Terrain Clearing" : "Mining";
                    _context.UpdateStatus("Mining",
                            $"Mode: {mode}",
                            $"Radius: {_context._section.OrbitRadiusState.Value:F1} m",
                            $"Mined Depth: {_context._section.MiningDepthState.Value:F1} m",
                            $"Cargo: {_context.GetCargoFillRatio():P0}",
                            $"Batteries: {_context.GetBatteryChargeRatio():P0}");

                    if (_context.GetBatteryChargeRatio() < _context._section.BatteryReturnThreshold.Value)
                    {
                        _context.TransitionTo(new RaisingState(_context));
                        return;
                    }

                    _context.SetDrills(true, IsTerrainClearing);

                    if (_context.IsCargoFull() || IsMiningCompleted)
                    {
                        _context.TransitionTo(new RaisingState(_context));
                        return;
                    }

                    var toPosition = _context._remoteControl.GetPosition() - _miningStart;
                    var planar = toPosition - Vector3D.Dot(toPosition, upDirection) * upDirection;
                    if (planar.LengthSquared() > 1e-6)
                    {
                        planar.Normalize();
                        var reference = _context.GetPlanarReference();
                        var angle = Math.Atan2(Vector3D.Dot(Vector3D.Cross(reference, planar), upDirection), Vector3D.Dot(reference, planar));
                        if (!double.IsNaN(angle))
                        {
                            var delta = angle - _lastAngle;
                            if (delta > Math.PI)
                            {
                                delta -= 2 * Math.PI;
                            }
                            else if (delta < -Math.PI)
                            {
                                delta += 2 * Math.PI;
                            }
                            _accumulatedAngle += delta;
                            _lastAngle = angle;
                        }
                    }

                    var clampedRadius = MathHelper.Clamp(_context._section.OrbitRadiusState.Value, 0.1, _context._section.MaxOrbit.Value);
                    AlignWithMining(upDirection, clampedRadius, planar);

                    if (Math.Abs(_accumulatedAngle) >= 2 * Math.PI)
                    {
                        _accumulatedAngle = 0;
                        var radiusIncrement = _context._section.ContractingState.Value ? 
                            -_context._section.OrbitRadiusIncrement.Value : 
                            _context._section.OrbitRadiusIncrement.Value;
                        var reachedOrbitLimit = _context._section.ContractingState.Value ? 
                            _context._section.OrbitRadiusState.Value + radiusIncrement < 0 : 
                            _context._section.OrbitRadiusState.Value + radiusIncrement > _context._section.MaxOrbit.Value;
                        if (reachedOrbitLimit)
                        {
                            _context._program.Echo($"Limit: {_context._section.ContractingState.Value} {_context._section.OrbitRadiusState.Value} {radiusIncrement} {_context._section.MaxOrbit.Value}");
                            var depthIncrement = IsTerrainClearing ? 
                                _context._section.TerrainClearingStep.Value :
                                _context._section.MiningStep.Value;
                            _context._section.MiningDepthState.Value += depthIncrement;
                            _context._section.ContractingState.Value = !_context._section.ContractingState.Value;
                            _miningStart -= depthIncrement * upDirection;
                        }
                        else
                        {
                            _context._section.OrbitRadiusState.Value += radiusIncrement;
                        }
                    }


                    var radiusForOrbit = Math.Max(0.1, _context._section.OrbitRadiusState.Value);
                    var movementSpeed = IsTerrainClearing ? 
                        _context._section.TerrainClearingMovementSpeed.Value : 
                        _context._section.MiningMovementSpeed.Value;
                    _context._navigation.OrbitPoint(_miningStart, radiusForOrbit, upDirection, movementSpeed);
                }

                private void AlignWithMining(Vector3D upDirection, double currentRadius, Vector3D planarDirection)
                {
                    var planar = planarDirection;
                    if (planar.LengthSquared() < 1e-6)
                    {
                        planar = _context.GetPlanarReference();
                    }
                    planar.Normalize();

                    var downward = -upDirection;
                    var maxOrbit = _context._section.MaxOrbit.Value;
                    var ratio = maxOrbit > 0 ? MathHelper.Clamp(currentRadius / maxOrbit, 0.0, 1.0) : 0.0;
                    var maxPitchRadians = MathHelper.ToRadians(_context._section.MaxPitch.Value);
                    var pitchRadians = ratio * maxPitchRadians;

                    var rotationAxis = Vector3D.Cross(downward, planar);
                    if (rotationAxis.LengthSquared() < 1e-6)
                    {
                        rotationAxis = Vector3D.Cross(downward, _context.GetPlanarReference());
                    }
                    if (rotationAxis.LengthSquared() < 1e-6)
                    {
                        rotationAxis = _context.GetPlanarReference();
                    }
                    rotationAxis.Normalize();

                    var rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, pitchRadians);
                    var forwardVector = Vector3D.Normalize(Vector3D.Transform(downward, rotationMatrix));

                    var rightVector = Vector3D.Cross(upDirection, forwardVector);
                    if (rightVector.LengthSquared() < 1e-6)
                    {
                        rightVector = Vector3D.Cross(upDirection, _context.GetPlanarReference());
                    }
                    if (rightVector.LengthSquared() < 1e-6)
                    {
                        rightVector = _context.GetPlanarReference();
                    }
                    rightVector.Normalize();
                    var adjustedUp = Vector3D.Normalize(Vector3D.Cross(forwardVector, rightVector));

                    var targetMatrix = MatrixD.CreateWorld(_context._remoteControl.GetPosition(), forwardVector, adjustedUp);
                    _context._alignment.AlignWithWorldMatrix(targetMatrix);
                }
            }

            private class RaisingState : SpiralMinerState
            {
                private bool _navigated;

                public RaisingState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.SetDrills(false, false);
                    _context._navigation.Stop();
                    _context._alignment.Stop();
                    _context.UpdateStatus("Raising");
                }

                public override void Execute()
                {
                    if (!_navigated)
                    {
                        if (_context._navigation.NavigateTo(_context._section.MiningSiteGPS.Value, _context._section.RepositioningMovementSpeed.Value))
                        {
                            _navigated = true;
                        }
                        var distance = Vector3D.Distance(_context._remoteControl.GetPosition(), _context._section.MiningSiteGPS.Value);
                        _context.UpdateStatus("Raising", $"Remaining: {distance:F1} m");
                        return;
                    }

                    if (!_context._alignment.AlignWithYawPitchRoll(0, 0, 0))
                    {
                        _context.UpdateStatus("Raising", "Aligning...");
                        return;
                    }

                    _context._navigation.Stop();
                    _context._alignment.Stop();
                    _context.TransitionTo(new NavigateToBaseState(_context));
                }
            }

            private class NavigateToBaseState : SpiralMinerState
            {
                private bool _commandIssued;

                public NavigateToBaseState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.UpdateStatus("NavigateToBase");
                    _commandIssued = true;
                    if (!_context.SendPathfinderCommand(_context._section.BaseGPS, "Base"))
                    {
                        _commandIssued = false;
                        _context.TransitionTo(new IdleState(_context));
                    }
                }

                public override void OnPathfinderStopped()
                {
                    if (_commandIssued)
                    {
                        _context.TransitionTo(new DockingState(_context));
                    }
                }

                public override void OnPathfinderNoPath()
                {
                    _context.UpdateStatus("NavigateToBase", "Path not found");
                    _context.TransitionTo(new IdleState(_context));
                }
            }

            private class DockingState : SpiralMinerState
            {
                public DockingState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.SetDrills(false, false);
                    _context.UpdateStatus("Docking");
                    if (!_context.RequestDock())
                    {
                        _context.TransitionTo(new IdleState(_context));
                    }
                }

                public override void OnAutoDockDocked()
                {
                    _context.TransitionTo(new WaitForCargoEmptyState(_context));
                }
            }

            private class WaitForCargoEmptyState : SpiralMinerState
            {
                public WaitForCargoEmptyState(SpiralMinerController context) : base(context) { }

                public override void Enter()
                {
                    _context.SetDrills(false, false);
                    _context.UpdateStatus("WaitForCargoEmpty");

                    if (_context._isDocked)
                    {
                        _context.SetBatteryRechargeMode(true);
                    }
                }

                public override void Execute()
                {
                    if (!_context._section.StartOnLoad.Value)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    var ratio = _context.GetCargoFillRatio();
                    var batteryRatio = _context.GetBatteryChargeRatio();
                    _context.UpdateStatus("WaitForCargoEmpty",
                        $"Cargo: {ratio:P0}",
                        $"Batteries: {batteryRatio:P0}");

                    if (!_context.IsCargoEmpty())
                    {
                        return;
                    }

                    if (_context._section.MiningDepthState.Value >= _context._section.Depth.Value - 1e-3)
                    {
                        _context.UpdateStatus("Mining Complete",
                            $"Cargo: {ratio:P0}",
                            $"Total Depth: {_context._section.MiningDepthState.Value:F1} m");
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    if (batteryRatio >= _context._section.BatteryUndockThreshold.Value)
                    {
                        _context.TransitionTo(new UndockingState(_context));
                    }
                }
            }

            private class SpiralMinerSection : Section
            {
                private const double MAX_ORBIT_DEFAULT = 10.0;
                private const double MINING_MOVEMENT_SPEED_DEFAULT = 1.0;
                private const double REPOSITIONING_MOVEMENT_SPEED_DEFAULT = 5.0;
                private const double DEPTH_DEFAULT = 10.0;
                private const double ORBIT_RADIUS_INCREMENT_DEFAULT = 1;
                private const double MAX_PITCH_DEFAULT = 45.0;
                private const double TERRAIN_CLEARING_DEPTH_DEFAULT = 5.0;
                private const double TERRAIN_CLEARING_STEP_DEFAULT = 2.0;
                private const double TERRAIN_CLEARING_SPEED_DEFAULT = 2.0;
                private const double MINING_STEP_DEFAULT = 1;
                private const double CARGO_FULL_THRESHOLD_DEFAULT = 0.95;
                private const double CARGO_EMPTY_THRESHOLD_DEFAULT = 0.05;
                private const string DOCKING_CONNECTOR_NAME_DEFAULT = "*";
                private const double BATTERY_UNDOCK_THRESHOLD_DEFAULT = 0.95;
                private const double BATTERY_RETURN_THRESHOLD_DEFAULT = 0.30;

                public GPSProperty BaseGPS { get; } = new GPSProperty("BaseGPS", Vector3D.Zero, "Base");
                public GPSProperty MiningSiteGPS { get; } = new GPSProperty("MiningSiteGPS", Vector3D.Zero, "MiningSite");
                public DoubleProperty MaxOrbit { get; } = new DoubleProperty("MaxOrbit", MAX_ORBIT_DEFAULT, comment: "Maximum orbit radius in meters");
                public DoubleProperty MiningMovementSpeed { get; } = new DoubleProperty("MiningMovementSpeed", MINING_MOVEMENT_SPEED_DEFAULT);
                public DoubleProperty RepositioningMovementSpeed { get; } = new DoubleProperty("RepositioningMovementSpeed", REPOSITIONING_MOVEMENT_SPEED_DEFAULT);
                public DoubleProperty Depth { get; } = new DoubleProperty("Depth", DEPTH_DEFAULT, comment: "Depth in meters");
                public DoubleProperty OrbitRadiusIncrement { get; } = new DoubleProperty("OrbitRadiusIncrement", ORBIT_RADIUS_INCREMENT_DEFAULT, comment: "Orbit radius increment in meters");
                public DoubleProperty MaxPitch { get; } = new DoubleProperty("MaxPitch", MAX_PITCH_DEFAULT, comment: "Maximum pitch in degrees");
                public DoubleProperty TerrainClearingDepth { get; } = new DoubleProperty("TerrainClearingDepth", TERRAIN_CLEARING_DEPTH_DEFAULT);
                public DoubleProperty TerrainClearingStep { get; } = new DoubleProperty("TerrainClearingStep", TERRAIN_CLEARING_STEP_DEFAULT);
                public DoubleProperty TerrainClearingMovementSpeed { get; } = new DoubleProperty("TerrainClearingMovementSpeed", TERRAIN_CLEARING_SPEED_DEFAULT);
                public DoubleProperty MiningStep { get; } = new DoubleProperty("MiningStep", MINING_STEP_DEFAULT);
                public DoubleProperty CargoFullThreshold { get; } = new DoubleProperty("CargoFullThreshold", CARGO_FULL_THRESHOLD_DEFAULT);
                public DoubleProperty CargoEmptyThreshold { get; } = new DoubleProperty("CargoEmptyThreshold", CARGO_EMPTY_THRESHOLD_DEFAULT);
                public StringProperty DockingConnectorName { get; } = new StringProperty("DockingConnectorName", DOCKING_CONNECTOR_NAME_DEFAULT);
                public DoubleProperty BatteryUndockThreshold { get; } = new DoubleProperty("BatteryUndockThreshold", BATTERY_UNDOCK_THRESHOLD_DEFAULT);
                public DoubleProperty BatteryReturnThreshold { get; } = new DoubleProperty("BatteryReturnThreshold", BATTERY_RETURN_THRESHOLD_DEFAULT);
                public BoolProperty StartOnLoad { get; } = new BoolProperty("StartOnLoad", false);
                public DoubleProperty OrbitRadiusState { get; } = new DoubleProperty("OrbitRadiusState", 0.0, showInCustomData: true);
                public BoolProperty ContractingState { get; } = new BoolProperty("ContractingState", false, showInCustomData: true);
                public DoubleProperty MiningDepthState { get; } = new DoubleProperty("MiningDepthState", 0.0, showInCustomData: true);

                public SpiralMinerSection() : base("SpiralMiner")
                {
                    _properties.Add(BaseGPS);
                    _properties.Add(MiningSiteGPS);
                    _properties.Add(MaxOrbit);
                    _properties.Add(MiningMovementSpeed);
                    _properties.Add(RepositioningMovementSpeed);
                    _properties.Add(Depth);
                    _properties.Add(OrbitRadiusIncrement);
                    _properties.Add(MaxPitch);
                    _properties.Add(TerrainClearingDepth);
                    _properties.Add(TerrainClearingStep);
                    _properties.Add(TerrainClearingMovementSpeed);
                    _properties.Add(MiningStep);
                    _properties.Add(CargoFullThreshold);
                    _properties.Add(CargoEmptyThreshold);
                    _properties.Add(DockingConnectorName);
                    _properties.Add(BatteryUndockThreshold);
                    _properties.Add(BatteryReturnThreshold);
                    _properties.Add(StartOnLoad);
                    _properties.Add(OrbitRadiusState);
                    _properties.Add(ContractingState);
                    _properties.Add(MiningDepthState);
                }
            }
        }
    }
}

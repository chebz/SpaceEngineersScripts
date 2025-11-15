using Sandbox.ModAPI.Ingame;
using System;
using VRageMath;
using System.Collections.Generic;

namespace IngameScript
{
    public class AutoDockDrone : Context
    {
        private class AutoDockDroneSection : Section
        {
            public DoubleProperty ConnectorOffset { get; } = new DoubleProperty("ConnectorOffset", CONNECTOR_OFFSET_DEFAULT);
            public DoubleProperty ApproachSpeed { get; } = new DoubleProperty("ApproachSpeed", APPROACH_SPEED_DEFAULT);
            public DoubleProperty DockingSpeed { get; } = new DoubleProperty("DockingSpeed", DOCKING_SPEED_DEFAULT);
            public DoubleProperty CorrectedApproachDistance { get; } = new DoubleProperty("CorrectedApproachDistance", CORRECTED_APPROACH_DISTANCE_DEFAULT);

            public AutoDockDroneSection() : base("AutoDockDrone")
            {
                _properties.Add(ConnectorOffset);
                _properties.Add(ApproachSpeed);
                _properties.Add(DockingSpeed);
                _properties.Add(CorrectedApproachDistance);
            }
        }

        #region Constants
        private const string BROADCAST_TAG = "AutoDock";
        private const double CONNECTOR_OFFSET_DEFAULT = 1.1;
        private const double APPROACH_SPEED_DEFAULT = 20.0;
        private const double DOCKING_SPEED_DEFAULT = 2.0;
        private const double CORRECTED_APPROACH_DISTANCE_DEFAULT = 10.0;
        #endregion

        #region Fields
        private bool _initialized = false;
        private MyGridProgram _program;
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyShipConnector _connector;
        private IMyRemoteControl _remoteControl;
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        private long _stationSourceId;
        private string _targetStationName = "*";
        private string _requestedConnectorName = "*";
        private AutoDockDroneSection _section;
        private Action _onDocked;
        private Action _onUndocked;
        private List<IMyBatteryBlock> _batteryBlocks = new List<IMyBatteryBlock>();
        #endregion

        #region Methods

        public bool Initialize(Program program, CustomDataConnector customDataConnector, Navigation navigation, Alignment alignment, out string errorMessage)
        {
            _program = program;
            _navigation = navigation;
            _alignment = alignment;
            errorMessage = string.Empty;

            _remoteControl = program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "AutoDockDrone: No remote control found!";
                return false;
            }

            var connectors = program.GetLocalBlocksNameContains<IMyShipConnector>("[AD]");
            if (connectors.Count == 0)
            {
                errorMessage = "No connector with [AD] tag found";
                return false;
            }

            _batteryBlocks = program.GetLocalBlocks<IMyBatteryBlock>();
            if (_batteryBlocks.Count == 0)
            {
                errorMessage = "No battery blocks found";
                return false;
            }

            _connector = connectors[0];

            _section = new AutoDockDroneSection();
            customDataConnector.AddSection(_section);

            _broadcastListener = program.IGC.RegisterBroadcastListener(BROADCAST_TAG);
            _broadcastListener.SetMessageCallback(BROADCAST_TAG);
            _unicastListener = program.IGC.UnicastListener;
            _unicastListener.SetMessageCallback(BROADCAST_TAG);

            if (_connector.IsConnected)
            {
                TransitionTo(new DockedState(this));
            }
            else
            {
                TransitionTo(new UndockedState(this));
            }

            _initialized = true;

            return true;
        }

        public void SetDockedCallback(Action onDocked)
        {
            _onDocked = CombineCallbacks(_onDocked, onDocked);
        }

        public void SetUndockedCallback(Action onUndocked)
        {
            _onUndocked = CombineCallbacks(_onUndocked, onUndocked);
        }

        public override void Execute()
        {
            if (!_initialized)
            {
                _program.Echo("AutoDockDrone not initialized"); 
                return;
            }

            HandleMessages();
            base.Execute();
        }

        private Action CombineCallbacks(Action existing, Action additional)
        {
            if (additional == null)
            {
                return existing;
            }

            if (existing == null)
            {
                return additional;
            }

            return delegate
            {
                existing();
                additional();
            };
        }

        private void InvokeDocked()
        {
            if (_onDocked != null)
            {
                _onDocked();
            }
        }

        private void InvokeUndocked()
        {
            if (_onUndocked != null)
            {
                _onUndocked();
            }
        }

        private void HandleMessages()
        {
            while (_unicastListener.HasPendingMessage)
            {
                var message = _unicastListener.AcceptMessage();
                if (message.Tag == BROADCAST_TAG && message.Data is string)
                {
                    var data = message.Data.ToString();
                    HandleMessage(data, message.Source);
                }
            }
        }

        private void HandleMessage(string message, long source)
        {
            if (message == "statuscheck")
            {
                HandleStatusCheck(source);
            }
            else if (CurrentState != null)
            {
                ((State<AutoDockDrone>)CurrentState).HandleMessage(message, source);
            }
        }

        private void HandleStatusCheck(long source)
        {
            if (CurrentState is DockedState || CurrentState is UndockedState)
            {
            }
            else
            {
                _program.IGC.SendUnicastMessage(source, BROADCAST_TAG, "onmyway");
            }
        }

        private string NormalizeConnectorName(string connectorName)
        {
            if (string.IsNullOrWhiteSpace(connectorName))
            {
                return "*";
            }

            return connectorName;
        }

        private string NormalizeStationName(string stationName)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                return "*";
            }

            return stationName.Trim();
        }

        public void DockToNearest(string connectorName)
        {
            if (!_initialized)
            {
                _program.Echo("Cannot dock - not initialized");
                return;
            }

            if (!(CurrentState is UndockedState))
            {
                _program.Echo($"Cannot dock - in state {CurrentState.GetType().Name}");
                return;
            }

            _requestedConnectorName = NormalizeConnectorName(connectorName);
            _targetStationName = "*";
            TransitionTo(new RequestingApproachState(this, "*"));
        }

        public void DockToStation(string stationName, string connectorName)
        {
            if (!_initialized)
            {
                return;
            }

            var normalizedStationName = NormalizeStationName(stationName);
            if (CurrentState is UndockedState)
            {
                _requestedConnectorName = NormalizeConnectorName(connectorName);
                _targetStationName = normalizedStationName;
                TransitionTo(new RequestingApproachState(this, normalizedStationName));
            }
            else if (CurrentState is DockedState)
            {
                _requestedConnectorName = NormalizeConnectorName(connectorName);
                _targetStationName = normalizedStationName;
                TransitionTo(new UndockingState(this, normalizedStationName));
            }
            else
            {
                _program.Echo("Cannot dock - operation in progress");
            }
        }

        public void Stop()
        {
            if (!_initialized)
            {
                return;
            }

            _navigation.Stop();
            _alignment.Stop();

            if (_connector.IsConnected)
            {
                TransitionTo(new DockedState(this));
            }
            else
            {
                TransitionTo(new UndockedState(this));
            }
        }

        public void Undock()
        {
            if (!_initialized)
            {
                return;
            }

            if (!(CurrentState is DockedState))
            {
                _program.Echo("Cannot undock - not docked");
                return;
            }

            if (!_connector.IsConnected)
            {
                _program.Echo("Cannot undock - not connected");
                return;
            }

            TransitionTo(new UndockingState(this));
        }

        public bool IsDocked()
        {
            return CurrentState is DockedState;
        }

        public bool IsUndocked()
        {
            return CurrentState is UndockedState;
        }

        private void SetBatteryRechargeMode(bool recharge)
        {
            foreach (var battery in _batteryBlocks)
            {
                if (battery == null)
                {
                    continue;
                }

                battery.ChargeMode = recharge ? ChargeMode.Recharge : ChargeMode.Auto;
            }
        }
        #endregion

        #region Types

        private class UndockedState : State<AutoDockDrone>
        {
            private readonly string _nextStationName;

            public UndockedState(AutoDockDrone context, string nextStationName = null) : base(context)
            {
                _nextStationName = nextStationName;
            }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();
                _context.InvokeUndocked();

                if (!string.IsNullOrEmpty(_nextStationName) && _nextStationName != "*")
                {
                    _context._program.Echo($"Transitioning to dock to station {_nextStationName}");
                    _context._targetStationName = _nextStationName;
                    _context.TransitionTo(new RequestingApproachState(_context, _nextStationName));
                }
            }

            public override void Execute()
            {
            }
        }

        private class DockedState : State<AutoDockDrone>
        {
            public DockedState(AutoDockDrone context) : base(context) { }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();
                _context._navigation.PowerOff();
                _context.SetBatteryRechargeMode(true);
                _context.InvokeDocked();
            }

            public override void Execute()
            {
            }
        }

        private class RequestingApproachState : State<AutoDockDrone>
        {
            private readonly string _targetStationName;

            public RequestingApproachState(AutoDockDrone context, string targetStationName) : base(context)
            {
                _targetStationName = targetStationName;
            }

            public override void Enter()
            {
                _context._program.Echo("Requesting approach");
                var droneName = _context._program.Me.CubeGrid.CustomName;
                _context._stationSourceId = 0;
                var message = $"requestdocking|{droneName}|{_context._requestedConnectorName}|{_targetStationName}";
                _context._program.IGC.SendBroadcastMessage(BROADCAST_TAG, message);
            }

            public override void Execute()
            {
            }

            public override void HandleMessage(string message, long source)
            {
                if (message.StartsWith("approachpos|"))
                {
                    var parts = message.Split('|');
                    if (parts.Length >= 4)
                    {
                        var x = double.Parse(parts[1]);
                        var y = double.Parse(parts[2]);
                        var z = double.Parse(parts[3]);
                        var approachPos = new Vector3D(x, y, z);
                        var vx = double.Parse(parts[4]);
                        var vy = double.Parse(parts[5]);
                        var vz = double.Parse(parts[6]);
                        var stationVelocity = new Vector3D(vx, vy, vz);

                        _context._stationSourceId = source;
                        _context.TransitionTo(new NavigatingToApproachState(_context, approachPos, stationVelocity, source));
                    }
                }
                else if (message == "nodocksavailable")
                {
                    _context._program.Echo("No docks available");
                    _context.TransitionTo(new UndockedState(_context));
                }
            }
        }

        private class NavigatingToApproachState : State<AutoDockDrone>
        {
            private Vector3D _approachPosition;
            private Vector3D _stationVelocity;

            public NavigatingToApproachState(AutoDockDrone context, Vector3D approachPosition, Vector3D stationVelocity, long stationSourceId) : base(context)
            {
                _approachPosition = approachPosition;
                _stationVelocity = stationVelocity;
                _context._stationSourceId = stationSourceId;
            }

            public override void Enter()
            {
                _context._program.Echo("Navigating to approach position");
            }

            public override void Execute()
            {
                if (!_context._navigation.NavigateTo(_approachPosition, _context._section.ApproachSpeed.Value, 1, _stationVelocity))
                {
                    return;
                }

                _context._program.Echo("Reached approach position");
                _context.TransitionTo(new RequestingDockCoordState(_context));
            }

            public override void HandleMessage(string message, long source)
            {
                if (message.StartsWith("approachupdate|"))
                {
                    var parts = message.Split('|');
                    if (parts.Length >= 7)
                    {
                        var x = double.Parse(parts[1]);
                        var y = double.Parse(parts[2]);
                        var z = double.Parse(parts[3]);
                        _approachPosition = new Vector3D(x, y, z);
                    }
                }
            }
        }

        private class RequestingDockCoordState : State<AutoDockDrone>
        {
            public RequestingDockCoordState(AutoDockDrone context) : base(context) { }

            public override void Enter()
            {
                _context._program.Echo("Requesting dock coordinate");
                if (_context._stationSourceId != 0)
                {
                    _context._program.IGC.SendUnicastMessage(_context._stationSourceId, BROADCAST_TAG, "requestdockcoord");
                }
            }

            public override void Execute()
            {
            }

            public override void HandleMessage(string message, long source)
            {
                if (message.StartsWith("dockcoord|"))
                {
                    var parts = message.Split('|');
                    if (parts.Length >= 17)
                    {
                        var matrix = new MatrixD(
                            double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3]), double.Parse(parts[4]),
                            double.Parse(parts[5]), double.Parse(parts[6]), double.Parse(parts[7]), double.Parse(parts[8]),
                            double.Parse(parts[9]), double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]),
                            double.Parse(parts[13]), double.Parse(parts[14]), double.Parse(parts[15]), double.Parse(parts[16])
                        );

                        Vector3D stationVelocity = Vector3D.Zero;
                        if (parts.Length >= 20)
                        {
                            var vx = double.Parse(parts[17]);
                            var vy = double.Parse(parts[18]);
                            var vz = double.Parse(parts[19]);
                            stationVelocity = new Vector3D(vx, vy, vz);
                        }

                        _context.TransitionTo(new DockingState(_context, matrix, stationVelocity));
                    }
                }
                else if (message == "denied")
                {
                    _context._program.Echo("Docking denied, re-requesting approach...");
                    _context.TransitionTo(new RequestingApproachState(_context, _context._targetStationName));
                }
            }
        }

        private class DockingState : State<AutoDockDrone>
        {
            private Vector3D _correctedApproachPosition;
            private MatrixD _stationConnectorMatrix;
            private Vector3D _stationVelocity;
            private Vector3D _localOffset;
            private QuaternionD _connectorToRemoteRotation;
            private bool _correctedApproach;
            private bool _alignedWithDock;

            public DockingState(AutoDockDrone context, MatrixD stationConnectorMatrix, Vector3D stationVelocity) : base(context)
            {
                _stationConnectorMatrix = stationConnectorMatrix;
                _stationVelocity = stationVelocity;
                
                var remotePos = context._remoteControl.GetPosition();
                var connectorPos = context._connector.GetPosition();
                var remoteMatrix = context._remoteControl.WorldMatrix;
                var connectorMatrix = context._connector.WorldMatrix;
                
                var connectorQuat = QuaternionD.CreateFromRotationMatrix(connectorMatrix);
                var remoteQuat = QuaternionD.CreateFromRotationMatrix(remoteMatrix);
                
                _connectorToRemoteRotation = QuaternionD.Inverse(connectorQuat) * remoteQuat;
                
                var worldOffset = remotePos - connectorPos;
                _localOffset = Vector3D.TransformNormal(worldOffset, MatrixD.Transpose(connectorMatrix));
                
                var targetMatrix = CalculateTargetMatrix();
                _correctedApproachPosition = targetMatrix.Translation + (stationConnectorMatrix.Forward * context._section.CorrectedApproachDistance.Value);
            }
            public override void Enter()
            {
                _context._program.Echo("Docking State");
            }

            public override void Execute()
            {
                if (_context._connector.Status == MyShipConnectorStatus.Connectable)
                {
                    _context._connector.Connect();
                    _context.TransitionTo(new DockedState(_context));
                    return;
                }

                var targetMatrix = CalculateTargetMatrix();
                

                if (!_alignedWithDock)
                {
                    if (!_context._alignment.AlignWithWorldMatrix(targetMatrix))
                    {
                        return;
                    }
                    _alignedWithDock = true;
                }
                if (!_correctedApproach)
                {
                    if (!_context._navigation.NavigateTo(_correctedApproachPosition, _context._section.DockingSpeed.Value, 0.1, _stationVelocity))
                    {
                        return;
                    }
                    _correctedApproach = true;
                }
                _context._navigation.NavigateTo(targetMatrix.Translation, _context._section.DockingSpeed.Value, 0.1, _stationVelocity);
            }

            public override void HandleMessage(string message, long source)
            {
                if (message.StartsWith("dockingupdate|"))
                {
                    var parts = message.Split('|');
                    if (parts.Length >= 20)
                    {
                        var stationConnectorMatrix = new MatrixD(
                            double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3]), double.Parse(parts[4]),
                            double.Parse(parts[5]), double.Parse(parts[6]), double.Parse(parts[7]), double.Parse(parts[8]),
                            double.Parse(parts[9]), double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]),
                            double.Parse(parts[13]), double.Parse(parts[14]), double.Parse(parts[15]), double.Parse(parts[16])
                        );

                        var vx = double.Parse(parts[17]);
                        var vy = double.Parse(parts[18]);
                        var vz = double.Parse(parts[19]);
                        _stationVelocity = new Vector3D(vx, vy, vz);
                        _stationConnectorMatrix = stationConnectorMatrix;
                    }
                }
            }

            private MatrixD CalculateTargetMatrix()
            {
                var targetShipConnectorMatrix = MatrixD.CreateWorld(
                    _stationConnectorMatrix.Translation,
                    -_stationConnectorMatrix.Forward,
                    _stationConnectorMatrix.Up
                );
                
                var targetShipConnectorQuat = QuaternionD.CreateFromRotationMatrix(targetShipConnectorMatrix);
                var targetRemoteQuat = targetShipConnectorQuat * _connectorToRemoteRotation;
                
                var targetRemoteMatrix = MatrixD.CreateFromQuaternion(targetRemoteQuat);
                
                var offsetInWorldSpace = Vector3D.TransformNormal(_localOffset, targetShipConnectorMatrix);
                targetRemoteMatrix.Translation = _stationConnectorMatrix.Translation + offsetInWorldSpace + (_stationConnectorMatrix.Forward * _context._section.ConnectorOffset.Value);
                
                return targetRemoteMatrix;
            }

        }

        private class UndockingState : State<AutoDockDrone>
        {
            private const double UNDOCK_DISTANCE = 10.0;
            private Vector3D _undockPosition;
            private readonly string _nextStationName;

            public UndockingState(AutoDockDrone context, string nextStationName = null) : base(context)
            {
                _nextStationName = nextStationName;
            }

            public override void Enter()
            {
                _context._program.Echo("Undocking State");
                if (!_context._connector.IsConnected)
                {
                    _context._program.Echo("Cannot undock - not connected");
                    _context.TransitionTo(new UndockedState(_context));
                    return;
                }
                _context.SetBatteryRechargeMode(false);
                var connectorForward = _context._connector.WorldMatrix.Forward;
                var currentPos = _context._connector.GetPosition();
                _undockPosition = currentPos - (connectorForward * UNDOCK_DISTANCE);
                _context._connector.Disconnect();
                _context._navigation.PowerOn();
            }

            public override void Execute()
            {
                if (_context._navigation.NavigateTo(_undockPosition, 10.0, 1.0))
                {
                    _context.TransitionTo(new UndockedState(_context, _nextStationName));
                }
            }
        }
        #endregion
    }
}


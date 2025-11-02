using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VRageMath;

namespace IngameScript
{
    public class AutoDockDrone : Context
    {
        #region Constants
        private const string BROADCAST_TAG = "AutoDock";
        private const double CONNECTOR_OFFSET = 1.1;
        #endregion

        #region Fields
        private bool _initialized = false;
        private MyGridProgram _program;
        private Navigation _navigation;
        private Alignment _alignment;
        private IMyShipConnector _connector;
        private IMyRemoteControl _remoteControl;
        private PathfindingNavigation _pathfinderNavigation;
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        private long _stationPbId;
        #endregion

        #region Methods

        public bool Initialize(Program program, Navigation navigation, Alignment alignment, PathfindingNavigation pathfinderNavigation, out string errorMessage)
        {
            _program = program;
            _navigation = navigation;
            _alignment = alignment;
            _pathfinderNavigation = pathfinderNavigation;
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

            _connector = connectors[0];

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

        public override void Execute()
        {
            if (!_initialized)
            {
                _program.Echo("Error: AutoDockDrone not initialized");
                return;
            }

            HandleMessages();
            _pathfinderNavigation.Execute();
            base.Execute();
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

        public void DockToNearest()
        {
            if (!_initialized)
            {
                _program.Echo("Error: AutoDockDrone not initialized");
                return;
            }

            if (!(CurrentState is UndockedState))
            {
                return;
            }

            TransitionTo(new RequestingApproachState(this, 0, true));
        }

        public void DockToStation(long stationPbId)
        {
            if (!_initialized)
            {
                _program.Echo("Error: AutoDockDrone not initialized");
                return;
            }

            if (CurrentState is UndockedState)
            {
                TransitionTo(new RequestingApproachState(this, stationPbId, false));
            }
            else if (CurrentState is DockedState)
            {
                TransitionTo(new UndockingState(this, stationPbId));
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
                _program.Echo("Error: AutoDockDrone not initialized");
                return;
            }

            _pathfinderNavigation.Stop();

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
                _program.Echo("Error: AutoDockDrone not initialized");
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
        #endregion

        #region Types

        private class UndockedState : State<AutoDockDrone>
        {
            private long _nextStationPbId;

            public UndockedState(AutoDockDrone context, long nextStationPbId = 0) : base(context)
            {
                _nextStationPbId = nextStationPbId;
            }

            public override void Enter()
            {
                _context._navigation.Stop();
                _context._alignment.Stop();

                if (_nextStationPbId != 0)
                {
                    _context._program.Echo($"Transitioning to dock to station {_nextStationPbId}");
                    _context.TransitionTo(new RequestingApproachState(_context, _nextStationPbId, false));
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
                _context._pathfinderNavigation.Stop();
                _context._navigation.Stop();
                _context._alignment.Stop();
                _context._navigation.PowerOff();
            }

            public override void Execute()
            {
            }
        }

        private class RequestingApproachState : State<AutoDockDrone>
        {
            private long _stationPbId;
            private bool _isBroadcast;

            public RequestingApproachState(AutoDockDrone context, long stationPbId, bool isBroadcast) : base(context)
            {
                _stationPbId = stationPbId;
                _isBroadcast = isBroadcast;
            }

            public override void Enter()
            {
                _context._program.Echo("Requesting approach");
                var droneName = _context._program.Me.CubeGrid.CustomName;
                var message = $"requestdocking|{droneName}";
                
                if (_isBroadcast)
                {
                    _context._program.IGC.SendBroadcastMessage(BROADCAST_TAG, message);
                }
                else
                {
                    _context._program.IGC.SendUnicastMessage(_stationPbId, BROADCAST_TAG, message);
                }
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

                        Vector3D stationVelocity = Vector3D.Zero;
                        if (parts.Length >= 7)
                        {
                            var vx = double.Parse(parts[4]);
                            var vy = double.Parse(parts[5]);
                            var vz = double.Parse(parts[6]);
                            stationVelocity = new Vector3D(vx, vy, vz);
                        }

                        _context.TransitionTo(new NavigatingToApproachState(_context, approachPos, source, stationVelocity));
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

            public NavigatingToApproachState(AutoDockDrone context, Vector3D approachPosition, long stationPbId, Vector3D stationVelocity) : base(context)
            {
                _approachPosition = approachPosition;
                _context._stationPbId = stationPbId;
                _stationVelocity = stationVelocity;
            }

            public override void Enter()
            {
                _context._program.Echo("Navigating to approach position");
                _context._pathfinderNavigation.NavigateTo(_approachPosition, 20.0, 1.0, _stationVelocity);
            }

            public override void Execute()
            {
                var navigationState = _context._pathfinderNavigation.GetNavigationState();
                
                if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Stuck)
                {
                    _context._program.Echo("Navigation stuck - aborting docking");
                    _context.TransitionTo(new UndockedState(_context));
                }
                else if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Idle)
                {
                    _context._program.Echo("Reached approach position");
                    _context.TransitionTo(new RequestingDockCoordState(_context));
                }
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

                        var vx = double.Parse(parts[4]);
                        var vy = double.Parse(parts[5]);
                        var vz = double.Parse(parts[6]);
                        _stationVelocity = new Vector3D(vx, vy, vz);

                        _context._navigationWithCollisionAvoidance.NavigateTo(_approachPosition, 20.0, 1.0, _stationVelocity);
                    }
                }
            }
        }

        private class RequestingDockCoordState : State<AutoDockDrone>
        {
            public RequestingDockCoordState(AutoDockDrone context) : base(context)
            {
            }

            public override void Enter()
            {
                _context._program.Echo("Requesting dock coordinate");
                _context._program.IGC.SendUnicastMessage(_context._stationPbId, BROADCAST_TAG, "requestdockcoord");
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
                    _context.TransitionTo(new RequestingApproachState(_context, _context._stationPbId, false));
                }
            }
        }

        private class DockingState : State<AutoDockDrone>
        {
            private const double APPROACH_CONE_ANGLE_DEG = 45.0;
            private const double ALIGNMENT_WAIT_THRESHOLD_DEG = 30.0;
            private MatrixD _stationConnectorMatrix;
            private Vector3D _stationVelocity;
            private Vector3D _localOffset;
            private QuaternionD _connectorToRemoteRotation;
            private bool _aligning;


            public DockingState(AutoDockDrone context, MatrixD stationConnectorMatrix, Vector3D stationVelocity) : base(context)
            {
                _stationConnectorMatrix = stationConnectorMatrix;
                _stationVelocity = stationVelocity;
                _aligning = false;
                
                var remotePos = context._remoteControl.GetPosition();
                var connectorPos = context._connector.GetPosition();
                var remoteMatrix = context._remoteControl.WorldMatrix;
                var connectorMatrix = context._connector.WorldMatrix;
                
                var connectorQuat = QuaternionD.CreateFromRotationMatrix(connectorMatrix);
                var remoteQuat = QuaternionD.CreateFromRotationMatrix(remoteMatrix);
                
                _connectorToRemoteRotation = QuaternionD.Inverse(connectorQuat) * remoteQuat;
                
                var worldOffset = remotePos - connectorPos;
                _localOffset = Vector3D.TransformNormal(worldOffset, MatrixD.Transpose(connectorMatrix));
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
                targetRemoteMatrix.Translation = _stationConnectorMatrix.Translation + offsetInWorldSpace + (_stationConnectorMatrix.Forward * CONNECTOR_OFFSET);
                
                return targetRemoteMatrix;
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

                // Check if we're still within the approach cone
                var currentPos = _context._remoteControl.GetPosition();
                var connectorPos = _stationConnectorMatrix.Translation;
                
                // Ideal approach vector points toward the connector (negative of connector's forward)
                var idealApproachVector = -_stationConnectorMatrix.Forward;
                
                // Actual vector: from current position to connector
                var actualVector = Vector3D.Normalize(connectorPos - currentPos);
                
                // Calculate angle between vectors
                var dotProduct = Vector3D.Dot(idealApproachVector, actualVector);
                var angleDeg = Math.Acos(MathHelper.Clamp(dotProduct, -1.0, 1.0)) * (180.0 / Math.PI);
                
                if (angleDeg > APPROACH_CONE_ANGLE_DEG)
                {
                    _context._program.Echo($"Outside approach cone ({angleDeg:F1}°), re-requesting approach");
                    _context.TransitionTo(new RequestingApproachState(_context, _context._stationPbId, false));
                    return;
                }

                var targetMatrix = CalculateTargetMatrix();
                
                if (!_aligning)
                {
                    // Check alignment delta to decide if we should pause for alignment
                    var currentMatrix = _context._remoteControl.WorldMatrix;
                    var currentQuat = QuaternionD.CreateFromRotationMatrix(currentMatrix);
                    var targetQuat = QuaternionD.CreateFromRotationMatrix(targetMatrix);
                    
                    // Calculate rotation difference
                    var deltaQuat = QuaternionD.Inverse(currentQuat) * targetQuat;
                    double angle;
                    Vector3D axis;
                    deltaQuat.GetAxisAngle(out axis, out angle);
                    var alignmentAngleDeg = Math.Abs(angle) * (180.0 / Math.PI);
                    
                    // Manage alignment state
                    if (alignmentAngleDeg > ALIGNMENT_WAIT_THRESHOLD_DEG)
                    {
                        _aligning = true;
                        _context._navigation.Stop();
                    }
                }
                else if (_context._alignment.AlignWithWorldMatrix(targetMatrix))
                {
                    _aligning = false;
                }

                _context._alignment.AlignWithWorldMatrix(targetMatrix);
                _context._navigation.NavigateTo(targetMatrix.Translation, 2.0, 0.1, _stationVelocity);
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
        }

        private class UndockingState : State<AutoDockDrone>
        {
            private const double UNDOCK_DISTANCE = 10.0;
            private Vector3D _undockPosition;
            private long _nextStationPbId;

            public UndockingState(AutoDockDrone context, long nextStationPbId = 0) : base(context)
            {
                _nextStationPbId = nextStationPbId;
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
                    _context.TransitionTo(new UndockedState(_context, _nextStationPbId));
                }
            }
        }
        #endregion
    }
}


using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
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
        private NavigationWithCollisionAvoidance _navigationWithCollisionAvoidance;
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        #endregion

        #region Methods

        public bool Initialize(Program program, Navigation navigation, Alignment alignment, NavigationWithCollisionAvoidance navigationWithCollisionAvoidance, out string errorMessage)
        {
            _program = program;
            _navigation = navigation;
            _alignment = alignment;
            _navigationWithCollisionAvoidance = navigationWithCollisionAvoidance;
            errorMessage = string.Empty;

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
            _navigationWithCollisionAvoidance.Execute();
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

            _navigationWithCollisionAvoidance.Stop();

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
                _context._navigation.Stop();
                _context._alignment.Stop();
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

                        _context.TransitionTo(new NavigatingToApproachState(_context, approachPos, source));
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
            private long _stationPbId;

            public NavigatingToApproachState(AutoDockDrone context, Vector3D approachPosition, long stationPbId) : base(context)
            {
                _approachPosition = approachPosition;
                _stationPbId = stationPbId;
            }

            public override void Enter()
            {
                _context._program.Echo("Navigating to approach position");
                _context._navigationWithCollisionAvoidance.NavigateTo(_approachPosition, 20.0, 1.0);
            }

            public override void Execute()
            {
                var navigationState = _context._navigationWithCollisionAvoidance.GetNavigationState();
                
                if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Stuck)
                {
                    _context._program.Echo("Navigation stuck - aborting docking");
                    _context.TransitionTo(new UndockedState(_context));
                }
                else if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Idle)
                {
                    _context._program.Echo("Reached approach position");
                    _context.TransitionTo(new RequestingDockCoordState(_context, _stationPbId));
                }
            }
        }

        private class RequestingDockCoordState : State<AutoDockDrone>
        {
            private long _stationId;

            public RequestingDockCoordState(AutoDockDrone context, long stationId) : base(context)
            {
                _stationId = stationId;
            }

            public override void Enter()
            {
                _context._program.IGC.SendUnicastMessage(_stationId, BROADCAST_TAG, "requestdockcoord");
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

                        _context.TransitionTo(new DockingState(_context, matrix));
                    }
                }
                else if (message == "denied")
                {
                    _context._program.Echo("Docking denied, re-requesting approach...");
                    _context.TransitionTo(new RequestingApproachState(_context, _stationId, false));
                }
            }
        }

        private class DockingState : State<AutoDockDrone>
        {
            private MatrixD _targetMatrix;
            private MatrixD _shipTargetMatrix;
            private Vector3D _dockingPosition;
            private bool _aligned;
            private bool _navigated;
            private IMyRemoteControl _remoteControl;

            public DockingState(AutoDockDrone context, MatrixD stationConnectorMatrix) : base(context)
            {
                _aligned = false;
                _navigated = false;
                
                _remoteControl = context._program.GridTerminalSystem.GetBlockWithName("Remote Control") as IMyRemoteControl;
                if (_remoteControl == null)
                {
                    var remotes = new List<IMyRemoteControl>();
                    context._program.GridTerminalSystem.GetBlocksOfType(remotes, r => r.CubeGrid == context._program.Me.CubeGrid);
                    if (remotes.Count > 0)
                    {
                        _remoteControl = remotes[0];
                    }
                }
                
                _dockingPosition = stationConnectorMatrix.Translation + (stationConnectorMatrix.Forward * CONNECTOR_OFFSET);
                
                var connectorPosition = stationConnectorMatrix.Translation;
                var connectorRight = stationConnectorMatrix.Right;
                
                var rotationMatrix1 = MatrixD.CreateFromAxisAngle(connectorRight, -Math.PI / 2);
                
                var rotatedForward = Vector3D.TransformNormal(stationConnectorMatrix.Forward, rotationMatrix1);
                var rotatedUp = Vector3D.TransformNormal(stationConnectorMatrix.Up, rotationMatrix1);
                
                var rotationMatrix2 = MatrixD.CreateFromAxisAngle(rotatedUp, Math.PI / 2);
                
                var finalForward = Vector3D.TransformNormal(rotatedForward, rotationMatrix2);
                var finalUp = Vector3D.TransformNormal(rotatedUp, rotationMatrix2);
                
                _targetMatrix = MatrixD.CreateWorld(connectorPosition, finalForward, finalUp);
                _shipTargetMatrix = _targetMatrix;
            }

            public override void Enter()
            {
            }

            public override void Execute()
            {
                if (!_aligned)
                {
                    if (_context._alignment.AlignWithWorldMatrix(_shipTargetMatrix))
                    {
                        _aligned = true;
                        _context._program.Echo("Aligned with connector");
                        
                        var remoteControlPos = _remoteControl.GetPosition();
                        var shipConnectorPos = _context._connector.GetPosition();
                        var offsetVector = shipConnectorPos - remoteControlPos;
                        
                        _dockingPosition = _dockingPosition - offsetVector;
                        _context._program.Echo($"Adjusted docking position for connector offset: {_dockingPosition}");
                    }
                }
                else if (!_navigated)
                {
                    if (_context._navigation.NavigateTo(_dockingPosition, 2.0, 0.1))
                    {
                        _navigated = true;
                        _context._program.Echo("Reached docking position");
                    }
                    else if (_context._connector.Status == MyShipConnectorStatus.Connectable)
                    {
                        _context._navigation.Stop();
                        _navigated = true;
                    }
                }
                else
                {
                    _context._connector.Connect();
                    
                    if (_context._connector.IsConnected)
                    {
                        _context._program.Echo("Successfully docked!");
                        _context.TransitionTo(new DockedState(_context));
                    }
                }
            }
        }

        private class UndockingState : State<AutoDockDrone>
        {
            private const double UNDOCK_DISTANCE = 10.0;
            private Vector3D _undockPosition;
            private bool _disconnected;
            private long _nextStationPbId;

            public UndockingState(AutoDockDrone context, long nextStationPbId = 0) : base(context)
            {
                _disconnected = false;
                _nextStationPbId = nextStationPbId;
            }

            public override void Enter()
            {
                var connector = _context._connector;
                if (connector.IsConnected)
                {
                    var connectorForward = connector.WorldMatrix.Forward;
                    var currentPos = connector.GetPosition();
                    _undockPosition = currentPos - (connectorForward * UNDOCK_DISTANCE);
                    _context._program.Echo("Starting undocking sequence");
                }
            }

            public override void Execute()
            {
                var connector = _context._connector;
                
                if (!_disconnected)
                {
                    if (connector.IsConnected)
                    {
                        connector.Disconnect();
                    }
                    else
                    {
                        _disconnected = true;
                    }
                }
                else
                {
                    if (_context._navigation.NavigateTo(_undockPosition, 10.0, 1.0))
                    {
                        _context._program.Echo("Undocking complete");
                        _context.TransitionTo(new UndockedState(_context, _nextStationPbId));
                    }
                }
            }
        }
        #endregion
    }
}


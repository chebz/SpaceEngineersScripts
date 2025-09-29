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
using VRage.Game.ModAPI;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region Constants
        private const string BROADCAST_TAG = "DroneControl";
        private const string UNDOCKING_PATH_NAME = "UndockingPath";
        private const double DOCKING_OFFSET = 15.0; // meters above target along gravity
        private const double CONNECTOR_OFFSET = 1.1; // meters from connector for docking
        #endregion

        #region Fields
        private Navigation _navigation;
        private Alignment _alignment;
        private NavigationWithCollisionAvoidance _navigationWithCollisionAvoidance;
        private List<IMyLightingBlock> _lights;
        private IMyRemoteControl _remoteControl;
        private IMyShipConnector _connector;
        private List<IMyCargoContainer> _cargoContainers;
        private IceLifterContext _context;
        private long _droneControlTowerEntityId;
        private long _tractorEntityId;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            // Initialize systems
            _navigation = new Navigation();
            _alignment = new Alignment();
            _navigationWithCollisionAvoidance = new NavigationWithCollisionAvoidance();
            _lights = this.GetLocalBlocks<IMyLightingBlock>();
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            _connector = this.GetLocalBlock<IMyShipConnector>("Docking Connector");
            _cargoContainers = this.GetLocalBlocks<IMyCargoContainer>();
            _droneControlTowerEntityId = 0; // Will be set when we receive first dispatch message
            _tractorEntityId = 0; // Will be set when we receive dispatch message
            _context = new IceLifterContext(this);

            // Initialize systems
            string errorMessage;
            if (!_navigation.Initialize(this, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_alignment.Initialize(this, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            if (!_navigationWithCollisionAvoidance.Initialize(this, _alignment, _navigation, out errorMessage))
            {
                Echo($"NavigationWithCollisionAvoidance Error: {errorMessage}");
            }

            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
            }

            if (_connector == null)
            {
                Echo("Error: No 'Docking Connector' found!");
            }

            Echo($"Found {_lights.Count} lights");

        }

        public void Main(string argument, UpdateType updateSource)
        {
            // Handle IGC messages
            if ((updateSource & UpdateType.IGC) > 0)
            {
                _context.HandleIGCMessages();
            }
            else if (!string.IsNullOrEmpty(argument))
            {
                // Only handle as command if it's not an IGC update
                HandleCommand(argument);
            }

            _context.Execute();
            _navigationWithCollisionAvoidance.Execute();
        }

        private void HandleCommand(string argument)
        {
            var args = argument.Split(' ');
            var command = args[0].ToLower();

            switch (command)
            {
                case "dock":
                    _context.Dock();
                    break;

                case "testalign":
                    _context.TestAlign();
                    break;

                case "testnav":
                    _context.TestNav();
                    break;

                case "stop":
                    _context.Stop();
                    break;

                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        public void SetLightSettings(Color color, bool isBlinking = false, double blinkOnDelay = 0.5, double blinkOffDelay = 0.5)
        {
            foreach (var light in _lights)
            {
                light.Color = color;
                if (isBlinking)
                {
                    light.BlinkIntervalSeconds = (float)(blinkOnDelay + blinkOffDelay);
                    light.BlinkLength = (float)(blinkOnDelay / (blinkOnDelay + blinkOffDelay));
                }
                else
                {
                    light.BlinkIntervalSeconds = 0;
                    light.BlinkLength = 0;
                }
                light.Enabled = true;
            }
        }

        public bool AreCargoContainersFull()
        {
            if (_cargoContainers.Count == 0) return false;

            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                if (inventory != null)
                {
                    var maxVolume = (double)inventory.MaxVolume;
                    var currentVolume = (double)inventory.CurrentVolume;
                    if (currentVolume < maxVolume * 0.95) // 95% full threshold
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool AreCargoContainersEmpty()
        {
            if (_cargoContainers.Count == 0) return true;

            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                if (inventory != null)
                {
                    var currentVolume = (double)inventory.CurrentVolume;
                    if (currentVolume > 0.1) // Not empty if more than 0.1L
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public class IceLifterContext : Context
        {
            public Program program;
            public Vector3D targetPosition;
            public long targetEntityId;
            private IMyUnicastListener _unicastListener;
            public bool _waitingForDctId = false;
            public bool _waitingForTestAlign = false;

            public IceLifterContext(Program program)
            {
                this.program = program;
                _unicastListener = program.IGC.UnicastListener;
                _unicastListener.SetMessageCallback(Program.BROADCAST_TAG);
                TransitionTo(new IdleState(this));
            }

            public void HandleIGCMessages()
            {
                if (_unicastListener != null && _unicastListener.HasPendingMessage)
                {
                    while (_unicastListener.HasPendingMessage)
                    {
                        var igcMessage = _unicastListener.AcceptMessage();
                        program.Echo($"IGC Message received - Tag: {igcMessage.Tag}, Data: {igcMessage.Data}, Source: {igcMessage.Source}");
                        
                        if (igcMessage.Tag == Program.BROADCAST_TAG && igcMessage.Data is string)
                        {
                            var message = igcMessage.Data.ToString();
                            program.Echo($"Received: {message}");
                            
                            if (message.StartsWith("dispatch|"))
                            {
                                HandleDispatchMessage(message, igcMessage.Source);
                            }
                            else if (message == "status check")
                            {
                                HandleStatusCheck(igcMessage.Source);
                            }
                            else if (message.StartsWith("dockcoord|"))
                            {
                                HandleDockingCoordinateResponse(message);
                            }
                            else if (message.StartsWith("cargostatus|"))
                            {
                                HandleCargoStatusResponse(message);
                            }
                            else if (message.StartsWith("approachpos|"))
                            {
                                HandleApproachPositionResponse(message);
                            }
                            else if (message.StartsWith("dctid|"))
                            {
                                HandleDctIdResponse(message);
                            }
                        }
                        else
                        {
                            program.Echo($"Message not for us - Tag: {igcMessage.Tag}, Expected: {Program.BROADCAST_TAG}");
                        }
                    }
                }
            }


            public void HandleDispatchMessage(string message, long senderEntityId)
            {
                program.Echo($"HandleDispatchMessage called: {message} from {senderEntityId}");
                
                // Store the DroneControlTower EntityId for future responses
                program._droneControlTowerEntityId = senderEntityId;
                
                // Parse message: "dispatch|EntityId"
                var parts = message.Split('|');
                if (parts.Length >= 2)
                {
                    var entityId = long.Parse(parts[1]);
                    
                    program.Echo($"Received dispatch for entity {entityId}");
                    program.Echo($"Current state: {CurrentState.GetType().Name}");
                    
                    // Only process dispatch if idle, otherwise respond with busy
                    if (CurrentState is IdleState)
                    {
                        targetEntityId = entityId;
                        program._tractorEntityId = entityId; // Cache tractor ID
                        
                        TransitionTo(new UndockingState(this, true));
                    }
                    else
                    {
                        // Send busy response directly to DroneControlTower via unicast
                        program.IGC.SendUnicastMessage(senderEntityId, Program.BROADCAST_TAG, "busy");
                    }
                }
                else
                {
                    program.Echo($"Invalid dispatch message format: {message}");
                }
            }

            public void HandleStatusCheck(long senderEntityId)
            {
                program.Echo($"Status check from {senderEntityId}, current state: {CurrentState.GetType().Name}");
                
                // Store the DroneControlTower EntityId for future responses
                program._droneControlTowerEntityId = senderEntityId;
                
                // Respond with availability status
                if (CurrentState is IdleState)
                {
                    program.IGC.SendUnicastMessage(senderEntityId, Program.BROADCAST_TAG, "available");
                    program.Echo("Responded: available");
                }
                else
                {
                    program.IGC.SendUnicastMessage(senderEntityId, Program.BROADCAST_TAG, "busy");
                    program.Echo("Responded: busy");
                }
            }

            public void HandleDockingCoordinateResponse(string message)
            {
                program.Echo($"Received docking coordinates: {message}");
                
                // Parse message: "dockcoord|M11|M12|M13|M14|M21|M22|M23|M24|M31|M32|M33|M34|M41|M42|M43|M44"
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
                        
                        program.Echo($"Parsed connector matrix: {matrix.Translation}");
                        
                        // Transition to docking state
                        if (CurrentState is RequestingDockingCoordinatesState)
                        {
                            var requestingState = (RequestingDockingCoordinatesState)CurrentState;
                            TransitionTo(new DockingState(this, matrix, requestingState._targetId));
                        }
                    }
                    catch (Exception ex)
                    {
                        program.Echo($"Error parsing docking coordinates: {ex.Message}");
                    }
                }
                else
                {
                    program.Echo($"Invalid docking coordinate message format: {message}");
                }
            }


            public void Dock()
            {
                if (CurrentState is IdleState)
                {
                    program.Echo("Requesting DCT entity ID for docking...");
                    
                    // Broadcast request for DCT entity ID
                    program.IGC.SendBroadcastMessage(Program.BROADCAST_TAG, "requestdctid");
                    
                    // Set a flag to handle the response
                    _waitingForDctId = true;
                }
                else
                {
                    program.Echo("Cannot dock while operation in progress!");
                }
            }

            public void TestAlign()
            {
                if (CurrentState is IdleState)
                {
                    program.Echo("Requesting DCT entity ID for test alignment...");
                    
                    // Broadcast request for DCT entity ID
                    program.IGC.SendBroadcastMessage(Program.BROADCAST_TAG, "requestdctid");
                    
                    // Set a flag to handle the response
                    _waitingForTestAlign = true;
                }
                else
                {
                    program.Echo("Cannot test align while operation in progress!");
                }
            }

            public void TestNav()
            {
                if (CurrentState is IdleState)
                {
                    program.Echo("Starting test navigation...");
                    TransitionTo(new TestNavState(this));
                }
                else
                {
                    program.Echo("Cannot test navigate while operation in progress!");
                }
            }

            public void Stop()
            {
                program.Echo("Stopping all operations...");
                TransitionTo(new IdleState(this));
            }

            public void HandleCargoStatusResponse(string message)
            {
                program.Echo($"Received cargo status: {message}");
                
                // Parse message: "cargostatus|empty" or "cargostatus|full"
                var parts = message.Split('|');
                if (parts.Length >= 2)
                {
                    var status = parts[1].ToLower();
                    
                    if (status == "empty" && CurrentState is LoadingState)
                    {
                        program.Echo("IceTractor cargo empty - transitioning to undocking");
                        TransitionTo(new UndockingState(this, false));
                    }
                    else
                    {
                        program.Echo($"IceTractor cargo status: {status}");
                    }
                }
                else
                {
                    program.Echo($"Invalid cargo status message format: {message}");
                }
            }

            public void HandleApproachPositionResponse(string message)
            {
                program.Echo($"Received approach position: {message}");
                
                // Parse message: "approachpos|X|Y|Z"
                var parts = message.Split('|');
                if (parts.Length >= 4)
                {
                    try
                    {
                        var x = double.Parse(parts[1]);
                        var y = double.Parse(parts[2]);
                        var z = double.Parse(parts[3]);
                        var approachPosition = new Vector3D(x, y, z);
                        
                        program.Echo($"Parsed approach position: {approachPosition}");
                        
                        // Handle different states that might be waiting for approach position
                        if (CurrentState is RequestingApproachPositionState)
                        {
                            var requestingState = (RequestingApproachPositionState)CurrentState;
                            TransitionTo(new NavigatingState(this, approachPosition, requestingState._targetId));
                        }
                        else if (_waitingForTestAlign)
                        {
                            _waitingForTestAlign = false;
                            program.Echo($"Received approach position for test align: {approachPosition}");
                            TransitionTo(new TestAlignState(this, approachPosition));
                        }
                    }
                    catch (Exception ex)
                    {
                        program.Echo($"Error parsing approach position: {ex.Message}");
                    }
                }
                else
                {
                    program.Echo($"Invalid approach position message format: {message}");
                }
            }

            public void HandleDctIdResponse(string message)
            {
                program.Echo($"Received DCT ID: {message}");
                
                // Parse message: "dctid|EntityId"
                var parts = message.Split('|');
                if (parts.Length >= 2)
                {
                    try
                    {
                        var dctEntityId = long.Parse(parts[1]);
                        program._droneControlTowerEntityId = dctEntityId;
                        
                        program.Echo($"Parsed DCT entity ID: {dctEntityId}");
                        
                        // Check if we were waiting for DCT ID (from dock command)
                        if (_waitingForDctId)
                        {
                            _waitingForDctId = false;
                            
                            // Check if connector is connected to determine next state
                            if (program._connector.IsConnected)
                            {
                                program.Echo("Connector is connected - transitioning to undocking");
                                TransitionTo(new UndockingState(this, false));
                            }
                            else
                            {
                                program.Echo("Connector is not connected - requesting approach position");
                                TransitionTo(new RequestingApproachPositionState(this, dctEntityId));
                            }
                        }
                        // Check if we were waiting for DCT ID (from test align command)
                        else if (_waitingForTestAlign)
                        {
                            _waitingForTestAlign = false;
                            
                            program.Echo("DCT entity ID received for test align - requesting approach position");
                            
                            // Request approach position from DCT
                            program.IGC.SendUnicastMessage(dctEntityId, Program.BROADCAST_TAG, "requestapproach");
                            program.Echo($"Sent approach position request to DCT: {dctEntityId}");
                            
                            // Set flag to handle the approach position response
                            _waitingForTestAlign = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        program.Echo($"Error parsing DCT ID: {ex.Message}");
                    }
                }
                else
                {
                    program.Echo($"Invalid DCT ID message format: {message}");
                }
            }

            private class IdleState : State<IceLifterContext>
            {
                public IdleState(IceLifterContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Green, false); // Solid green
                    _context.program.Echo("Idle State");
                    
                    // Stop all navigation and alignment systems
                    _context.program._navigation.Stop();
                    _context.program._alignment.Stop();
                    _context.program._navigationWithCollisionAvoidance.Stop();
                    
                    // Notify DroneControlTower that we're available again
                    if (_context.program._droneControlTowerEntityId != 0)
                    {
                        _context.program.IGC.SendUnicastMessage(_context.program._droneControlTowerEntityId, Program.BROADCAST_TAG, "available");
                        _context.program.Echo("Notified DroneControlTower: available");
                    }
                }

                public override void Execute()
                {
                }
            }

            private class UndockingState : State<IceLifterContext>
            {
                private Vector3D _approachPosition;
                private bool _dispatch;
                public UndockingState(IceLifterContext context, bool dispatch = false) : base(context) { _dispatch = dispatch; }
            
                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Yellow, true, 0.5, 0.5); // Blinking yellow
                    if (!_context.program._connector.IsConnected)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }
                    var otherConnector = _context.program._connector.OtherConnector;
                    var undockingDir = otherConnector.WorldMatrix.Forward;
                    _approachPosition = _context.program._remoteControl.GetPosition() + (undockingDir * DOCKING_OFFSET);
                    _context.program._connector.Disconnect();
                    _context.program._navigation.Precision = 1.0;
                }
            
                public override void Execute()
                {
                    if (!_context.program._navigation.NavigateTo(_approachPosition, 5.0))
                    {
                        return;
                    }
                    
                    // Send "clear" message to tractor if this is a dispatch operation
                    if (!_dispatch && _context.program._tractorEntityId != 0)
                    {
                        _context.program.IGC.SendUnicastMessage(_context.program._tractorEntityId, "DroneControl", "clear");
                        _context.program.Echo("Sent 'clear' message to tractor");
                    }
                    
                    var targetEntityId = _dispatch ? _context.program._tractorEntityId : _context.program._droneControlTowerEntityId;
                    _context.TransitionTo(new RequestingApproachPositionState(_context, targetEntityId));
                }
            }

            
            private class DockingState : State<IceLifterContext>
            {
                private MatrixD _targetConnectorMatrix;
                private Vector3D _dockingPosition;
                private bool _aligned = false;
                private bool _navigated = false;
                private long _targetEntityId;

                public DockingState(IceLifterContext context, MatrixD connectorMatrix, long targetEntityId) : base(context)
                {                    
                    _targetEntityId = targetEntityId;
                    
                    // Calculate base docking position BEFORE any rotations
                    // This is where the ship's connector should be positioned
                    _dockingPosition = connectorMatrix.Translation + (connectorMatrix.Forward * Program.CONNECTOR_OFFSET);
                    
                    // Rotate around the connector's local right vector (not world origin)
                    // This preserves the connector's position while rotating its orientation
                    var connectorPosition = connectorMatrix.Translation;
                    var connectorRight = connectorMatrix.Right;
                    
                    // Create first rotation matrix around the connector's right vector
                    var rotationMatrix1 = MatrixD.CreateFromAxisAngle(connectorRight, -Math.PI / 2); // 90 degrees around connector's right
                    
                    // Apply first rotation
                    var rotatedForward = Vector3D.TransformNormal(connectorMatrix.Forward, rotationMatrix1);
                    var rotatedUp = Vector3D.TransformNormal(connectorMatrix.Up, rotationMatrix1);
                    
                    // Create second rotation matrix around the rotated up vector
                    var rotationMatrix2 = MatrixD.CreateFromAxisAngle(rotatedUp, Math.PI / 2); // 90 degrees around rotated up
                    
                    // Apply second rotation
                    var finalForward = Vector3D.TransformNormal(rotatedForward, rotationMatrix2);
                    var finalUp = Vector3D.TransformNormal(rotatedUp, rotationMatrix2);
                    
                    // Create final matrix with both rotations applied
                    _targetConnectorMatrix = MatrixD.CreateWorld(connectorPosition, finalForward, finalUp);
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Blue, true, 0.5, 0.5); // Blinking blue
                    _context.program.Echo("Docking State");
                    _aligned = false;
                    _navigated = false;
                }

                public override void Execute()
                {
                    if (!_aligned)
                    {
                        // Align with connector orientation
                        if (_context.program._alignment.AlignWithWorldMatrix(_targetConnectorMatrix))
                        {
                            _aligned = true;
                            _context.program.Echo("Aligned with connector");
                            
                            // Now adjust docking position for ship's connector vs remote control offset
                            var remoteControlPos = _context.program._remoteControl.GetPosition();
                            var shipConnectorPos = _context.program._connector.GetPosition();
                            var offsetVector = shipConnectorPos - remoteControlPos;
                            
                            // Adjust docking position so ship's connector reaches the target, not remote control
                            _dockingPosition = _dockingPosition - offsetVector;
                            _context.program._navigation.Precision = 0.1; // more precise alignment for docking
                            _context.program.Echo($"Adjusted docking position for connector offset: {_dockingPosition}");
                        }
                    }
                    else if (!_navigated)
                    {
                        // Navigate to the adjusted docking position
                        if (_context.program._navigation.NavigateTo(_dockingPosition, 2.0))
                        {
                            _navigated = true;
                            _context.program.Echo("Reached docking position");
                        }
                        else if (_context.program._connector.Status == MyShipConnectorStatus.Connectable)
                        {
                           _context.program._navigation.Stop();
                           _navigated = true;
                        }
                    }
                    else
                    {
                        // Try to connect
                        _context.program._connector.Connect();
                        
                        // Check if connected
                        if (_context.program._connector.IsConnected)
                        {
                            _context.program.Echo("Successfully docked!");
                            
                            // Determine next state based on target entity
                            if (_targetEntityId == _context.program._tractorEntityId)
                            {
                                _context.program.Echo("Docked with IceTractor - transitioning to LoadingState");
                                _context.TransitionTo(new LoadingState(_context));
                            }
                            else
                            {
                                _context.program.Echo("Docked with DroneControlTower - transitioning to UnloadingState");
                                _context.TransitionTo(new UnloadingState(_context));
                            }
                        }
                        else
                        {
                            _context.program.Echo("Failed to connect, retrying...");
                            _context.TransitionTo(new DockingState(_context, _targetConnectorMatrix, _targetEntityId));
                        }
                    }
                }
            }


            private class RequestingApproachPositionState : State<IceLifterContext>
            {
                public long _targetId;

                public RequestingApproachPositionState(IceLifterContext context, long targetId) : base(context) 
                {
                    _targetId = targetId;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Cyan, true, 0.5, 0.5); // Blinking cyan
                    _context.program.Echo("Requesting Approach Position State");
                    
                    // Send request for approach position to target (DCT or Tractor)
                    _context.program.IGC.SendUnicastMessage(_targetId, Program.BROADCAST_TAG, "requestapproach");
                    _context.program.Echo($"Sent approach position request to {_targetId}");
                }

                public override void Execute()
                {
                    // Wait for response - handled in HandleIGCMessages
                }
            }

            private class RequestingDockingCoordinatesState : State<IceLifterContext>
            {
                public long _targetId;

                public RequestingDockingCoordinatesState(IceLifterContext context, long targetId) : base(context) 
                {
                    _targetId = targetId;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Cyan, true, 0.5, 0.5); // Blinking cyan
                    _context.program.Echo("Requesting Docking Coordinates State");
                    
                    // Send request for docking coordinates to target (DCT or Tractor)
                    _context.program.IGC.SendUnicastMessage(_targetId, Program.BROADCAST_TAG, "requestdockcoord");
                    _context.program.Echo($"Sent docking coordinate request to {_targetId}");
                }

                public override void Execute()
                {
                    // Wait for response - handled in HandleIGCMessages
                }
            }

            private class NavigatingState : State<IceLifterContext>
            {
                private Vector3D _targetPosition;
                private long _targetEntityId;

                public NavigatingState(IceLifterContext context, Vector3D targetPosition, long targetEntityId = 0) : base(context)
                {
                    _targetPosition = targetPosition;
                    _targetEntityId = targetEntityId;
                    _context.program._navigation.Precision = 1.0;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Yellow, false); // Solid yellow
                    _context.program.Echo("Navigating State");
                    _context.program._navigationWithCollisionAvoidance.NavigateTo(_targetPosition, 100.0);
                }

                public override void Execute()
                {
                    var navigationState = _context.program._navigationWithCollisionAvoidance.GetNavigationState();
                    if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Stuck)
                    {
                        _context.program.Echo("Navigation stuck - transitioning to IdleState");
                        _context.TransitionTo(new IdleState(_context));
                    }
                    else if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Idle)
                    {
                        var targetEntityId = _targetEntityId != 0 ? _targetEntityId : _context.program._tractorEntityId;
                        _context.TransitionTo(new RequestingDockingCoordinatesState(_context, targetEntityId));
                    }
                }
            }
            
            private class LoadingState : State<IceLifterContext>
            {
                private DateTime _lastCargoCheck = DateTime.MinValue;
                private const double CARGO_CHECK_INTERVAL = 2.0; // seconds

                public LoadingState(IceLifterContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Orange, true, 0.5, 0.5); // Blinking orange
                    _context.program.Echo("Loading State - Waiting for cargo transfer");
                    _lastCargoCheck = DateTime.MinValue;
                }

                public override void Execute()
                {
                    // Check if our drone's cargo is full
                    if (_context.program.AreCargoContainersFull())
                    {
                        _context.program.Echo("Drone cargo full - transitioning to undocking");
                        _context.TransitionTo(new UndockingState(_context, false));
                        return;
                    }

                    // Periodically check IceTractor's cargo status
                    if ((DateTime.Now - _lastCargoCheck).TotalSeconds >= CARGO_CHECK_INTERVAL)
                    {
                        // Send cargo status request to IceTractor
                        _context.program.IGC.SendUnicastMessage(_context.targetEntityId, Program.BROADCAST_TAG, "cargostatus");
                        _context.program.Echo("Sent cargo status request to IceTractor");
                        _lastCargoCheck = DateTime.Now;
                    }
                }
            }


            private class UnloadingState : State<IceLifterContext>
            {
                public UnloadingState(IceLifterContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Blue, true, 0.5, 0.5); // Blinking blue
                    _context.program.Echo("Unloading State");
                }

                public override void Execute()
                {
                    // Check if cargo is empty
                    if (_context.program.AreCargoContainersEmpty())
                    {
                        _context.program.Echo("Cargo unloaded - transitioning to idle");
                        _context.TransitionTo(new IdleState(_context));
                    }
                }
            }

            private class TestAlignState : State<IceLifterContext>
            {
                private Vector3D _targetPosition;

                public TestAlignState(IceLifterContext context, Vector3D targetPosition) : base(context)
                {
                    _targetPosition = targetPosition;
                }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Purple, true, 0.3, 0.3); // Fast blinking purple
                    _context.program.Echo("Test Align State");
                    _context.program.Echo($"Aligning with target position: {_targetPosition}");
                }

                public override void Execute()
                {
                    // Use the alignment system to align with the target position
                    if (_context.program._alignment.AlignWithTarget(_targetPosition))
                    {
                        _context.program.Echo("Successfully aligned with target!");
                        _context.TransitionTo(new IdleState(_context));
                    }
                    else
                    {
                        _context.program.Echo("Alignment in progress...");
                    }
                }
            }

            private class TestNavState : State<IceLifterContext>
            {
                private Vector3D _testTarget;

                public TestNavState(IceLifterContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetLightSettings(Color.Orange, true, 0.5, 0.5); // Blinking orange
                    _context.program.Echo("Test Navigation State");
                    
                    // Calculate test position 100m away on ship's forward vector
                    var currentPosition = _context.program._remoteControl.GetPosition();
                    var forwardVector = _context.program._remoteControl.WorldMatrix.Forward;
                    _testTarget = currentPosition + (forwardVector * 200.0);
                    
                    _context.program.Echo($"Test navigation target: {_testTarget}");
                    _context.program.Echo("Starting navigation with collision avoidance...");
                    
                    _context.program._navigation.Precision = 1.0;
                    _context.program._navigationWithCollisionAvoidance.NavigateTo(_testTarget, 100.0);
                }

                public override void Execute()
                {
                    var navigationState = _context.program._navigationWithCollisionAvoidance.GetNavigationState();
                    if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Stuck)
                    {
                        _context.TransitionTo(new IdleState(_context));
                    }
                    else if (navigationState == NavigationWithCollisionAvoidance.NavigationState.Idle)
                    {
                        _context.TransitionTo(new IdleState(_context));
                    }
                }
            }

        }
    }
}

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
        #region Constants
        private const string BROADCAST_TAG = "DroneControl";
        private const double STATUS_CHECK_INTERVAL = 5.0; // seconds
        private const double APPROACH_OFFSET = 15.0; // meters above docking connector
        #endregion

        #region Fields
        private List<ConnectorInfo> _connectors;
        private List<DroneInfo> _connectedDrones;
        private List<CargoRequest> _requestQueue;
        private List<long> _pendingStatusChecks;
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        private IMyTextPanel _displayPanel;
        private DateTime _lastStatusCheck;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            // Initialize connector list
            _connectors = new List<ConnectorInfo>();
            _connectedDrones = new List<DroneInfo>();
            _requestQueue = new List<CargoRequest>();
            _pendingStatusChecks = new List<long>();

            // Find all connectors on the station (local grid only)
            var allConnectors = this.GetLocalBlocks<IMyShipConnector>();
            foreach (var connector in allConnectors)
            {
                _connectors.Add(new ConnectorInfo
                {
                    Connector = connector,
                    Status = ConnectorStatus.Available,
                    ReservedDroneId = 0
                });
            }
            Echo($"Found {_connectors.Count} connectors");

            // Initialize IGC
            _broadcastListener = IGC.RegisterBroadcastListener(BROADCAST_TAG);
            _broadcastListener.SetMessageCallback(BROADCAST_TAG);
            _unicastListener = IGC.UnicastListener;
            _unicastListener.SetMessageCallback(BROADCAST_TAG);

            // Find display panel (local grid only)
            _displayPanel = this.GetLocalBlock<IMyTextPanel>("Drone Control Display");
            if (_displayPanel != null)
            {
                _displayPanel.ContentType = ContentType.TEXT_AND_IMAGE;
                _displayPanel.FontSize = 1.0f;
            }

            // Initial scan for connected drones
            ScanForConnectedDrones();
            _lastStatusCheck = DateTime.Now;
        }

        public void Save()
        {
            // Save state if needed
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // Handle IGC messages first
            if ((updateSource & UpdateType.IGC) > 0)
            {
                Echo("Handling IGC messages:" + argument);
                // Handle broadcast messages (from IceTractor)
                while (_broadcastListener.HasPendingMessage)
                {
                    var igcMessage = _broadcastListener.AcceptMessage();
                    if (igcMessage.Tag == BROADCAST_TAG && igcMessage.Data is string)
                    {
                        var message = igcMessage.Data.ToString();
                        Echo($"Received broadcast: {message}");
                        
                        if (message.StartsWith("cargofull|"))
                        {
                            HandleCargoFullMessage(message);
                        }
                        else if (message == "requestdctid")
                        {
                            HandleDctIdRequest(igcMessage.Source);
                        }
                        else {
                            Echo($"Unknown broadcast message: {message}");
                        }
                    }
                }
                
                // Handle unicast messages (from IceLifter)
                while (_unicastListener.HasPendingMessage)
                {
                    var igcMessage = _unicastListener.AcceptMessage();
                    if (igcMessage.Tag == BROADCAST_TAG && igcMessage.Data is string)
                    {
                        var message = igcMessage.Data.ToString();
                        Echo($"Received unicast: {message}");
                        
                        if (message == "busy")
                        {
                            HandleBusyResponse(igcMessage.Source);
                        }
                        else if (message == "available")
                        {
                            HandleAvailableResponse(igcMessage.Source);
                        }
                        else if (message == "requestapproach")
                        {
                            HandleApproachPositionRequest(igcMessage.Source);
                        }
                        else if (message == "requestdockcoord")
                        {
                            HandleDockingCoordinateRequest(igcMessage.Source);
                        }
                        else {
                            Echo($"Unknown unicast message: {message}");
                        }
                    }
                }
            }

            // Send periodic status checks and scan connectors
            if ((DateTime.Now - _lastStatusCheck).TotalSeconds >= STATUS_CHECK_INTERVAL)
            {
                ScanForConnectedDrones();
                SendStatusChecks();
                _lastStatusCheck = DateTime.Now;
            }
            else
            {
                // Handle commands only if not an IGC update
                if (!string.IsNullOrEmpty(argument))
                {
                    HandleCommand(argument);
                }
            }
            
            // Process queued requests when drones become available
            ProcessRequestQueue();
            
            // Update display
            UpdateDisplay();
        }

        private void HandleCommand(string argument)
        {
            var command = argument.ToLower();

            switch (command)
            {
                case "scan":
                    ScanForConnectedDrones();
                    Echo("Scanned for connected drones");
                    break;

                case "status":
                    Echo($"Connected drones: {_connectedDrones.Count}");
                    Echo($"Available connectors: {_connectors.Count(c => c.Status == ConnectorStatus.Available)}");
                    Echo($"Reserved connectors: {_connectors.Count(c => c.Status == ConnectorStatus.Reserved)}");
                    Echo($"Occupied connectors: {_connectors.Count(c => c.Status == ConnectorStatus.Occupied)}");
                    break;

                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        private void ScanForConnectedDrones()
        {
            // Store existing drone availability status
            var existingDroneStatus = new Dictionary<long, bool>();
            foreach (var drone in _connectedDrones)
            {
                existingDroneStatus[drone.EntityId] = drone.IsAvailable;
            }

            _connectedDrones.Clear();

            for (int i = 0; i < _connectors.Count; i++)
            {
                var connectorInfo = _connectors[i];
                var connector = connectorInfo.Connector;
                
                if (connector.IsConnected)
                {
                    // If connector was reserved and now connected, mark as occupied
                    if (connectorInfo.Status == ConnectorStatus.Reserved || connectorInfo.Status == ConnectorStatus.Available)
                    {
                        connectorInfo.Status = ConnectorStatus.Occupied;
                        _connectors[i] = connectorInfo;
                    }
                    
                    // Try to find programmable block on connected grid
                    var connectedGrid = connector.OtherConnector.CubeGrid;
                    var programmableBlocks = new List<IMyProgrammableBlock>();
                    GridTerminalSystem.GetBlocksOfType(programmableBlocks, block => block.CubeGrid == connectedGrid);
                    
                    foreach (var pb in programmableBlocks)
                    {
                        // Preserve availability status if drone was already known, default to busy until status check
                        var isAvailable = existingDroneStatus.ContainsKey(pb.EntityId) ? existingDroneStatus[pb.EntityId] : false;
                        
                        _connectedDrones.Add(new DroneInfo
                        {
                            ProgrammableBlock = pb,
                            Connector = connector,
                            EntityId = pb.EntityId,
                            IsAvailable = isAvailable
                        });
                    }
                }
                else
                {
                    // If connector was occupied and now disconnected, mark as available
                    if (connectorInfo.Status == ConnectorStatus.Occupied)
                    {
                        connectorInfo.Status = ConnectorStatus.Available;
                        _connectors[i] = connectorInfo;
                    }
                }
            }
        }


        private void HandleCargoFullMessage(string message)
        {
            // Parse message: "cargofull|EntityId"
            var parts = message.Split('|');
            if (parts.Length >= 2)
            {
                var entityId = long.Parse(parts[1]);

                Echo($"Cargo full request from {entityId}");

                // Send acknowledgment to sender
                IGC.SendUnicastMessage(entityId, BROADCAST_TAG, "acknowledged");

                // Check if request from this entity already exists and remove it
                _requestQueue.RemoveAll(r => r.EntityId == entityId);

                // Find available drone
                var availableDrone = _connectedDrones.FirstOrDefault(d => d.IsAvailable);
                if (availableDrone != null)
                {
                    // Dispatch drone immediately
                    DispatchDrone(availableDrone, entityId);
                }
                else
                {
                    // Queue the request
                    _requestQueue.Add(new CargoRequest
                    {
                        EntityId = entityId,
                        Timestamp = DateTime.Now
                    });
                }
            }
        }

        private void HandleApproachPositionRequest(long requesterEntityId)
        {
            Echo($"Approach position request from {requesterEntityId}");
            
            // Find the first available connector for approach position
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].Status == ConnectorStatus.Available)
                {
                    var connectorInfo = _connectors[i];
                    connectorInfo.Status = ConnectorStatus.Reserved;
                    connectorInfo.ReservedDroneId = requesterEntityId;
                    _connectors[i] = connectorInfo;
                    
                    var connectorPosition = connectorInfo.Connector.GetPosition();
                    var connectorForward = connectorInfo.Connector.WorldMatrix.Forward;
                    var approachPosition = connectorPosition + (connectorForward * APPROACH_OFFSET);
                    
                    // Send position data as pipe-separated values
                    var positionMessage = $"approachpos|{approachPosition.X}|{approachPosition.Y}|{approachPosition.Z}";
                    
                    IGC.SendUnicastMessage(requesterEntityId, BROADCAST_TAG, positionMessage);
                    Echo($"Sent approach position: {approachPosition} (connector reserved for drone {requesterEntityId})");
                    return;
                }
            }
            
            Echo("ERROR: No available connectors found!");
        }

        private void HandleDctIdRequest(long requesterEntityId)
        {
            Echo($"DCT ID request from {requesterEntityId}");
            
            // Send our entity ID
            var dctIdMessage = $"dctid|{Me.EntityId}";
            IGC.SendUnicastMessage(requesterEntityId, BROADCAST_TAG, dctIdMessage);
            Echo($"Sent DCT entity ID: {Me.EntityId}");
        }

        private void HandleDockingCoordinateRequest(long requesterEntityId)
        {
            Echo($"Docking coordinate request from {requesterEntityId}");
            
            // Find the connector reserved for this specific drone
            var reservedConnectorInfo = _connectors.FirstOrDefault(c => c.ReservedDroneId == requesterEntityId);
            if (reservedConnectorInfo.Connector != null)
            {
                var connectorMatrix = reservedConnectorInfo.Connector.WorldMatrix;
                
                // Send connector matrix as pipe-separated values
                var matrixMessage = $"dockcoord|{connectorMatrix.M11}|{connectorMatrix.M12}|{connectorMatrix.M13}|{connectorMatrix.M14}|" +
                                   $"{connectorMatrix.M21}|{connectorMatrix.M22}|{connectorMatrix.M23}|{connectorMatrix.M24}|" +
                                   $"{connectorMatrix.M31}|{connectorMatrix.M32}|{connectorMatrix.M33}|{connectorMatrix.M34}|" +
                                   $"{connectorMatrix.M41}|{connectorMatrix.M42}|{connectorMatrix.M43}|{connectorMatrix.M44}";
                
                IGC.SendUnicastMessage(requesterEntityId, BROADCAST_TAG, matrixMessage);
                Echo($"Sent docking coordinates: {connectorMatrix.Translation} (for drone {requesterEntityId})");
            }
            else
            {
                Echo($"ERROR: No connector reserved for drone {requesterEntityId}!");
            }
        }

        private void DispatchDrone(DroneInfo drone, long entityId)
        {
            var dispatchMessage = $"dispatch|{entityId}";
            Echo($"Sending dispatch message to drone {drone.EntityId}: {dispatchMessage}");
            IGC.SendUnicastMessage(drone.EntityId, BROADCAST_TAG, dispatchMessage);
            
            drone.IsAvailable = false;
            
            // Mark the drone's connector as available when dispatched
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].Connector == drone.Connector)
                {
                    var connectorInfo = _connectors[i];
                    connectorInfo.Status = ConnectorStatus.Available;
                    connectorInfo.ReservedDroneId = 0;
                    _connectors[i] = connectorInfo;
                    break;
                }
            }
            
            Echo($"Dispatched drone {drone.ProgrammableBlock.CustomName} to {entityId} (connector marked available)");
        }

        private void HandleBusyResponse(long droneEntityId)
        {
            // Find the drone that sent the busy response
            var drone = _connectedDrones.FirstOrDefault(d => d.EntityId == droneEntityId);
            if (drone != null)
            {
                drone.IsAvailable = false;
                Echo($"Drone {drone.ProgrammableBlock.CubeGrid.CustomName} reported busy - marked as unavailable");
                
                // Remove any pending status check for this drone
                _pendingStatusChecks.Remove(droneEntityId);
            }
        }

        private void HandleAvailableResponse(long droneEntityId)
        {
            // Find the drone that sent the available response
            var drone = _connectedDrones.FirstOrDefault(d => d.EntityId == droneEntityId);
            if (drone != null)
            {
                drone.IsAvailable = true;
                Echo($"Drone {drone.ProgrammableBlock.CubeGrid.CustomName} reported available - marked as available");
                
                // Remove any pending status check for this drone
                _pendingStatusChecks.Remove(droneEntityId);
            }
        }

        private void SendStatusChecks()
        {
            // Mark any pending requests as timed out (no response received)
            foreach (var entityId in _pendingStatusChecks)
            {
                var drone = _connectedDrones.FirstOrDefault(d => d.EntityId == entityId);
                if (drone != null)
                {
                    drone.IsAvailable = false;
                    Echo($"Drone {drone.ProgrammableBlock.CubeGrid.CustomName} timed out - marked as busy");
                }
            }
            
            // Clear pending list and start new status checks
            _pendingStatusChecks.Clear();
            
            foreach (var drone in _connectedDrones)
            {
                // Send status check to each drone
                IGC.SendUnicastMessage(drone.EntityId, BROADCAST_TAG, "status check");
                
                // Track this request
                _pendingStatusChecks.Add(drone.EntityId);
            }
            
            if (_connectedDrones.Count > 0)
            {
                Echo($"Sent status checks to {_connectedDrones.Count} drones");
            }
        }


        private void ProcessRequestQueue()
        {
            if (_requestQueue.Count == 0) return;

            Echo($"Processing queue: {_requestQueue.Count} requests, {_connectedDrones.Count} drones");
            foreach (var drone in _connectedDrones)
            {
                Echo($"  Drone: {drone.ProgrammableBlock.CubeGrid.CustomName} - Available: {drone.IsAvailable}");
            }

            var availableDrone = _connectedDrones.FirstOrDefault(d => d.IsAvailable);
            if (availableDrone != null)
            {
                // Get the oldest request from the queue
                var request = _requestQueue[0];
                _requestQueue.RemoveAt(0);
                
                // Dispatch drone to handle the queued request
                DispatchDrone(availableDrone, request.EntityId);
            }
        }

        private void UpdateDisplay()
        {
            if (_displayPanel == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== DRONE CONTROL TOWER ===");
            sb.AppendLine();
            sb.AppendLine($"Available Drones: {_connectedDrones.Count(d => d.IsAvailable)}");
            sb.AppendLine($"Busy Drones: {_connectedDrones.Count(d => !d.IsAvailable)}");
            sb.AppendLine($"Queued Requests: {_requestQueue.Count}");
            sb.AppendLine();
            sb.AppendLine("=== DOCKING STATUS ===");
            
            foreach (var connectorInfo in _connectors)
            {
                var status = connectorInfo.Status.ToString().ToUpper();
                var droneName = "";
                
                if (connectorInfo.Status == ConnectorStatus.Occupied && connectorInfo.Connector.IsConnected)
                {
                    // Get the connected grid name
                    var connectedGrid = connectorInfo.Connector.OtherConnector.CubeGrid;
                    droneName = $" - {connectedGrid.CustomName}";
                }
                // Reserved connectors don't show drone name
                
                sb.AppendLine($"{connectorInfo.Connector.CustomName} --- {status}{droneName}");
            }
            
            sb.AppendLine();
            sb.AppendLine("=== DRONE STATUS ===");
            
            foreach (var drone in _connectedDrones)
            {
                var status = drone.IsAvailable ? "AVAILABLE" : "BUSY";
                sb.AppendLine($"{drone.ProgrammableBlock.CubeGrid.CustomName}: {status}");
            }

            if (_requestQueue.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"=== QUEUED REQUESTS: {_requestQueue.Count} ===");
            }

            _displayPanel.WriteText(sb.ToString());
        }

        private enum ConnectorStatus
        {
            Available,
            Reserved,
            Occupied
        }

        private struct ConnectorInfo
        {
            public IMyShipConnector Connector;
            public ConnectorStatus Status;
            public long ReservedDroneId;
        }

        private class DroneInfo
        {
            public IMyProgrammableBlock ProgrammableBlock;
            public IMyShipConnector Connector;
            public long EntityId;
            public bool IsAvailable;
        }

        private class CargoRequest
        {
            public long EntityId;
            public DateTime Timestamp;
        }

    }
}

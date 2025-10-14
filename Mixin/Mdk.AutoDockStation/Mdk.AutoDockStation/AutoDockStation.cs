using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    public class AutoDockStation
    {
        #region Constants
        private const string BROADCAST_TAG = "AutoDock";
        private const double UPDATE_INTERVAL_DEFAULT = 5.0;
        private const double APPROACH_DISTANCE_DEFAULT = 10.0;
        #endregion
        
        #region Fields
        private bool _initialized = false;
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private List<ConnectorInfo> _connectors = new List<ConnectorInfo>();
        private Dictionary<long, DateTime> _reservationStatusChecks;
        private IMyBroadcastListener _broadcastListener;
        private IMyUnicastListener _unicastListener;
        private DateTime _lastUpdate;
        private IMyTextPanel _lcdPanel;
        private int _scrollOffset = 0;
        private bool _scrollDirection = true;
        private DateTime _lastScrollUpdate = DateTime.MinValue;
        private bool _scrollPaused = false;
        private DateTime _scrollPauseStart = DateTime.MinValue;
        private const double SCROLL_INTERVAL = 0.25;
        private const double SCROLL_PAUSE_DURATION = 2.0;
        private List<string> _debugLog = new List<string>();
        private const int MAX_DEBUG_LINES = 50;
        private Random _random = new Random();
        #endregion

        #region Properties
        public double UpdateInterval { get; set; } = UPDATE_INTERVAL_DEFAULT;
        public double ApproachDistance { get; set; } = APPROACH_DISTANCE_DEFAULT;
        #endregion

        #region Methods
        public bool Initialize(Program program, out string errorMessage)
        {
            _program = program;
            errorMessage = string.Empty;
            
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "AutoDockStation: No remote control found!";
                return false;
            }
            
            _reservationStatusChecks = new Dictionary<long, DateTime>();
            _lastUpdate = DateTime.MinValue;

            var allConnectors = new List<IMyShipConnector>();
            
            var connectorsByName = program.GetLocalBlocksNameContains<IMyShipConnector>("[AD]");
            allConnectors.AddRange(connectorsByName);
            
            var groups = new List<IMyBlockGroup>();
            program.GridTerminalSystem.GetBlockGroups(groups, g => g.Name.Contains("[AD]"));
            foreach (var group in groups)
            {
                var groupConnectors = new List<IMyShipConnector>();
                group.GetBlocksOfType(groupConnectors, c => c.IsSameConstructAs(program.Me));
                allConnectors.AddRange(groupConnectors);
            }
            
            var uniqueConnectors = allConnectors.Distinct().ToList();
            
            if (uniqueConnectors.Count == 0)
            {
                errorMessage = "No connectors with [AD] tag or in [AD] group found";
                return false;
            }
            foreach (var connector in uniqueConnectors)
            {
                _connectors.Add(new ConnectorInfo
                {
                    Connector = connector,
                    Status = ConnectorStatus.Available,
                    ReservedShipId = 0
                });
            }
            _broadcastListener = program.IGC.RegisterBroadcastListener(BROADCAST_TAG);
            _broadcastListener.SetMessageCallback(BROADCAST_TAG);
            _unicastListener = program.IGC.UnicastListener;
            _unicastListener.SetMessageCallback(BROADCAST_TAG);

            var lcdPanels = program.GetLocalBlocksNameContains<IMyTextPanel>("[AD]");
            if (lcdPanels.Count > 0)
            {
                _lcdPanel = lcdPanels[0];
                _lcdPanel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            }

            _initialized = true;

            return true;
        }

        public void Update()
        {
            if (!_initialized)
            {
                return;
            }

            HandleMessages();
            SendPositionUpdates();

            if ((DateTime.Now - _lastUpdate).TotalSeconds >= UpdateInterval)
            {
                UpdateConnectorStatus();
                CheckReservations();
                _lastUpdate = DateTime.Now;
            }

            UpdateDisplay();
        }

        private void HandleMessages()
        {
            while (_broadcastListener.HasPendingMessage)
            {
                var message = _broadcastListener.AcceptMessage();
                if (message.Tag == BROADCAST_TAG && message.Data is string)
                {
                    var data = message.Data.ToString();
                    if (data.StartsWith("requestdocking"))
                    {
                        var parts = data.Split('|');
                        var droneName = parts.Length > 1 ? parts[1] : "Unknown";
                        HandleDockingRequest(message.Source, droneName);
                    }
                }
            }

            while (_unicastListener.HasPendingMessage)
            {
                var message = _unicastListener.AcceptMessage();
                if (message.Tag == BROADCAST_TAG && message.Data is string)
                {
                    var data = message.Data.ToString();
                    if (data.StartsWith("requestdocking"))
                    {
                        var parts = data.Split('|');
                        var droneName = parts.Length > 1 ? parts[1] : "Unknown";
                        HandleDockingRequest(message.Source, droneName);
                    }
                    else if (data == "requestdockcoord")
                    {
                        HandleDockCoordRequest(message.Source);
                    }
                    else if (data == "onmyway")
                    {
                        HandleOnMyWayResponse(message.Source);
                    }
                }
            }
        }

        private void HandleDockingRequest(long shipId, string droneName)
        {
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].ReservedShipId == shipId)
                {
                    var connectorInfo = _connectors[i];
                    
                    // If ship is re-requesting while in Docking state, it means it's outside the approach cone
                    // Transition back to Reserved
                    if (connectorInfo.Status == ConnectorStatus.Docking)
                    {
                        connectorInfo.Status = ConnectorStatus.Reserved;
                        connectorInfo.ReservationTime = DateTime.Now;
                        _connectors[i] = connectorInfo;
                        _program.Echo($"Ship {shipId} re-requested docking, transitioning from Docking to Reserved");
                    }
                    
                    var connectorPosition = connectorInfo.Connector.GetPosition();
                    var connectorForward = connectorInfo.Connector.WorldMatrix.Forward;
                    var approachPosition = connectorPosition + (connectorForward * ApproachDistance);
                    var stationVelocity = _remoteControl.GetShipVelocities().LinearVelocity;

                    var response = $"approachpos|{approachPosition.X:F2}|{approachPosition.Y:F2}|{approachPosition.Z:F2}|" +
                                  $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
                    _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, response);
                    
                    return;
                }
            }

            var availableIndices = new List<int>();
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].Status == ConnectorStatus.Available)
                {
                    availableIndices.Add(i);
                }
            }

            if (availableIndices.Count > 0)
            {
                int randomIndex = availableIndices[_random.Next(availableIndices.Count)];
                
                var connectorInfo = _connectors[randomIndex];
                connectorInfo.Status = ConnectorStatus.Reserved;
                connectorInfo.ReservedShipId = shipId;
                connectorInfo.ReservedShipName = droneName;
                connectorInfo.ReservationTime = DateTime.Now;
                _connectors[randomIndex] = connectorInfo;

                var connectorPosition = connectorInfo.Connector.GetPosition();
                var connectorForward = connectorInfo.Connector.WorldMatrix.Forward;
                var approachPosition = connectorPosition + (connectorForward * ApproachDistance);
                var stationVelocity = _remoteControl.GetShipVelocities().LinearVelocity;

                var response = $"approachpos|{approachPosition.X:F2}|{approachPosition.Y:F2}|{approachPosition.Z:F2}|" +
                              $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
                _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, response);
                
                return;
            }

            _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, "nodocksavailable");
        }

        private void HandleDockCoordRequest(long shipId)
        {
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].ReservedShipId == shipId && _connectors[i].Status == ConnectorStatus.Reserved)
                {
                    var connectorInfo = _connectors[i];
                    connectorInfo.Status = ConnectorStatus.Docking;
                    _connectors[i] = connectorInfo;
                    
                    var matrix = connectorInfo.Connector.WorldMatrix;
                    var stationVelocity = _remoteControl.GetShipVelocities().LinearVelocity;
                    var response = $"dockcoord|{matrix.M11}|{matrix.M12}|{matrix.M13}|{matrix.M14}|" +
                                  $"{matrix.M21}|{matrix.M22}|{matrix.M23}|{matrix.M24}|" +
                                  $"{matrix.M31}|{matrix.M32}|{matrix.M33}|{matrix.M34}|" +
                                  $"{matrix.M41}|{matrix.M42}|{matrix.M43}|{matrix.M44}|" +
                                  $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
                    _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, response);
                    return;
                }
            }
            
            _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, "denied");
            _program.Echo("Docking denied, re-requesting approach...");
        }

        private void HandleOnMyWayResponse(long shipId)
        {
            if (_reservationStatusChecks.ContainsKey(shipId))
            {
                _reservationStatusChecks.Remove(shipId);
            }
        }

        private void SendPositionUpdates()
        {
            var stationVelocity = _remoteControl.GetShipVelocities().LinearVelocity;
            
            foreach (var connectorInfo in _connectors)
            {
                if (connectorInfo.ReservedShipId == 0)
                    continue;

                if (connectorInfo.Status == ConnectorStatus.Reserved)
                {
                    var connectorPosition = connectorInfo.Connector.GetPosition();
                    var connectorForward = connectorInfo.Connector.WorldMatrix.Forward;
                    var approachPosition = connectorPosition + (connectorForward * ApproachDistance);
                    
                    var message = $"approachupdate|{approachPosition.X:F2}|{approachPosition.Y:F2}|{approachPosition.Z:F2}|" +
                                 $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
                    _program.IGC.SendUnicastMessage(connectorInfo.ReservedShipId, BROADCAST_TAG, message);
                }
                else if (connectorInfo.Status == ConnectorStatus.Docking)
                {
                    var matrix = connectorInfo.Connector.WorldMatrix;
                    var message = $"dockingupdate|{matrix.M11}|{matrix.M12}|{matrix.M13}|{matrix.M14}|" +
                                 $"{matrix.M21}|{matrix.M22}|{matrix.M23}|{matrix.M24}|" +
                                 $"{matrix.M31}|{matrix.M32}|{matrix.M33}|{matrix.M34}|" +
                                 $"{matrix.M41}|{matrix.M42}|{matrix.M43}|{matrix.M44}|" +
                                 $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
                    _program.IGC.SendUnicastMessage(connectorInfo.ReservedShipId, BROADCAST_TAG, message);
                }
            }
        }

        private void UpdateConnectorStatus()
        {
            for (int i = 0; i < _connectors.Count; i++)
            {
                var connectorInfo = _connectors[i];
                
                if (connectorInfo.Connector.IsConnected && connectorInfo.Status != ConnectorStatus.Occupied)
                {
                    connectorInfo.Status = ConnectorStatus.Occupied;
                    _connectors[i] = connectorInfo;
                }
                else if (!connectorInfo.Connector.IsConnected && connectorInfo.Status == ConnectorStatus.Occupied)
                {
                    connectorInfo.Status = ConnectorStatus.Available;
                    connectorInfo.ReservedShipId = 0;
                    connectorInfo.ReservedShipName = string.Empty;
                    _connectors[i] = connectorInfo;
                }
            }
        }

        private void CheckReservations()
        {
            var shipsToCheck = new List<long>();
            
            foreach (var connectorInfo in _connectors)
            {
                if ((connectorInfo.Status == ConnectorStatus.Reserved || connectorInfo.Status == ConnectorStatus.Docking) 
                    && connectorInfo.ReservedShipId != 0)
                {
                    shipsToCheck.Add(connectorInfo.ReservedShipId);
                }
            }

            foreach (var shipId in shipsToCheck)
            {
                if (_reservationStatusChecks.ContainsKey(shipId))
                {
                    var timeSinceResponse = (DateTime.Now - _reservationStatusChecks[shipId]).TotalSeconds;
                    if (timeSinceResponse >= UpdateInterval)
                    {
                        _remoteControl.Log($"AutoDockStation: Release reservation for ship {shipId} due to timeout");
                        ReleaseReservation(shipId);
                        _reservationStatusChecks.Remove(shipId);
                    }
                }
                else
                {
                    _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, "statuscheck");
                    _reservationStatusChecks[shipId] = DateTime.MinValue;
                }
            }
        }

        private void ReleaseReservation(long shipId)
        {
            for (int i = 0; i < _connectors.Count; i++)
            {
                if (_connectors[i].ReservedShipId == shipId)
                {
                    if (_connectors[i].Status == ConnectorStatus.Occupied || _connectors[i].Connector.IsConnected)
                    {
                        continue;
                    }

                    if (_connectors[i].Status == ConnectorStatus.Reserved || _connectors[i].Status == ConnectorStatus.Docking)
                    {
                        var connectorInfo = _connectors[i];
                        connectorInfo.Status = ConnectorStatus.Available;
                        connectorInfo.ReservedShipId = 0;
                        connectorInfo.ReservedShipName = string.Empty;
                        _connectors[i] = connectorInfo;
                    }
                }
            }
        }

        private void UpdateDisplay()
        {
            if (_lcdPanel == null)
                return;

            var allLines = new List<string>();
            allLines.Add("=== Auto Dock Station ===");
            allLines.Add("");

            var sortedConnectors = _connectors.OrderBy(c => c.Connector.CustomName, new NaturalStringComparer()).ToList();

            for (int i = 0; i < sortedConnectors.Count; i++)
            {
                var connector = sortedConnectors[i];
                var connectorName = connector.Connector.CustomName;
                
                switch (connector.Status)
                {
                    case ConnectorStatus.Available:
                        allLines.Add($"{connectorName} - Available");
                        break;
                    case ConnectorStatus.Reserved:
                        allLines.Add($"{connectorName} - Reserved ({connector.ReservedShipName})");
                        break;
                    case ConnectorStatus.Docking:
                        allLines.Add($"{connectorName} - Docking ({connector.ReservedShipName})");
                        break;
                    case ConnectorStatus.Occupied:
                        var otherConnector = connector.Connector.OtherConnector;
                        var shipName = otherConnector != null ? otherConnector.CubeGrid.CustomName : "Unknown";
                        allLines.Add($"{connectorName} - Occupied ({shipName})");
                        break;
                }
            }

            var fontSize = _lcdPanel.FontSize;
            var surfaceSize = _lcdPanel.SurfaceSize;
            var linesPerScreen = (int)(surfaceSize.Y / (fontSize * 28f));

            if (allLines.Count > linesPerScreen)
            {
                if (_scrollPaused)
                {
                    if ((DateTime.Now - _scrollPauseStart).TotalSeconds >= SCROLL_PAUSE_DURATION)
                    {
                        _scrollPaused = false;
                        _lastScrollUpdate = DateTime.Now;
                    }
                }
                else if ((DateTime.Now - _lastScrollUpdate).TotalSeconds >= SCROLL_INTERVAL)
                {
                    if (_scrollDirection)
                    {
                        _scrollOffset++;
                        var maxScrollOffset = Math.Min(allLines.Count - 1, allLines.Count - linesPerScreen + 2);
                        if (_scrollOffset >= maxScrollOffset)
                        {
                            _scrollDirection = false;
                            _scrollPaused = true;
                            _scrollPauseStart = DateTime.Now;
                        }
                    }
                    else
                    {
                        _scrollOffset--;
                        if (_scrollOffset <= 0)
                        {
                            _scrollDirection = true;
                            _scrollPaused = true;
                            _scrollPauseStart = DateTime.Now;
                        }
                    }
                    _lastScrollUpdate = DateTime.Now;
                }

                var visibleLines = allLines.Skip(_scrollOffset).Take(linesPerScreen);
                var sb = new System.Text.StringBuilder();
                foreach (var line in visibleLines)
                {
                    sb.AppendLine(line);
                }
                _lcdPanel.WriteText(sb.ToString());
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var line in allLines)
                {
                    sb.AppendLine(line);
                }
                _lcdPanel.WriteText(sb.ToString());
            }
        }

        public List<ConnectorInfo> GetConnectors()
        {
            return _connectors;
        }
        #endregion

        #region Types
        public enum ConnectorStatus
        {
            Available,
            Reserved,
            Docking,
            Occupied
        }

        public struct ConnectorInfo
        {
            public IMyShipConnector Connector;
            public ConnectorStatus Status;
            public long ReservedShipId;
            public string ReservedShipName;
            public DateTime ReservationTime;
        }

        private class NaturalStringComparer : IComparer<string>
        {
            private static System.Text.RegularExpressions.Regex _numberRegex = 
                new System.Text.RegularExpressions.Regex(@"(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

            public int Compare(string x, string y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                var xParts = _numberRegex.Split(x);
                var yParts = _numberRegex.Split(y);

                for (int i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
                {
                    if (xParts[i] != yParts[i])
                    {
                        int xNum, yNum;
                        if (int.TryParse(xParts[i], out xNum) && int.TryParse(yParts[i], out yNum))
                        {
                            return xNum.CompareTo(yNum);
                        }
                        return string.Compare(xParts[i], yParts[i], StringComparison.Ordinal);
                    }
                }

                return xParts.Length.CompareTo(yParts.Length);
            }
        }
        #endregion
    }
}


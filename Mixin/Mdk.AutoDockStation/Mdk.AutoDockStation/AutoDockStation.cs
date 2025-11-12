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
        private List<ConnectorInfo> _allConnectors = new List<ConnectorInfo>();
        private Dictionary<string, List<ConnectorInfo>> _connectorsByKey = new Dictionary<string, List<ConnectorInfo>>(StringComparer.OrdinalIgnoreCase);
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
            
            var connectorsByName = program.GetLocalBlocksNameContains<IMyShipConnector>("[AD:");
            allConnectors.AddRange(connectorsByName);
            
            var groups = new List<IMyBlockGroup>();
            program.GridTerminalSystem.GetBlockGroups(groups, g => g.Name.Contains("[AD:"));
            foreach (var group in groups)
            {
                var groupConnectors = new List<IMyShipConnector>();
                group.GetBlocksOfType(groupConnectors, c => c.IsSameConstructAs(program.Me));
                allConnectors.AddRange(groupConnectors);
            }
            
            var uniqueConnectors = allConnectors.Distinct().ToList();
            
            _allConnectors.Clear();
            _connectorsByKey.Clear();

            foreach (var connector in uniqueConnectors)
            {
                string connectorKey;
                if (!TryGetConnectorKey(connector.CustomName, out connectorKey))
                {
                    continue;
                }

                var connectorInfo = new ConnectorInfo
                {
                    Connector = connector,
                    Key = connectorKey,
                    Status = ConnectorStatus.Available,
                    ReservedShipId = 0,
                    ReservedShipName = string.Empty
                };

                _allConnectors.Add(connectorInfo);

                List<ConnectorInfo> connectorList;
                if (!_connectorsByKey.TryGetValue(connectorKey, out connectorList))
                {
                    connectorList = new List<ConnectorInfo>();
                    _connectorsByKey[connectorKey] = connectorList;
                }

                connectorList.Add(connectorInfo);
            }

            if (_allConnectors.Count == 0)
            {
                errorMessage = @"No connectors with [AD:Name] tag found";
                return false;
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
                        var connectorKey = parts.Length > 2 ? parts[2] : "*";
                        HandleDockingRequest(message.Source, droneName, connectorKey, true);
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
                        var connectorKey = parts.Length > 2 ? parts[2] : "*";
                        HandleDockingRequest(message.Source, droneName, connectorKey, false);
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

        private void HandleDockingRequest(long shipId, string droneName, string requestedConnectorKey, bool isBroadcast)
        {
            requestedConnectorKey = string.IsNullOrWhiteSpace(requestedConnectorKey) ? "*" : requestedConnectorKey;

            var existingConnector = FindConnectorByShipId(shipId);
            if (existingConnector != null)
            {
                if (existingConnector.Status == ConnectorStatus.Docking)
                {
                    existingConnector.Status = ConnectorStatus.Reserved;
                    existingConnector.ReservationTime = DateTime.Now;
                    _program.Echo($"Ship {shipId} re-requested docking, transitioning from Docking to Reserved");
                }

                existingConnector.ReservedShipName = droneName;
                SendApproachResponse(existingConnector, shipId);
                return;
            }

            if (requestedConnectorKey != "*" && !_connectorsByKey.ContainsKey(requestedConnectorKey))
            {
                if (!isBroadcast)
                {
                    _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, "nodocksavailable");
                }
                return;
            }

            List<ConnectorInfo> candidates = new List<ConnectorInfo>();

            if (requestedConnectorKey == "*")
            {
                foreach (var connector in _allConnectors)
                {
                    if (connector.Status == ConnectorStatus.Available)
                    {
                        candidates.Add(connector);
                    }
                }
            }
            else
            {
                var connectorsForKey = _connectorsByKey[requestedConnectorKey];
                foreach (var connector in connectorsForKey)
                {
                    if (connector.Status == ConnectorStatus.Available)
                    {
                        candidates.Add(connector);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, "nodocksavailable");
                return;
            }

            _program.Echo($"Found {candidates.Count} candidates for {droneName} on {requestedConnectorKey}");

            var selectedConnector = candidates[_random.Next(candidates.Count)];
            selectedConnector.Status = ConnectorStatus.Reserved;
            selectedConnector.ReservedShipId = shipId;
            selectedConnector.ReservedShipName = droneName;
            selectedConnector.ReservationTime = DateTime.Now;

            SendApproachResponse(selectedConnector, shipId);
        }

        private ConnectorInfo FindConnectorByShipId(long shipId)
        {
            foreach (var connectorInfo in _allConnectors)
            {
                if (connectorInfo.ReservedShipId == shipId)
                {
                    return connectorInfo;
                }
            }

            return null;
        }

        private void SendApproachResponse(ConnectorInfo connectorInfo, long shipId)
        {
            var connectorPosition = connectorInfo.Connector.GetPosition();
            var connectorForward = connectorInfo.Connector.WorldMatrix.Forward;
            var approachPosition = connectorPosition + (connectorForward * ApproachDistance);
            var stationVelocity = _remoteControl.GetShipVelocities().LinearVelocity;

            var response = $"approachpos|{approachPosition.X:F2}|{approachPosition.Y:F2}|{approachPosition.Z:F2}|" +
                          $"{stationVelocity.X:F2}|{stationVelocity.Y:F2}|{stationVelocity.Z:F2}";
            _program.IGC.SendUnicastMessage(shipId, BROADCAST_TAG, response);
        }

        private void HandleDockCoordRequest(long shipId)
        {
            foreach (var connectorInfo in _allConnectors)
            {
                if (connectorInfo.ReservedShipId == shipId && connectorInfo.Status == ConnectorStatus.Reserved)
                {
                    connectorInfo.Status = ConnectorStatus.Docking;

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
            
            foreach (var connectorInfo in _allConnectors)
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
            foreach (var connectorInfo in _allConnectors)
            {
                if (connectorInfo.Connector.IsConnected && connectorInfo.Status != ConnectorStatus.Occupied)
                {
                    connectorInfo.Status = ConnectorStatus.Occupied;
                }
                else if (!connectorInfo.Connector.IsConnected && connectorInfo.Status == ConnectorStatus.Occupied)
                {
                    connectorInfo.Status = ConnectorStatus.Available;
                    connectorInfo.ReservedShipId = 0;
                    connectorInfo.ReservedShipName = string.Empty;
                }
            }
        }

        private void CheckReservations()
        {
            var shipsToCheck = new List<long>();
            
            foreach (var connectorInfo in _allConnectors)
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
            foreach (var connectorInfo in _allConnectors)
            {
                if (connectorInfo.ReservedShipId != shipId)
                {
                    continue;
                }

                if (connectorInfo.Status == ConnectorStatus.Occupied || connectorInfo.Connector.IsConnected)
                {
                    continue;
                }

                if (connectorInfo.Status == ConnectorStatus.Reserved || connectorInfo.Status == ConnectorStatus.Docking)
                {
                    connectorInfo.Status = ConnectorStatus.Available;
                    connectorInfo.ReservedShipId = 0;
                    connectorInfo.ReservedShipName = string.Empty;
                }
            }
        }

        private bool TryGetConnectorKey(string name, out string key)
        {
            key = string.Empty;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var startIndex = name.IndexOf("[AD:", StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
            {
                return false;
            }

            startIndex += 4;
            var endIndex = name.IndexOf(']', startIndex);
            if (endIndex < 0)
            {
                return false;
            }

            key = name.Substring(startIndex, endIndex - startIndex).Trim();
            return !string.IsNullOrEmpty(key);
        }

        private void UpdateDisplay()
        {
            if (_lcdPanel == null)
                return;

            var allLines = new List<string>();
            allLines.Add("=== Auto Dock Station ===");
            allLines.Add("");

            var sortedConnectors = _allConnectors.OrderBy(c => c.Connector.CustomName, new NaturalStringComparer()).ToList();

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
            return _allConnectors;
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

        public class ConnectorInfo
        {
            public IMyShipConnector Connector;
            public string Key;
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



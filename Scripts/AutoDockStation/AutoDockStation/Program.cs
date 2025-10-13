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
        private AutoDockStation _autoDockStation;
        private IMyTextPanel _displayPanel;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            _autoDockStation = new AutoDockStation();

            string errorMessage;
            if (!_autoDockStation.Initialize(this, out errorMessage))
            {
                Echo($"AutoDockStation Error: {errorMessage}");
                return;
            }

            _displayPanel = this.GetLocalBlock<IMyTextPanel>("Docking Display");
            if (_displayPanel != null)
            {
                _displayPanel.ContentType = ContentType.TEXT_AND_IMAGE;
                _displayPanel.FontSize = 1.0f;
            }

            Echo("AutoDockStation initialized");
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

            _autoDockStation.Update();
            UpdateDisplay();
        }

        private void HandleCommand(string argument)
        {
            var command = argument.ToLower();

            switch (command)
            {
                case "status":
                    ShowStatus();
                    break;

                case "calldrones":
                    CallDrones();
                    break;

                default:
                    break;
            }
        }

        private void CallDrones()
        {
            var stationPbId = Me.EntityId;
            var stationGridId = Me.CubeGrid.EntityId;
            var message = $"orderdock|{stationPbId}|{stationGridId}";
            IGC.SendBroadcastMessage("AutoDock", message);
            Echo($"Broadcasting dock order to all drones (Station Grid ID: {stationGridId})");
        }

        private void ShowStatus()
        {
            var connectors = _autoDockStation.GetConnectors();
            Echo($"Total Connectors: {connectors.Count}");
            Echo($"Available: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Available)}");
            Echo($"Reserved: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Reserved)}");
            Echo($"Occupied: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Occupied)}");

            foreach (var connector in connectors)
            {
                Echo($"  {connector.Connector.CustomName}: {connector.Status}");
            }
        }

        private void UpdateDisplay()
        {
            if (_displayPanel == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== AUTO DOCK STATION ===");
            sb.AppendLine();

            var connectors = _autoDockStation.GetConnectors();
            sb.AppendLine($"Total Docking Ports: {connectors.Count}");
            sb.AppendLine($"Available: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Available)}");
            sb.AppendLine($"Reserved: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Reserved)}");
            sb.AppendLine($"Occupied: {connectors.Count(c => c.Status == AutoDockStation.ConnectorStatus.Occupied)}");
            sb.AppendLine();
            sb.AppendLine("=== DOCKING STATUS ===");

            foreach (var connectorInfo in connectors)
            {
                var status = connectorInfo.Status.ToString().ToUpper();
                var shipName = "";

                if (connectorInfo.Status == AutoDockStation.ConnectorStatus.Occupied && connectorInfo.Connector.IsConnected)
                {
                    var connectedGrid = connectorInfo.Connector.OtherConnector.CubeGrid;
                    shipName = $" - {connectedGrid.CustomName}";
                }
                else if (connectorInfo.Status == AutoDockStation.ConnectorStatus.Reserved)
                {
                    shipName = $" - Reserved for {connectorInfo.ReservedShipId}";
                }

                sb.AppendLine($"{connectorInfo.Connector.CustomName}");
                sb.AppendLine($"  Status: {status}{shipName}");
            }

            _displayPanel.WriteText(sb.ToString());
        }
    }
}

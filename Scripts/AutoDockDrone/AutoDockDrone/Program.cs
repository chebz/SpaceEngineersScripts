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
        private CustomDataConnector _customDataConnector;
        private Navigation _navigation;
        private Alignment _alignment;
        private PathfindingNavigation _pathfinderNavigation;
        private AutoDockDrone _autoDockDrone;
        private IMyProgrammableBlock _callbackProgrammableBlock;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            _navigation = new Navigation();
            _alignment = new Alignment();
            _pathfinderNavigation = new PathfindingNavigation();
            _autoDockDrone = new AutoDockDrone();

            string errorMessage;
            if (!_navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            if (!_pathfinderNavigation.Initialize(this, _customDataConnector, _navigation, _alignment, out errorMessage))
            {
                Echo($"PathfinderNavigation Error: {errorMessage}");
            }

            if (!_autoDockDrone.Initialize(this, _customDataConnector, _navigation, _alignment, _pathfinderNavigation, out errorMessage))
            {
                Echo($"AutoDockDrone Error: {errorMessage}");
            }

            _autoDockDrone.SetDockedCallback(OnAutoDockDocked);
            _autoDockDrone.SetUndockedCallback(OnAutoDockUndocked);

            _customDataConnector.Load();

            Echo("AutoDockDrone initialized");
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }

            HandleBroadcastMessages();
            _autoDockDrone.Execute();
            _customDataConnector.Update();
        }

        private void HandleBroadcastMessages()
        {
            var listener = IGC.RegisterBroadcastListener("AutoDock");
            while (listener.HasPendingMessage)
            {
                var message = listener.AcceptMessage();
                if (message.Data is string)
                {
                    var data = message.Data.ToString();
                    if (data.StartsWith("orderdock|"))
                    {
                        var parts = data.Split('|');
                        if (parts.Length >= 3)
                        {
                            long stationPbId, stationGridId;
                            if (long.TryParse(parts[1], out stationPbId) && long.TryParse(parts[2], out stationGridId))
                            {
                                var connectorName = parts.Length >= 4 ? parts[3] : "*";
                                _autoDockDrone.DockToStation(stationPbId, connectorName);
                                if (connectorName == "*")
                                {
                                    Echo($"Received dock order to station {stationGridId}");
                                }
                                else
                                {
                                    Echo($"Received dock order to station {stationGridId} ({connectorName})");
                                }
                            }
                            else
                            {
                                Echo($"Failed to parse station IDs: PB='{parts[1]}' Grid='{parts[2]}'");
                                Echo($"Message was: '{data}'");
                            }
                        }
                    }
                }
            }
        }

        private void HandleCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            var trimmed = argument.Trim();
            var spaceIndex = trimmed.IndexOf(' ');
            var command = spaceIndex >= 0 ? trimmed.Substring(0, spaceIndex).ToLower() : trimmed.ToLower();
            var commandArgs = spaceIndex >= 0 ? trimmed.Substring(spaceIndex + 1).Trim() : string.Empty;

            switch (command)
            {
                case "dock":
                    var connectorName = "*";
                    var callbackName = string.Empty;

                    if (!string.IsNullOrEmpty(commandArgs))
                    {
                        var separatorIndex = commandArgs.IndexOf(' ');
                        if (separatorIndex == -1)
                        {
                            connectorName = commandArgs;
                        }
                        else
                        {
                            connectorName = commandArgs.Substring(0, separatorIndex).Trim();
                            callbackName = commandArgs.Substring(separatorIndex + 1).Trim();
                        }

                        if (string.IsNullOrEmpty(connectorName))
                        {
                            connectorName = "*";
                        }
                    }

                    if (!string.IsNullOrEmpty(callbackName))
                    {
                        LinkCallbackProgrammableBlock(callbackName);
                    }

                    _autoDockDrone.DockToNearest(connectorName);
                    if (connectorName == "*")
                    {
                        Echo("Requesting docking to nearest station...");
                    }
                    else
                    {
                        Echo($"Requesting docking to nearest station ({connectorName})...");
                    }
                    break;

                case "undock":
                    if (!string.IsNullOrEmpty(commandArgs))
                    {
                        LinkCallbackProgrammableBlock(commandArgs);
                    }
                    _autoDockDrone.Undock();
                    Echo("Undocking...");
                    break;

                case "toggledock":
                    ToggleDock();
                    break;

                case "stop":
                    _autoDockDrone.Stop();
                    Echo("Stopping docking operations...");
                    break;

                default:
                    break;
            }
        }

        private void ToggleDock()
        {
            if (_autoDockDrone.IsDocked())
            {
                _autoDockDrone.Undock();
                Echo("Undocking...");
            }
            else if (_autoDockDrone.IsUndocked())
            {
                _autoDockDrone.DockToNearest("*");
                Echo("Requesting docking to nearest station...");
            }
            else
            {
                Echo("Cannot toggle - operation in progress");
            }
        }

        private void OnAutoDockDocked()
        {
            TriggerCallback("ad_docked");
        }

        private void OnAutoDockUndocked()
        {
            TriggerCallback("ad_undocked");
        }

        private void TriggerCallback(string command)
        {
            if (_callbackProgrammableBlock == null)
            {
                return;
            }

            if (!_callbackProgrammableBlock.IsFunctional)
            {
                return;
            }

            _callbackProgrammableBlock.TryRun(command);
        }

        private void LinkCallbackProgrammableBlock(string programmableBlockName)
        {
            var programmableBlock = FindProgrammableBlock(programmableBlockName);
            if (programmableBlock == null)
            {
                Echo($"Programmable block '{programmableBlockName}' not found");
                return;
            }

            _callbackProgrammableBlock = programmableBlock;
            Echo($"Linked programmable block '{_callbackProgrammableBlock.CustomName}'");
        }

        private IMyProgrammableBlock FindProgrammableBlock(string programmableBlockName)
        {
            if (string.IsNullOrEmpty(programmableBlockName))
            {
                return null;
            }

            var blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, block =>
                block.IsSameConstructAs(Me) &&
                block.CustomName.IndexOf(programmableBlockName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (blocks.Count == 0)
            {
                return null;
            }

            return blocks[0];
        }
    }
}

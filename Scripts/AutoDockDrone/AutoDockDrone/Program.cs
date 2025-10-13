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
        private NavigationWithCollisionAvoidance _navigationWithCollisionAvoidance;
        private AutoDockDrone _autoDockDrone;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            _navigation = new Navigation();
            _alignment = new Alignment();
            _navigationWithCollisionAvoidance = new NavigationWithCollisionAvoidance();
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

            if (!_navigationWithCollisionAvoidance.Initialize(this, _alignment, _navigation, _customDataConnector, out errorMessage))
            {
                Echo($"NavigationWithCollisionAvoidance Error: {errorMessage}");
            }

            if (!_autoDockDrone.Initialize(this, _navigation, _alignment, _navigationWithCollisionAvoidance, out errorMessage))
            {
                Echo($"AutoDockDrone Error: {errorMessage}");
            }

            _customDataConnector.Load();
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
                                _autoDockDrone.DockToStation(stationPbId);
                                Echo($"Received dock order to station {stationGridId}");
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
            var command = argument.ToLower();

            switch (command)
            {
                case "dock":
                    _autoDockDrone.DockToNearest();
                    Echo("Requesting docking to nearest station...");
                    break;

                case "undock":
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
                _autoDockDrone.DockToNearest();
                Echo("Requesting docking to nearest station...");
            }
            else
            {
                Echo("Cannot toggle - operation in progress");
            }
        }
    }
}

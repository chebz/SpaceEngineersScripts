using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI.Interfaces;
using VRage.Game;
using VRage.Collections;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;


namespace Pathfinder
{
    public class FindPathAction : IMyTerminalAction
    {
        public string Id { get { return "GetPath"; } }
        public string Icon { get; set; }
        public StringBuilder Name { get; set; }
        public StringBuilder Description { get; set; }
        public Func<Sandbox.ModAPI.IMyTerminalBlock, bool> Enabled { get; set; }
        public List<MyToolbarType> InvalidToolbarTypes { get; set; }
        public bool ValidForGroups { get; set; }
        public Action<Sandbox.ModAPI.IMyTerminalBlock> Action { get; set; }
        public Action<Sandbox.ModAPI.IMyTerminalBlock, StringBuilder> Writer { get; set; }

        public FindPathAction()
        {
            Icon = "Textures\\GUI\\Icons\\Actions\\Toggle.dds";
            Name = new StringBuilder("Get Path");
            Description = new StringBuilder("Calculate path to first waypoint");
            Enabled = (block) => true;
            InvalidToolbarTypes = new List<MyToolbarType>();
            ValidForGroups = true;
            Action = GetPath;
            Writer = GetPathWriter;
        }

        // ITerminalAction interface implementation
        public void Apply(VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
        {
            GetPath(block as Sandbox.ModAPI.IMyTerminalBlock);
        }

        public void Apply(VRage.Game.ModAPI.Ingame.IMyCubeBlock block, VRage.Collections.ListReader<TerminalActionParameter> terminalActionParameters)
        {
            GetPath(block as Sandbox.ModAPI.IMyTerminalBlock);
        }

        public bool IsEnabled(VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
        {
            return Enabled(block as Sandbox.ModAPI.IMyTerminalBlock);
        }

        public void WriteValue(VRage.Game.ModAPI.Ingame.IMyCubeBlock block, StringBuilder appendTo)
        {
            GetPathWriter(block as Sandbox.ModAPI.IMyTerminalBlock, appendTo);
        }

        private void GetPath(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            var remoteControl = block as Sandbox.ModAPI.IMyRemoteControl;
            if (remoteControl == null) return;

            // Get the navigation component from the remote control
            var navigationComponent = remoteControl.Components.Get<NavigationComponent>();
            if (navigationComponent != null)
            {
                // Get the first waypoint as destination
                var waypoints = new List<Sandbox.ModAPI.Ingame.MyWaypointInfo>();
                remoteControl.GetWaypointInfo(waypoints);
                
                if (waypoints.Count > 0)
                {
                    var destination = waypoints[0].Coords;
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Calculating path to: {destination}");
                    
                    // The pathfinding will be handled by the NavigationComponent's UpdateBeforeSimulation100 method
                    // This action just triggers the process
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", "No waypoints set in remote control");
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", "NavigationComponent not found on this remote control");
            }
        }

        private void GetPathWriter(Sandbox.ModAPI.IMyTerminalBlock block, StringBuilder output)
        {
            output.Append("Calculate path to first waypoint");
        }
    }
}

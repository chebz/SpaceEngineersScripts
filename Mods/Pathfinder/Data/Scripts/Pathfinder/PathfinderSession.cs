using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.Game.Entities;

namespace Pathfinder
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PathfinderSession : MySessionComponentBase
    {
        public override void LoadData()
        {
            MyAPIGateway.Utilities.ShowMessage("Pathfinder", "Pathfinder mod loaded.");
        }

        public override void BeforeStart()
        {
            // List not supported
            // var navPathProperty = MyAPIGateway.TerminalControls.CreateProperty<List<Vector3D>, IMyRemoteControl>("NavPath");
            // navPathProperty.Getter = (rcBlock) => GetNavPath(rcBlock);
            // navPathProperty.Enabled = (rcBlock) => true;
            // navPathProperty.Visible = (rcBlock) => false;            
            // MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(navPathProperty);
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"NavPath property added: {navPathProperty.TypeName}");

            var recomputePathAction = MyAPIGateway.TerminalControls.CreateAction<IMyRemoteControl>("RecomputePath");
            recomputePathAction.Action = (rcBlock) => RecomputePath(rcBlock);
            recomputePathAction.Enabled = (rcBlock) => true;
            MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(recomputePathAction);

            var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("RecomputePath");
            c.Title = MyStringId.GetOrCompute("Recompute Path");
            c.Tooltip = MyStringId.GetOrCompute("Recompute the path to the current destination");
            c.SupportsMultipleBlocks = true;
            c.Visible = (rcBlock) => true;
            c.Action = (rcBlock) => RecomputePath(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(c);
        }

        private void RecomputePath(IMyTerminalBlock rc)
        {
             var gameLogicComponent = rc.Components.Get<MyGameLogicComponent>();
            if (gameLogicComponent == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"GameLogicComponent not found");
                return;
            }
            var navigationComponent = gameLogicComponent.GetAs<NavigationComponent>();
            if (navigationComponent == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"NavigationComponent not found");
                return;
            }
            navigationComponent.NeedsRecompute = true;
        }

    }
}

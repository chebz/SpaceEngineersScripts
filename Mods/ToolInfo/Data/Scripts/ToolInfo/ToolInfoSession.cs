using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRageRender;
using VRage.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders;

namespace ToolInfo
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    class ToolInfoSession : MySessionComponentBase
    {
        public override void LoadData()
        {
            MyAPIGateway.Utilities.ShowMessage("ToolInfo", "ToolInfo mod loaded.");
        }

        public override void BeforeStart()
        {
            var canUseWelderProperty = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyShipWelder>("CanUse");
            canUseWelderProperty.Getter = (toolBlock) => CanUse(toolBlock);
            canUseWelderProperty.Enabled = (toolBlock) => true;
            canUseWelderProperty.Visible = (toolBlock) => false;            
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(canUseWelderProperty);

            var canUseGrinderProperty = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyShipGrinder>("CanUse");
            canUseGrinderProperty.Getter = (toolBlock) => CanUse(toolBlock);
            canUseGrinderProperty.Enabled = (toolBlock) => true;
            canUseGrinderProperty.Visible = (toolBlock) => false;            
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(canUseGrinderProperty);

            // EnableToolInfo property for welders
            var enableToolInfoWelderProperty = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyShipWelder>("EnableToolInfo");
            enableToolInfoWelderProperty.Getter = (toolBlock) => GetEnableToolInfo(toolBlock);
            enableToolInfoWelderProperty.Setter = (toolBlock, value) => SetEnableToolInfo(toolBlock, value);
            enableToolInfoWelderProperty.Enabled = (toolBlock) => true;
            enableToolInfoWelderProperty.Visible = (toolBlock) => true;

            // EnableToolInfo checkbox for welders
            var enableToolInfoWelderCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("EnableToolInfo");
            enableToolInfoWelderCheckbox.Title = MyStringId.GetOrCompute("Enable ToolInfo");
            enableToolInfoWelderCheckbox.Tooltip = MyStringId.GetOrCompute("Enable or disable ToolInfo functionality for this tool");
            enableToolInfoWelderCheckbox.SupportsMultipleBlocks = true;
            enableToolInfoWelderCheckbox.Visible = (b) => true;
            enableToolInfoWelderCheckbox.Enabled = (b) => true;
            enableToolInfoWelderCheckbox.Getter = (b) => GetEnableToolInfo(b);
            enableToolInfoWelderCheckbox.Setter = (b, v) => SetEnableToolInfo(b, v);
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(enableToolInfoWelderCheckbox);

            // EnableToolInfo property for grinders
            var enableToolInfoGrinderProperty = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyShipGrinder>("EnableToolInfo");
            enableToolInfoGrinderProperty.Getter = (toolBlock) => GetEnableToolInfo(toolBlock);
            enableToolInfoGrinderProperty.Setter = (toolBlock, value) => SetEnableToolInfo(toolBlock, value);
            enableToolInfoGrinderProperty.Enabled = (toolBlock) => true;
            enableToolInfoGrinderProperty.Visible = (toolBlock) => true;

            // EnableToolInfo checkbox for grinders
            var enableToolInfoGrinderCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipGrinder>("EnableToolInfo");
            enableToolInfoGrinderCheckbox.Title = MyStringId.GetOrCompute("Enable ToolInfo");
            enableToolInfoGrinderCheckbox.Tooltip = MyStringId.GetOrCompute("Enable or disable ToolInfo functionality for this tool");
            enableToolInfoGrinderCheckbox.SupportsMultipleBlocks = true;
            enableToolInfoGrinderCheckbox.Visible = (b) => true;
            enableToolInfoGrinderCheckbox.Enabled = (b) => true;
            enableToolInfoGrinderCheckbox.Getter = (b) => GetEnableToolInfo(b);
            enableToolInfoGrinderCheckbox.Setter = (b, v) => SetEnableToolInfo(b, v);
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(enableToolInfoGrinderCheckbox);

            // Debug Draw checkbox for welders
            var debugDrawWelderCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("ToolInfoDebugDraw");
            debugDrawWelderCheckbox.Title = MyStringId.GetOrCompute("ToolInfo Debug Draw");
            debugDrawWelderCheckbox.Tooltip = MyStringId.GetOrCompute("Show or hide debug drawing for this tool");
            debugDrawWelderCheckbox.SupportsMultipleBlocks = true;
            debugDrawWelderCheckbox.Visible = (b) => true;
            debugDrawWelderCheckbox.Enabled = (b) => true;
            debugDrawWelderCheckbox.Getter = (b) => GetShowDebugDraw(b);
            debugDrawWelderCheckbox.Setter = (b, v) => SetShowDebugDraw(b, v);
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(debugDrawWelderCheckbox);

            // Debug Draw checkbox for grinders
            var debugDrawGrinderCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipGrinder>("ToolInfoDebugDraw");
            debugDrawGrinderCheckbox.Title = MyStringId.GetOrCompute("ToolInfo Debug Draw");
            debugDrawGrinderCheckbox.Tooltip = MyStringId.GetOrCompute("Show or hide debug drawing for this tool");
            debugDrawGrinderCheckbox.SupportsMultipleBlocks = true;
            debugDrawGrinderCheckbox.Visible = (b) => true;
            debugDrawGrinderCheckbox.Enabled = (b) => true;
            debugDrawGrinderCheckbox.Getter = (b) => GetShowDebugDraw(b);
            debugDrawGrinderCheckbox.Setter = (b, v) => SetShowDebugDraw(b, v);
            MyAPIGateway.TerminalControls.AddControl<IMyShipGrinder>(debugDrawGrinderCheckbox);

        }

        protected override void UnloadData()
        {
            base.UnloadData();
        }

        private static bool CanUse(IMyTerminalBlock block)
        {
            if (block is IMyShipToolBase)
            {
                var glComponent = block.Components.Get<MyGameLogicComponent>();
                var toolComponent = glComponent as ToolInfoComponent;
                
                if (toolComponent != null)
                {
                    return toolComponent.CanUse();
                }
            }
            return false;
        }

        private static bool GetEnableToolInfo(IMyTerminalBlock block)
        {
            if (block is IMyShipToolBase)
            {
                var glComponent = block.Components.Get<MyGameLogicComponent>();
                var toolComponent = glComponent as ToolInfoComponent;
                bool result = toolComponent?.EnableToolInfo ?? false;
                return result;
            }
            return false;
        }

        private static void SetEnableToolInfo(IMyTerminalBlock block, bool enable)
        {
            if (block is IMyShipToolBase)
            {
                var glComponent = block.Components.Get<MyGameLogicComponent>();
                var toolComponent = glComponent as ToolInfoComponent;
                
                if (toolComponent != null)
                {
                    toolComponent.EnableToolInfo = enable;
                }
            }
        }

        private static bool GetShowDebugDraw(IMyTerminalBlock block)
        {
            if (block is IMyShipToolBase)
            {
                var glComponent = block.Components.Get<MyGameLogicComponent>();
                var toolComponent = glComponent as ToolInfoComponent;
                bool result = toolComponent?.ShowDebugDraw ?? false;
                return result;
            }
            return false;
        }

        private static void SetShowDebugDraw(IMyTerminalBlock block, bool enable)
        {
            if (block is IMyShipToolBase)
            {
                var glComponent = block.Components.Get<MyGameLogicComponent>();
                var toolComponent = glComponent as ToolInfoComponent;
                
                if (toolComponent != null)
                {
                    toolComponent.ShowDebugDraw = enable;
                }
            }
        }
    }

}
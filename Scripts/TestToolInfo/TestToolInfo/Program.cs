using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region Fields
        private List<IMyShipToolBase> _tools;
        private IMyTextSurface _displayPanel;
        private int _updateCounter = 0;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10; // Update every 10 ticks
            Initialize();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                UpdateToolStatus();
            }
            catch (Exception e)
            {
                Echo($"Error: {e.Message}");
            }
        }

        private void Initialize()
        {
            // Get all tools (welders and grinders) on the grid
            _tools = new List<IMyShipToolBase>();
            GridTerminalSystem.GetBlocksOfType(_tools);
            _tools = _tools.Where(t => t.IsSameConstructAs(Me)).ToList();

            // Get display panel (LCD)
            _displayPanel = Me.GetSurface(0);

            Echo($"Initialized: {_tools.Count} tools");
        }

        private void UpdateToolStatus()
        {
            _updateCounter++;
            
            bool anyToolWorking = false;
            int workingCount = 0;
            int totalTools = _tools.Count;

            // Check each tool's CanUse property
            foreach (var tool in _tools)
            {
                try
                {
                    List<ITerminalProperty> properties = new List<ITerminalProperty>();
                    tool.GetProperties(properties, null);
                    var propertiesString = string.Join(", ", properties.Select(p => p.Id.ToString()));
                    Echo($"Properties: {propertiesString}");
                    // Get the CanUse property from the ToolInfo mod
                    var canUseProperty = tool.GetProperty("CanUse");
                    if (canUseProperty != null)
                    {
                        var toolCanUse = canUseProperty.AsBool().GetValue(tool);
                        if (toolCanUse)
                        {
                            anyToolWorking = true;
                            workingCount++;
                            
                        }
                        UpdateToolLight(tool, toolCanUse);
                    }
                    else
                    {
                        Echo($"Error getting CanUse property for tool {tool.CustomName}");
                    }
                }
                catch (Exception e)
                {
                    Echo($"Error checking tool {tool.CustomName}: {e.Message}");
                }
            }

            // Update display panel
            UpdateDisplay(anyToolWorking, workingCount, totalTools);
        }

        private void UpdateToolLight(IMyShipToolBase tool, bool canUse)
        {
            // Get the light name from the tool's CustomData
            string lightName = tool.CustomData?.Trim();
            
            if (string.IsNullOrEmpty(lightName))
            {
                Echo($"Tool {tool.CustomName} has no light name in CustomData");
                return;
            }

            // Find the light by name
            var light = GridTerminalSystem.GetBlockWithName(lightName) as IMyLightingBlock;
            
            if (light == null)
            {
                Echo($"Light '{lightName}' not found for tool {tool.CustomName}");
                return;
            }

            // Update the light based on tool status
            try
            {
                Color lightColor = canUse ? Color.Green : Color.Red;
                float intensity = canUse ? 2.0f : 0.5f;
                
                light.Color = lightColor;
                light.Intensity = intensity;
                light.Enabled = true;
                
                Echo($"Updated light '{lightName}' for tool {tool.CustomName}: {(canUse ? "GREEN" : "RED")}");
            }
            catch (Exception e)
            {
                Echo($"Error updating light '{lightName}' for tool {tool.CustomName}: {e.Message}");
            }
        }

        private void UpdateDisplay(bool canUse, int workingCount, int totalTools)
        {
            if (_displayPanel == null) return;

            var displayText = new System.Text.StringBuilder();
            displayText.AppendLine("=== TOOL STATUS ===");
            displayText.AppendLine($"Update: {_updateCounter}");
            displayText.AppendLine($"Total Tools: {totalTools}");
            displayText.AppendLine($"Working: {workingCount}");
            displayText.AppendLine($"Status: {(canUse ? "CAN USE" : "CANNOT USE")}");
            displayText.AppendLine();
            displayText.AppendLine("=== TOOLS ===");

            foreach (var tool in _tools)
            {
                try
                {
                    var canUseProperty = tool.GetProperty("CanUse");
                    if (canUseProperty != null)
                    {
                        var toolCanUse = canUseProperty.AsBool().GetValue(tool);
                        displayText.AppendLine($"{tool.CustomName}: {(toolCanUse ? "CAN USE" : "CANNOT USE")}");
                    }
                    else
                    {
                        displayText.AppendLine($"{tool.CustomName}: NO PROPERTY");
                    }
                }
                catch (Exception e)
                {
                    displayText.AppendLine($"{tool.CustomName}: ERROR - {e.Message}");
                }
            }

            _displayPanel.WriteText(displayText.ToString());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI.Interfaces.Terminal;
using Pathfinder.OctreeAStar;
using VRage.ModAPI;

namespace Pathfinder
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PathfinderSession : MySessionComponentBase
    {
        public override void LoadData()
        {
            Utils.InitializeLog();

            MyAPIGateway.Utilities.ShowMessage("Pathfinder", "Pathfinder loaded.");
            OctreeAStarSettings.Instance.Load();
        }

        protected override void UnloadData()
        {
            Utils.ShutdownLog();
        }

        public override void BeforeStart()
        {
            
            // Separator
            var separator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyRemoteControl>("");
            separator.SupportsMultipleBlocks = true;
            separator.Visible = (rcBlock) => true;
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(separator);

            // Path points property (hidden)
            var pathProperty = MyAPIGateway.TerminalControls.CreateProperty<string, IMyRemoteControl>("PathfinderPath");
            pathProperty.SupportsMultipleBlocks = true;
            pathProperty.Visible = (rcBlock) => false;
            pathProperty.Enabled = (rcBlock) => true;
            pathProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.GetPathPointsString() : string.Empty;
            };
            pathProperty.Setter = (rcBlock, value) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(pathProperty);

            // Path status property (hidden)
            var pathStatusProperty = MyAPIGateway.TerminalControls.CreateProperty<string, IMyRemoteControl>("PathfinderStatus");
            pathStatusProperty.SupportsMultipleBlocks = true;
            pathStatusProperty.Visible = (rcBlock) => false;
            pathStatusProperty.Enabled = (rcBlock) => true;
            pathStatusProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.GetPathStatusString() : string.Empty;
            };
            pathStatusProperty.Setter = (rcBlock, value) => { };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(pathStatusProperty);

            // Goal property (visible)
            var destinationProperty = MyAPIGateway.TerminalControls.CreateProperty<Vector3D?, IMyRemoteControl>("PathfinderDestination");
            destinationProperty.SupportsMultipleBlocks = true;
            destinationProperty.Visible = (rcBlock) => false;
            destinationProperty.Enabled = (rcBlock) => true;
            destinationProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.Destination : null;
            };
            destinationProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    if (value.HasValue)
                    {
                        nav.Destination = value.Value;
                    }
                    else
                    {
                        nav.Destination = null;
                    }
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(destinationProperty);

            // GPS combo
            var gpsCombo = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyRemoteControl>("PathfinderGpsCombo");
            gpsCombo.Title = MyStringId.GetOrCompute("Destination GPS");
            gpsCombo.Tooltip = MyStringId.GetOrCompute("Select a GPS coordinate for the pathfinding destination");
            gpsCombo.SupportsMultipleBlocks = true;
            gpsCombo.Visible = (rcBlock) => true;
            gpsCombo.Enabled = (rcBlock) => true;
            gpsCombo.ComboBoxContent = (items) =>
            {
                items.Clear();

                var gpsList = Utils.GetGpsList();
                for (int i = 0; i < gpsList.Count; i++)
                {
                    var gps = gpsList[i];
                    if (gps == null)
                    {
                        continue;
                    }

                    var name = string.IsNullOrWhiteSpace(gps.Name) ? "Unnamed GPS" : gps.Name;
                    items.Add(new MyTerminalControlComboBoxItem
                    {
                        Key = gps.Hash,
                        Value = MyStringId.GetOrCompute(name)
                    });
                }
            };
            gpsCombo.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav == null)
                {
                    return -1;
                }
                return Utils.ComputeGpsKey(nav.Destination);
            };
            gpsCombo.Setter = (rcBlock, key) =>
            {   
                destinationProperty.Setter(rcBlock, Utils.GetGpsFromKey(key));
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(gpsCombo);

            // Min Altitude property (visible)
            var minAltitudeProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderMinAltitude");
            minAltitudeProperty.SupportsMultipleBlocks = true;
            minAltitudeProperty.Visible = (rcBlock) => true;
            minAltitudeProperty.Enabled = (rcBlock) => true;
            minAltitudeProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.MinAltitude : 0.0;
            };
            minAltitudeProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MinAltitude = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(minAltitudeProperty);

            var minAltitudeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderMinAltitudeSlider");
            minAltitudeSlider.Title = MyStringId.GetOrCompute("Minimum Altitude");
            minAltitudeSlider.Tooltip = MyStringId.GetOrCompute("Minimum altitude (meters) the pathfinder will maintain");
            minAltitudeSlider.SupportsMultipleBlocks = true;
            minAltitudeSlider.Visible = (rcBlock) => true;
            minAltitudeSlider.Enabled = (rcBlock) => true;
            minAltitudeSlider.SetLimits(0f, 5000f);
            minAltitudeSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.MinAltitude : 0f;
            };
            minAltitudeSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MinAltitude = value;
                }
            };
            minAltitudeSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var altitude = nav != null ? nav.MinAltitude : 0.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", altitude);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(minAltitudeSlider);

            var maxAltitudeProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderMaxAltitude");
            maxAltitudeProperty.SupportsMultipleBlocks = true;
            maxAltitudeProperty.Visible = (rcBlock) => true;
            maxAltitudeProperty.Enabled = (rcBlock) => true;
            maxAltitudeProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.MaxAltitude : 0.0;
            };
            maxAltitudeProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxAltitude = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxAltitudeProperty);

            var maxAltitudeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderMaxAltitudeSlider");
            maxAltitudeSlider.Title = MyStringId.GetOrCompute("Maximum Altitude");
            maxAltitudeSlider.Tooltip = MyStringId.GetOrCompute("Maximum altitude (meters) the pathfinder will maintain");
            maxAltitudeSlider.SupportsMultipleBlocks = true;
            maxAltitudeSlider.Visible = (rcBlock) => true;
            maxAltitudeSlider.Enabled = (rcBlock) => true;
            maxAltitudeSlider.SetLimits(0f, 5000f);
            maxAltitudeSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.MaxAltitude : 0f;
            };
            maxAltitudeSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxAltitude = value;
                }
            };
            maxAltitudeSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var altitude = nav != null ? nav.MaxAltitude : 0.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", altitude);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxAltitudeSlider);

            var recomputePathAction = MyAPIGateway.TerminalControls.CreateAction<IMyRemoteControl>("RecomputePath");
            recomputePathAction.Name = new StringBuilder("Recompute Path");
            recomputePathAction.Action = (rcBlock) => RecomputePath(rcBlock);
            recomputePathAction.Enabled = (rcBlock) => true;
            recomputePathAction.Writer = (rcBlock, builder) => builder.Append("Recompute Path");
            MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(recomputePathAction);

            var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("RecomputePath");
            c.Title = MyStringId.GetOrCompute("Recompute Path");
            c.Tooltip = MyStringId.GetOrCompute("Recompute the path to the current destination");
            c.SupportsMultipleBlocks = true;
            c.Visible = (rcBlock) => true;
            c.Action = (rcBlock) => RecomputePath(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(c);

            // Debug actions
            // var stepPathAction = MyAPIGateway.TerminalControls.CreateAction<IMyRemoteControl>("StepPath");
            // stepPathAction.Name = new StringBuilder("Step Pathfinding");
            // stepPathAction.Action = (rcBlock) => StepPath(rcBlock);
            // stepPathAction.Enabled = (rcBlock) => true;
            // stepPathAction.Writer = (rcBlock, builder) => builder.Append("Step Pathfinding");
            // MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(stepPathAction);

            // var stepBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("Step");
            // stepBtn.Title = MyStringId.GetOrCompute("Step");
            // stepBtn.Tooltip = MyStringId.GetOrCompute("Advance the pathfinding by one step");
            // stepBtn.SupportsMultipleBlocks = true;
            // stepBtn.Visible = (rcBlock) => true;
            // stepBtn.Action = (rcBlock) => StepPath(rcBlock);
            // MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(stepBtn);

            var clearPathAction = MyAPIGateway.TerminalControls.CreateAction<IMyRemoteControl>("ClearPath");
            clearPathAction.Name = new StringBuilder("Clear Path");
            clearPathAction.Action = (rcBlock) => ClearPath(rcBlock);
            clearPathAction.Enabled = (rcBlock) => true;
            clearPathAction.Writer = (rcBlock, builder) => builder.Append("Clear Path");
            MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(clearPathAction);

            var clearBtn = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("ClearPathButton");
            clearBtn.Title = MyStringId.GetOrCompute("Clear Path");
            clearBtn.Tooltip = MyStringId.GetOrCompute("Stop pathfinding and clear the current path");
            clearBtn.SupportsMultipleBlocks = true;
            clearBtn.Visible = (rcBlock) => true;
            clearBtn.Action = (rcBlock) => ClearPath(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(clearBtn);

            var clearPathButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("ClearPath");
            clearPathButton.Title = MyStringId.GetOrCompute("Clear Path");
            clearPathButton.Tooltip = MyStringId.GetOrCompute("Clear the path");
            clearPathButton.SupportsMultipleBlocks = true;
            clearPathButton.Visible = (rcBlock) => true;
            clearPathButton.Action = (rcBlock) => ClearPath(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(clearPathButton);

            // Debug
            // var loadSettingsButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("LoadPathfinderSettings");
            // loadSettingsButton.Title = MyStringId.GetOrCompute("Load Settings");
            // loadSettingsButton.Tooltip = MyStringId.GetOrCompute("Load OctreeAStar settings from PathfinderSettings.xml");
            // loadSettingsButton.SupportsMultipleBlocks = true;
            // loadSettingsButton.Visible = (rcBlock) => true;
            // loadSettingsButton.Action = (rcBlock) => OctreeAStarSettings.Instance.Load();
            // MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(loadSettingsButton);
        }

        private void RecomputePath(IMyTerminalBlock rc)
        {
            string error;
            var navigationComponent = GetNavigationComponent(rc, out error);
            if (navigationComponent == null)
            {
                Utils.Log(MyLogSeverity.Error, "Pathfinder: {0}", error ?? "Pathfinder component not found");
                return;
            }
            navigationComponent.NeedsRecompute = true;
        }

        private void StepPath(IMyTerminalBlock rc)
        {
            string error;
            var navigationComponent = GetNavigationComponent(rc, out error);
            if (navigationComponent == null)
            {
                Utils.Log(MyLogSeverity.Error, "Pathfinder: {0}", error ?? "Pathfinder component not found");
                return;
            }
            navigationComponent.Step();
        }

        private void ClearPath(IMyTerminalBlock rc)
        {
            string error;
            var navigationComponent = GetNavigationComponent(rc, out error);
            if (navigationComponent == null)
            {
                Utils.Log(MyLogSeverity.Error, "Pathfinder: {0}", error ?? "Pathfinder component not found");
                return;
            }
            navigationComponent.ClearPath();
        }

        private NavigationComponent GetNavigationComponent(IMyTerminalBlock rc, out string errorMessage)
        {
            errorMessage = null;
            var gameLogicComponent = rc.Components.Get<MyGameLogicComponent>();
            if (gameLogicComponent == null)
            {
                errorMessage = "GameLogicComponent not found";
                return null;
            }
            var navigationComponent = gameLogicComponent.GetAs<NavigationComponent>();
            if (navigationComponent == null)
            {
                errorMessage = "NavigationComponent not found";
            }
            return navigationComponent;
        }
    }
}



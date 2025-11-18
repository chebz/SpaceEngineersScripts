using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

            // Current waypoint index property (hidden)
            var currentWaypointIndexProperty = MyAPIGateway.TerminalControls.CreateProperty<int, IMyRemoteControl>("CurrentWaypointIndex");
            currentWaypointIndexProperty.SupportsMultipleBlocks = true;
            currentWaypointIndexProperty.Visible = (rcBlock) => false;
            currentWaypointIndexProperty.Enabled = (rcBlock) => true;
            currentWaypointIndexProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.CurrentWaypointIndex : -1;
            };
            currentWaypointIndexProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.SetCurrentWaypointIndex(value);
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(currentWaypointIndexProperty);

            // Dynamic path refinement path property (hidden)
            var dprPathProperty = MyAPIGateway.TerminalControls.CreateProperty<string, IMyRemoteControl>("DPRPath");
            dprPathProperty.SupportsMultipleBlocks = true;
            dprPathProperty.Visible = (rcBlock) => false;
            dprPathProperty.Enabled = (rcBlock) => true;
            dprPathProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.GetRefinedPathString() : string.Empty;
            };
            dprPathProperty.Setter = (rcBlock, value) =>
            {
                // Intentionally left blank - refined path is managed internally
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(dprPathProperty);

            // Destination property (hidden)
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

            var destinationNameProperty = MyAPIGateway.TerminalControls.CreateProperty<string, IMyRemoteControl>("PathfinderDestinationName");
            destinationNameProperty.SupportsMultipleBlocks = true;
            destinationNameProperty.Visible = (rcBlock) => false;
            destinationNameProperty.Enabled = (rcBlock) => true;
            destinationNameProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.DestinationName : string.Empty;
            };
            destinationNameProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav == null)
                {
                    return;
                }
                nav.SetDestinationNameFromTerminal(value);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(destinationNameProperty);

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
                var gpsList = Utils.GetGpsList();
                var match = gpsList.FirstOrDefault(g => g != null && g.Hash == key);
                destinationProperty.Setter(rcBlock, match != null ? (Vector3D?)match.Coords : null);
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.SetDestinationNameFromTerminal(match?.Name);
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(gpsCombo);

            var destinationToleranceProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderDestinationTolerance");
            destinationToleranceProperty.SupportsMultipleBlocks = true;
            destinationToleranceProperty.Visible = (rcBlock) => true;
            destinationToleranceProperty.Enabled = (rcBlock) => true;
            destinationToleranceProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.DestinationTolerance : 50.0;
            };
            destinationToleranceProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.DestinationTolerance = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(destinationToleranceProperty);

            var destinationToleranceSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderDestinationToleranceSlider");
            destinationToleranceSlider.Title = MyStringId.GetOrCompute("Destination Tolerance");
            destinationToleranceSlider.Tooltip = MyStringId.GetOrCompute("Distance within which destination changes are ignored");
            destinationToleranceSlider.SupportsMultipleBlocks = true;
            destinationToleranceSlider.Visible = (rcBlock) => true;
            destinationToleranceSlider.Enabled = (rcBlock) => true;
            destinationToleranceSlider.SetLimits(0f, 5000f);
            destinationToleranceSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.DestinationTolerance : 50f;
            };
            destinationToleranceSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.DestinationTolerance = value;
                }
            };
            destinationToleranceSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var tolerance = nav != null ? nav.DestinationTolerance : 50.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", tolerance);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(destinationToleranceSlider);

            var minDistanceProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderMinDistanceFromDestination");
            minDistanceProperty.SupportsMultipleBlocks = true;
            minDistanceProperty.Visible = (rcBlock) => true;
            minDistanceProperty.Enabled = (rcBlock) => true;
            minDistanceProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.MinDistanceFromDestination : 0.0;
            };
            minDistanceProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MinDistanceFromDestination = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(minDistanceProperty);

            var minDistanceSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderMinDistanceFromDestinationSlider");
            minDistanceSlider.Title = MyStringId.GetOrCompute("Min Dist From Dest");
            minDistanceSlider.Tooltip = MyStringId.GetOrCompute("Inner radius to skip when searching for destination");
            minDistanceSlider.SupportsMultipleBlocks = true;
            minDistanceSlider.Visible = (rcBlock) => true;
            minDistanceSlider.Enabled = (rcBlock) => true;
            minDistanceSlider.SetLimits(0f, 5000f);
            minDistanceSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.MinDistanceFromDestination : 0f;
            };
            minDistanceSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MinDistanceFromDestination = value;
                }
            };
            minDistanceSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var distance = nav != null ? nav.MinDistanceFromDestination : 0.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", distance);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(minDistanceSlider);

            var maxDistanceProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderMaxDistanceFromDestination");
            maxDistanceProperty.SupportsMultipleBlocks = true;
            maxDistanceProperty.Visible = (rcBlock) => true;
            maxDistanceProperty.Enabled = (rcBlock) => true;
            maxDistanceProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.MaxDistanceFromDestination : 50.0;
            };
            maxDistanceProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxDistanceFromDestination = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxDistanceProperty);

            var maxDistanceSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderMaxDistanceFromDestinationSlider");
            maxDistanceSlider.Title = MyStringId.GetOrCompute("Max Dist From Dest");
            maxDistanceSlider.Tooltip = MyStringId.GetOrCompute("Outer radius to search for a reachable destination");
            maxDistanceSlider.SupportsMultipleBlocks = true;
            maxDistanceSlider.Visible = (rcBlock) => true;
            maxDistanceSlider.Enabled = (rcBlock) => true;
            maxDistanceSlider.SetLimits(0f, 10000f);
            maxDistanceSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.MaxDistanceFromDestination : 50f;
            };
            maxDistanceSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxDistanceFromDestination = value;
                }
            };
            maxDistanceSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var distance = nav != null ? nav.MaxDistanceFromDestination : 50.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", distance);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxDistanceSlider);

            // Max RDP Distance From Destination property (visible)
            var maxRdpDistanceProperty = MyAPIGateway.TerminalControls.CreateProperty<double, IMyRemoteControl>("PathfinderMaxRdpDistanceFromDestination");
            maxRdpDistanceProperty.SupportsMultipleBlocks = true;
            maxRdpDistanceProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.MaxRdpDistanceFromDestination : 50.0;
            };
            maxRdpDistanceProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxRdpDistanceFromDestination = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxRdpDistanceProperty);

            var maxRdpDistanceSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyRemoteControl>("PathfinderMaxRdpDistanceFromDestinationSlider");
            maxRdpDistanceSlider.Title = MyStringId.GetOrCompute("Max RDP Dist From Dest");
            maxRdpDistanceSlider.Tooltip = MyStringId.GetOrCompute("Outer radius to search for a reachable destination during dynamic path refinement");
            maxRdpDistanceSlider.SupportsMultipleBlocks = true;
            maxRdpDistanceSlider.Visible = (rcBlock) => true;
            maxRdpDistanceSlider.Enabled = (rcBlock) => true;
            maxRdpDistanceSlider.SetLimits(0f, 10000f);
            maxRdpDistanceSlider.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? (float)nav.MaxRdpDistanceFromDestination : 50f;
            };
            maxRdpDistanceSlider.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.MaxRdpDistanceFromDestination = value;
                }
            };
            maxRdpDistanceSlider.Writer = (rcBlock, builder) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                var distance = nav != null ? nav.MaxRdpDistanceFromDestination : 50.0;
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:N1} m", distance);
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(maxRdpDistanceSlider);

            // Enable Dynamic Path Refinement property (visible)
            var enableDynamicPathRefinementProperty = MyAPIGateway.TerminalControls.CreateProperty<bool, IMyRemoteControl>("PathfinderEnableDynamicPathRefinement");
            enableDynamicPathRefinementProperty.SupportsMultipleBlocks = true;
            enableDynamicPathRefinementProperty.Visible = (rcBlock) => true;
            enableDynamicPathRefinementProperty.Enabled = (rcBlock) => true;
            enableDynamicPathRefinementProperty.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.EnableDynamicPathRefinement : true;
            };
            enableDynamicPathRefinementProperty.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.EnableDynamicPathRefinement = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(enableDynamicPathRefinementProperty);

            var enableDynamicPathRefinementCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyRemoteControl>("PathfinderEnableDynamicPathRefinementCheckbox");
            enableDynamicPathRefinementCheckbox.Title = MyStringId.GetOrCompute("Enable Dynamic Path Refinement");
            enableDynamicPathRefinementCheckbox.Tooltip = MyStringId.GetOrCompute("If enabled, uses dynamic obstacle avoidance. If disabled, DPR path goes directly to next master path waypoint.");
            enableDynamicPathRefinementCheckbox.SupportsMultipleBlocks = true;
            enableDynamicPathRefinementCheckbox.Visible = (rcBlock) => true;
            enableDynamicPathRefinementCheckbox.Enabled = (rcBlock) => true;
            enableDynamicPathRefinementCheckbox.Getter = (rcBlock) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                return nav != null ? nav.EnableDynamicPathRefinement : true;
            };
            enableDynamicPathRefinementCheckbox.Setter = (rcBlock, value) =>
            {
                string error;
                var nav = GetNavigationComponent(rcBlock, out error);
                if (nav != null)
                {
                    nav.EnableDynamicPathRefinement = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(enableDynamicPathRefinementCheckbox);

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

            var clearPathButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("ClearPath");
            clearPathButton.Title = MyStringId.GetOrCompute("Clear Path");
            clearPathButton.Tooltip = MyStringId.GetOrCompute("Clear the path");
            clearPathButton.SupportsMultipleBlocks = true;
            clearPathButton.Visible = (rcBlock) => true;
            clearPathButton.Action = (rcBlock) => ClearPath(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(clearPathButton);

            var resetSettingsAction = MyAPIGateway.TerminalControls.CreateAction<IMyRemoteControl>("ResetSettings");
            resetSettingsAction.Name = new StringBuilder("Reset Settings");
            resetSettingsAction.Action = (rcBlock) => ResetSettings(rcBlock);
            resetSettingsAction.Enabled = (rcBlock) => true;
            resetSettingsAction.Writer = (rcBlock, builder) => builder.Append("Reset Settings");
            MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(resetSettingsAction);

            var resetSettingsButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRemoteControl>("ResetSettings");
            resetSettingsButton.Title = MyStringId.GetOrCompute("Reset Settings");
            resetSettingsButton.Tooltip = MyStringId.GetOrCompute("Reset OctreeAStar settings to default values");
            resetSettingsButton.SupportsMultipleBlocks = true;
            resetSettingsButton.Visible = (rcBlock) => true;
            resetSettingsButton.Action = (rcBlock) => ResetSettings(rcBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(resetSettingsButton);

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

        private void ResetSettings(IMyTerminalBlock rc)
        {
            OctreeAStarSettings.Instance.ResetToDefaults();
            Utils.ShowHudMessage("Pathfinder settings reset to defaults");
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



using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRage.Game.ModAPI;
using VRageRender;
using VRage.Utils;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.ObjectBuilders;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.Entities;

namespace Pathfinder
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl), false)]
    public class NavigationComponent : MyGameLogicComponent
    {
        private bool _initialized = false;
        private Sandbox.ModAPI.IMyRemoteControl _remoteControl;
        private Grid _grid;
        private List<Vector3D> _currentPath = new List<Vector3D>();
        private bool _needsRecompute = false;
        private Vector3D? _currentDestination = null;

        static Color colorGreen = Color.Green;
        static Color colorRed = Color.Red;

        public List<Vector3D> CurrentPath 
        {
            get { return _currentPath; }
        }

        public bool NeedsRecompute
        {
            get { return _needsRecompute; }
            set { _needsRecompute = value; }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_initialized)
            {
                if (Entity is Sandbox.ModAPI.IMyRemoteControl)
                {
                    _remoteControl = (Sandbox.ModAPI.IMyRemoteControl)Entity;
                    
                    // Calculate node size based on grid bounding sphere
                    var grid = _remoteControl.CubeGrid;
                    var boundingSphere = grid.WorldVolume;
                    MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Bounding sphere: {boundingSphere.Radius}");

                    // var radius = Math.Max(boundingSphere.Radius * 2, 10);
                    var radius = boundingSphere.Radius * 2;
                    _grid = new Grid(radius, _remoteControl.CubeGrid);
                    // _grid = new Grid(10, grid);
                    _initialized = true;
                    
                }
                return;
            }

            FindPath();

            DrawPath();
        }

        //public override void UpdateBeforeSimulation10()
        private void FindPath()
        {
            if (_remoteControl == null || _grid == null) return;

            // Read first waypoint from remote control; if none, clear and skip
            Vector3D? maybeDestination = TryGetFirstWaypoint(_remoteControl);
            if (!maybeDestination.HasValue)
            {
                _currentPath.Clear();
                _currentDestination = null;
                return;
            }

            var destination = maybeDestination.Value;
            var start = _remoteControl.CenterOfMass;

            // Check if destination has changed
            bool destinationChanged = !_currentDestination.HasValue || 
                                    Vector3D.DistanceSquared(_currentDestination.Value, destination) > 1.0; // 1 meter threshold

            // Start pathfinding if not already started or if destination changed
            if (destinationChanged || _needsRecompute)
            {
                _needsRecompute = false;
                _currentDestination = destination;
                _remoteControl.CustomData = "";
                _grid.StartPathfinding(start, destination);
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Starting pathfinding to new destination: {destination}");
            }

            // Update pathfinding (processes a few nodes per frame)
            _grid.Update();

            // Update current path - use partial path while calculating, final path when complete
            if (_grid.IsPathCalculated)
            {
                _currentPath = _grid.CalculatedPath;
                
                // Update remote control custom data with path points in F2 format
                UpdateRemoteControlCustomData();
            }
            else if (_grid.PartialPath.Count > 0)
            {
                // Show partial path while calculating
                _currentPath = _grid.PartialPath;
            }
        }

        private void UpdateRemoteControlCustomData()
        {
            if (_currentPath == null || _currentPath.Count == 0)
            {
                _remoteControl.CustomData = "No path found";
                return;
            }

            var customData = new StringBuilder();
            customData.AppendLine("Pathfinder Generated Waypoints:");
            customData.AppendLine($"Total waypoints: {_currentPath.Count}");
            customData.AppendLine();

            for (int i = 0; i < _currentPath.Count; i++)
            {
                var point = _currentPath[i];
                customData.AppendLine($"GPS:Path_{i + 1}:{point.X:F2}:{point.Y:F2}:{point.Z:F2}:");
            }

            _remoteControl.CustomData = customData.ToString();
        }

        private Vector3D? TryGetFirstWaypoint(Sandbox.ModAPI.IMyRemoteControl rc)
        {
            var waypoints = new List<MyWaypointInfo>();
            rc.GetWaypointInfo(waypoints);
            if (waypoints.Count > 0)
            {
                return waypoints[0].Coords;
            }
            return null;
        }

        private void DrawPath()
        {
            _grid.Draw();
            
            if (_currentPath.Count < 2) return;

            try
            {
                // Determine color based on whether path is complete or partial
                bool isComplete = _grid.IsPathCalculated;
                Vector4 lineColor = isComplete ? colorGreen.ToVector4() : Color.Red.ToVector4();
                Color sphereColor = isComplete ? colorGreen : Color.Yellow;

                // Draw lines between consecutive path points
                for (int i = 0; i < _currentPath.Count - 1; i++)
                {
                    var start = _currentPath[i];
                    var end = _currentPath[i + 1];
                    MySimpleObjectDraw.DrawLine(
                        start, 
                        end, 
                        null, 
                        ref lineColor, 
                        1, 
                        MyBillboard.BlendTypeEnum.AdditiveTop);
                }

                // Draw spheres at each path point
                foreach (var point in _currentPath)
                {
                    var matrix = MatrixD.CreateTranslation(point);
                    MySimpleObjectDraw.DrawTransparentSphere(
                        ref matrix, 
                        0.5f, 
                        ref sphereColor, 
                        MySimpleObjectRasterizer.Solid, 
                        12,
                        null,
                        null,
                        -1,
                        -1,
                        null,
                        MyBillboard.BlendTypeEnum.AdditiveTop,
                        1f
                    );
                }

            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Error drawing path: {e.Message}");
            }
        }

    }
}

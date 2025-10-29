using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRageRender;
using Sandbox.Common.ObjectBuilders;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.ModAPI.Ingame;
using Pathfinder.OctreeAStar;

namespace Pathfinder
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl), false)]
    public class NavigationComponent : MyGameLogicComponent
    {
        private bool _initialized = false;
        private Sandbox.ModAPI.IMyRemoteControl _remoteControl;
        private List<Vector3D> _currentPath = new List<Vector3D>();
        private bool _needsRecompute = false;
        private Vector3D? _currentDestination = null;
        private Graph _graph;


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
                    _graph = new Graph();
                    _initialized = true;
                }
                return;
            }

            FindPath();
            _graph.Render();
        }

        //public override void UpdateBeforeSimulation10()
        private void FindPath()
        {
            if (_remoteControl == null) 
            {
                return;
            }

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
                _graph.BeginFindPath(_remoteControl.CubeGrid, start, destination);
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Starting pathfinding to new destination: {destination}");
            }

            _graph.Update();
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
    }
}

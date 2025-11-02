using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using VRageRender;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI;
using Pathfinder.OctreeAStar;

namespace Pathfinder
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl), false)]
    public class NavigationComponent : MyGameLogicComponent
    {
        private bool _initialized = false;
        private Sandbox.ModAPI.IMyRemoteControl _remoteControl;
        private bool _needsRecompute = false;
        private bool _graphReset = true;
        private Graph _graph;
        private DateTime _pathStartTime = DateTime.MinValue;
        private static readonly Guid PATHFINDER_GOAL_STORAGE_ID = new Guid("7ff8e4a6-69ca-48d3-917e-8d8d4c3a4d9a");
        private Vector3D? _destination = null;
        private bool _suppressSave;

        public bool NeedsRecompute
        {
            get { return _needsRecompute; }
            set { _needsRecompute = value; }
        }
        
        public Vector3D? Destination
        {
            get { return _destination; }
            set 
            { 
                if (_destination == value)
                {
                    return;
                }
                _destination = value; 
                _needsRecompute = true;
                Save();
            }
        }
        
        // for debugging
        public void Step()
        {
            if (_graph != null)
            {
                _graph.Update();
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_initialized)
            {
                if (Entity is Sandbox.ModAPI.IMyRemoteControl)
                {
                    _remoteControl = (Sandbox.ModAPI.IMyRemoteControl)Entity;
                    _graph = new Graph();
                    Load();
                    _initialized = true;
                }
                return;
            }

            FindPath();
            _graph.Render();
        }

        public override void UpdateBeforeSimulation100()
        {
            OctreeAStarSettings.Instance.Update();
        }

        private void FindPath()
        {
            if (_destination == null) 
            {
                ClearPath();
                return;
            }

            var destination = _destination.Value;
            var start = _remoteControl.CubeGrid.WorldAABB.Center;
            _graphReset = false;

            if (_needsRecompute)
            {
                _needsRecompute = false;
                _graph.BeginFindPath(_remoteControl.CubeGrid, start, destination);
                _pathStartTime = DateTime.Now;
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Starting pathfinding to GPS '{_destination.Value}'");
            }

            if (_graph.Path == null || _graph.Path.state != Path.State.Calculating)
            {
                return;
            }
            _graph.Update();

            if (_graph.Path.state == Path.State.Ready)
            {
                var elapsed = (DateTime.Now - _pathStartTime).TotalSeconds;
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Path found in {elapsed:F3} seconds");
            }
            else if (_graph.Path.state == Path.State.NoPath)
            {
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", "No path found");
            }
        }

        public void ClearPath()
        {
            if (_graphReset || _graph == null)
            {
                return;
            }
            _graphReset = true;
            _needsRecompute = false;
            _graph.Reset();
            _graphReset = true;
        }

        private void Load()
        {
            if (_remoteControl == null)
            {
                return;
            }
            var storage = _remoteControl.Storage;
            if (storage == null)
            {
                return;
            }
            string value;
            if (!storage.TryGetValue(PATHFINDER_GOAL_STORAGE_ID, out value) || string.IsNullOrWhiteSpace(value))
            {
                WithSaveSuppressed(() => Destination = null);
                return;
            }
            WithSaveSuppressed(() => Destination = Utils.ParseCoordsFromString(value));
        }

        private void Save()
        {
            if (_remoteControl == null || _suppressSave)
            {
                return;
            }
            if (_remoteControl.Storage == null)
            {
                _remoteControl.Storage = new MyModStorageComponent();
            }
            var destinationString = Utils.GetStringFromCoords(Destination);
            _remoteControl.Storage.RemoveValue(PATHFINDER_GOAL_STORAGE_ID);
            _remoteControl.Storage.SetValue(PATHFINDER_GOAL_STORAGE_ID, destinationString);
        }

        private void WithSaveSuppressed(Action action)
        {
            if (action == null)
            {
                return;
            }
            var previous = _suppressSave;
            _suppressSave = true;
            try
            {
                action();
            }
            finally
            {
                _suppressSave = previous;
            }
        }

        public string GetPathPointsString()
        {
            if (_graph != null && _graph.Path != null)
            {
                return _graph.Path.ToString();
            }
            return string.Empty;
        }

        public string GetPathStatusString()
        {
            if (_graph != null && _graph.Path != null)
            {
                return _graph.Path.state.ToString();
            }
            return Path.State.NotStarted.ToString();
        }
    }
}

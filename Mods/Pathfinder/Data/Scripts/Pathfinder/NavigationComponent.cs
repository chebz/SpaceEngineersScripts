using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
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
using Sandbox.Game.Entities.Blocks;

namespace Pathfinder
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl), false)]
    public class NavigationComponent : MyGameLogicComponent
    {
        private const double DEFAULT_MIN_ALTITUDE = 100.0;
        private const double DEFAULT_MAX_ALTITUDE = 0.0;

        private bool _initialized = false;
        private Sandbox.ModAPI.IMyRemoteControl _remoteControl;
        private bool _needsRecompute = false;
        private bool _graphReset = true;
        private Graph _graph;
        private DateTime _pathStartTime = DateTime.MinValue;
        private static readonly Guid PATHFINDER_GOAL_STORAGE_ID = new Guid("7ff8e4a6-69ca-48d3-917e-8d8d4c3a4d9a");
        private static readonly Guid MIN_ALTITUDE_STORAGE_ID = new Guid("79df6c4d-962b-438f-b7b3-eca6c479d67d");
        private static readonly Guid MAX_ALTITUDE_STORAGE_ID = new Guid("3a75e1a9-bdc9-4a07-b5f1-9b9b6c1b0ed8");
        private Vector3D? _destination = null;
        private bool _suppressSave;
        private Vector3D? _lastStartPosition;
        private const double POSITION_EPSILON = 0.1;
        private double _minAltitude = DEFAULT_MIN_ALTITUDE;
        private double _maxAltitude = DEFAULT_MAX_ALTITUDE;
        private readonly DynamicPathRefinement _dynamicPathRefinement = new DynamicPathRefinement();
        private int _currentWaypointIndex = -1;

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
                var newDestination = value;
                bool destinationChanged = !AreClose(_destination, newDestination, POSITION_EPSILON);

                _destination = newDestination;

                bool droneMoved = HasDroneMoved(POSITION_EPSILON);

                if (destinationChanged || droneMoved)
                {
                    _needsRecompute = true;
                    if (destinationChanged)
                    {
                        Save();
                    }
                }
            }
        }

        public double MinAltitude
        {
            get 
            { 
                return _minAltitude; 
            }
            set 
            { 
                if (Math.Abs(value - _minAltitude) < 0.01)
                {
                    return;
                }
                _minAltitude = value;
                _needsRecompute = true;
                Save();
            }
        }

        public double MaxAltitude
        {
            get
            {
                return _maxAltitude;
            }
            set
            {
                if (Math.Abs(value - _maxAltitude) < 0.01)
                {
                    return;
                }
                _maxAltitude = value;
                _needsRecompute = true;
                Save();
            }
        }

        public Graph Graph => _graph;

        public Sandbox.ModAPI.IMyRemoteControl RemoteControl => _remoteControl;

        public int CurrentWaypointIndex => _currentWaypointIndex;

        public string RefinedPathString => _dynamicPathRefinement.RefinedPathString;
        
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
                if (_remoteControl == null && Entity is Sandbox.ModAPI.IMyRemoteControl)
                {
                    _remoteControl = (Sandbox.ModAPI.IMyRemoteControl)Entity;
                }

                if (!Load())
                {
                    return;
                }
                if (_remoteControl != null)
                {
                    _graph = new Graph();
                    
                    _initialized = true;
                }
                return;
            }

            FindPath();
            _graph.Render();
            _dynamicPathRefinement.Update(this);
        }

        public override void UpdateBeforeSimulation100()
        {
            OctreeAStarSettings.Instance.Update();

            _dynamicPathRefinement.RefinePath(this);
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
                _graph.BeginFindPath(_remoteControl, start, destination, MinAltitude, MaxAltitude);
                _pathStartTime = DateTime.Now;
                _lastStartPosition = start;
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

        private bool Load()
        {
            if (_remoteControl == null)
            {
                return false;
            }
            var storage = _remoteControl.Storage;
            if (storage == null)
            {
                storage = new MyModStorageComponent();
                _remoteControl.Storage = storage;
            }
            string value;
            // destination
            if (storage.TryGetValue(PATHFINDER_GOAL_STORAGE_ID, out value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Destination = null;
                }
                else
                {
                    WithSaveSuppressed(() => Destination = Utils.ParseCoordsFromString(value));
                }
            }
            else
            {
                Destination = null;
            }
            // min altitude
            if (storage.TryGetValue(MIN_ALTITUDE_STORAGE_ID, out value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    MinAltitude = DEFAULT_MIN_ALTITUDE;
                }
                else
                {
                    WithSaveSuppressed(() => MinAltitude = double.Parse(value, CultureInfo.InvariantCulture));
                }
            }
            else
            {
                MinAltitude = DEFAULT_MIN_ALTITUDE;
            }
            // max altitude
            if (storage.TryGetValue(MAX_ALTITUDE_STORAGE_ID, out value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    MaxAltitude = DEFAULT_MAX_ALTITUDE;
                }
                else
                {
                    WithSaveSuppressed(() => MaxAltitude = double.Parse(value, CultureInfo.InvariantCulture));
                }
            }
            else
            {
                MaxAltitude = DEFAULT_MAX_ALTITUDE;
            }
            return true;
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
            _remoteControl.Storage.SetValue(PATHFINDER_GOAL_STORAGE_ID, destinationString);
            var minAltitudeString = MinAltitude.ToString(CultureInfo.InvariantCulture);
            _remoteControl.Storage.SetValue(MIN_ALTITUDE_STORAGE_ID, minAltitudeString);
            var maxAltitudeString = MaxAltitude.ToString(CultureInfo.InvariantCulture);
            _remoteControl.Storage.SetValue(MAX_ALTITUDE_STORAGE_ID, maxAltitudeString);
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

        private bool HasDroneMoved(double tolerance)
        {
            if (!_lastStartPosition.HasValue || _remoteControl == null)
            {
                return false;
            }

            var currentPosition = _remoteControl.CubeGrid.WorldAABB.Center;
            var toleranceSquared = tolerance * tolerance;
            return Vector3D.DistanceSquared(currentPosition, _lastStartPosition.Value) > toleranceSquared;
        }

        private static bool AreClose(Vector3D? a, Vector3D? b, double tolerance)
        {
            if (!a.HasValue && !b.HasValue)
            {
                return true;
            }

            if (!a.HasValue || !b.HasValue)
            {
                return false;
            }

            var toleranceSquared = tolerance * tolerance;
            return Vector3D.DistanceSquared(a.Value, b.Value) <= toleranceSquared;
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

        public void SetCurrentWaypointIndex(int index)
        {
            _currentWaypointIndex = index;
        }

        public string GetRefinedPathString()
        {
            return _dynamicPathRefinement.RefinedPathString;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;
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
using Sandbox.Game.Entities;

namespace Pathfinder
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RemoteControl), false)]
    public class NavigationComponent : MyGameLogicComponent
    {
        private const double DEFAULT_MIN_ALTITUDE = 100.0;
        private const double DEFAULT_MAX_ALTITUDE = 0.0;
        private const double DEFAULT_DESTINATION_TOLERANCE = 50.0;
        private const double DEFAULT_MIN_DISTANCE_FROM_DESTINATION = 0.0;
        private const double DEFAULT_MAX_DISTANCE_FROM_DESTINATION = 50.0;
        private const double DEFAULT_MAX_RDP_DISTANCE_FROM_DESTINATION = 50.0;
        private const bool DEFAULT_ENABLE_DYNAMIC_PATH_REFINEMENT = true;

        private bool _initialized = false;
        private Sandbox.ModAPI.IMyRemoteControl _remoteControl;
        private bool _needsRecompute = false;
        private bool _graphReset = true;
        private Graph _graph;
        private DateTime _pathStartTime = DateTime.MinValue;
        private static readonly Guid PATHFINDER_GOAL_STORAGE_ID = new Guid("7ff8e4a6-69ca-48d3-917e-8d8d4c3a4d9a");
        private static readonly Guid MIN_ALTITUDE_STORAGE_ID = new Guid("79df6c4d-962b-438f-b7b3-eca6c479d67d");
        private static readonly Guid MAX_ALTITUDE_STORAGE_ID = new Guid("3a75e1a9-bdc9-4a07-b5f1-9b9b6c1b0ed8");
        private static readonly Guid DESTINATION_NAME_STORAGE_ID = new Guid("4d9c3b27-4120-4b7a-86d9-6fb5c9cf9bf3");
        private static readonly Guid DESTINATION_TOLERANCE_STORAGE_ID = new Guid("f4e8f9c5-0e96-49aa-8f5d-4db4c0d0f8d4");
        private static readonly Guid MIN_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("1f1397f9-910a-4a4a-95da-63d74f7a3fd2");
        private static readonly Guid MAX_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("31cbad8e-6adb-4471-9f4a-67583515a69c");
        private static readonly Guid MAX_RDP_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("a2b3c4d5-e6f7-8901-2345-6789abcdef01");
        private static readonly Guid ENABLE_DYNAMIC_PATH_REFINEMENT_STORAGE_ID = new Guid("b3c4d5e6-f7a8-9012-3456-789abcdef012");
        private Vector3D? _destination = null;
        private string _destinationName = string.Empty;
        private Vector3D? _destinationNamePosition;
        private double _destinationTolerance = DEFAULT_DESTINATION_TOLERANCE;
        private double _minDistanceFromDestination = DEFAULT_MIN_DISTANCE_FROM_DESTINATION;
        private double _maxDistanceFromDestination = DEFAULT_MAX_DISTANCE_FROM_DESTINATION;
        private double _maxRdpDistanceFromDestination = DEFAULT_MAX_RDP_DISTANCE_FROM_DESTINATION;
        private bool _enableDynamicPathRefinement = DEFAULT_ENABLE_DYNAMIC_PATH_REFINEMENT;
        private bool _suppressSave;
        private Vector3D? _lastStartPosition;
        private const double POSITION_EPSILON = 0.1;
        private double _minAltitude = DEFAULT_MIN_ALTITUDE;
        private double _maxAltitude = DEFAULT_MAX_ALTITUDE;
        private readonly DynamicPathRefinement _dynamicPathRefinement = new DynamicPathRefinement();
        private int _currentWaypointIndex = -1;
        private Vector3D _testRaycastStart;
        private Vector3D _testRaycastEnd;
        private MyOrientedBoundingBoxD _testOOB;

        public bool NeedsRecompute
        {
            get { return _needsRecompute; }
            set { _needsRecompute = value; }
        }
        
        public Vector3D? Destination
        {
            get { return _destination; }
            set { SetDestinationInternal(value, _destinationTolerance, true); }
        }

        public string DestinationName => _destinationName;

        public double DestinationTolerance
        {
            get { return _destinationTolerance; }
            set { SetDestinationTolerance(value, false); }
        }

        public double MinDistanceFromDestination
        {
            get { return _minDistanceFromDestination; }
            set { SetMinDistanceFromDestination(value, false); }
        }

        public double MaxDistanceFromDestination
        {
            get { return _maxDistanceFromDestination; }
            set { SetMaxDistanceFromDestination(value, false); }
        }

        public double MaxRdpDistanceFromDestination
        {
            get { return _maxRdpDistanceFromDestination; }
            set { SetMaxRdpDistanceFromDestination(value, false); }
        }

        public bool EnableDynamicPathRefinement
        {
            get { return _enableDynamicPathRefinement; }
            set { SetEnableDynamicPathRefinement(value, false); }
        }

        public void SetDestinationNameFromTerminal(string name)
        {
            SetDestinationName(name, false);
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
                    _graph.OnPathStateChanged += OnPathStateChanged;
                    _initialized = true;
                }
                return;
            }

            FindPath();
            _graph.Render();
            _dynamicPathRefinement.Update(this);
            // if (_remoteControl.CubeGrid.CustomName == "Medium Miner")
            // {
            //     _testRaycastStart = _remoteControl.CubeGrid.WorldAABB.Center;
            //     _testRaycastEnd = _testRaycastStart + _remoteControl.WorldMatrix.GetOrientation().Forward * 200;
            //     Utils.DrawOBB(_testOOB, Color.Green, true);
            // }
        }

        public override void UpdateBeforeSimulation100()
        {
            OctreeAStarSettings.Instance.Update();

            UpdateDestinationFromName();
            
            _dynamicPathRefinement.RefinePath(this);
            //TestRaycast();
            // TestOOBCast();
        }

        private void OnPathStateChanged(Path.State state)
        {
            if (state == Path.State.Ready)
            {
                _currentWaypointIndex = 1;
            }
            else if (state == Path.State.NoPath)
            {
                _dynamicPathRefinement.Clear();
                _currentWaypointIndex = -1;
            }
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
                var parameters = new PathfindingParameters
                {
                    RemoteControl = _remoteControl,
                    Start = start,
                    End = destination,
                    MinAltitude = MinAltitude,
                    MaxAltitude = MaxAltitude,
                    MinDistanceFromDestination = MinDistanceFromDestination,
                    MaxDistanceFromDestination = MaxDistanceFromDestination,
                    EnableDynamicPathRefinement = false,
                    DynamicObstacles = null
                };
                _graph.BeginFindPath(parameters);
                _pathStartTime = DateTime.Now;
                _lastStartPosition = start;
                Utils.ShowHudMessage($"Starting pathfinding to GPS '{_destination.Value}'");
            }

            if (_graph.Path == null || _graph.Path.state != Path.State.Calculating)
            {
                return;
            }
            _graph.Update();

            if (_graph.Path.state == Path.State.Ready)
            {
                var elapsed = (DateTime.Now - _pathStartTime).TotalSeconds;
                Utils.ShowHudMessage($"Path found in {elapsed:F3} seconds");
            }
            else if (_graph.Path.state == Path.State.NoPath)
            {
                Utils.ShowHudMessage("No path found");
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
            _dynamicPathRefinement.Clear();
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
            if (storage.TryGetValue(DESTINATION_NAME_STORAGE_ID, out value))
            {
                SetDestinationName(value, true);
            }
            if (storage.TryGetValue(DESTINATION_TOLERANCE_STORAGE_ID, out value))
            {
                double tolerance;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance))
                {
                    SetDestinationTolerance(tolerance, true);
                }
                else
                {
                    SetDestinationTolerance(DEFAULT_DESTINATION_TOLERANCE, true);
                }
            }
            else
            {
                SetDestinationTolerance(DEFAULT_DESTINATION_TOLERANCE, true);
            }
        if (storage.TryGetValue(MIN_DISTANCE_FROM_DESTINATION_STORAGE_ID, out value))
        {
            double distance;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
            {
                SetMinDistanceFromDestination(distance, true);
            }
            else
            {
                SetMinDistanceFromDestination(DEFAULT_MIN_DISTANCE_FROM_DESTINATION, true);
            }
        }
        else
        {
            SetMinDistanceFromDestination(DEFAULT_MIN_DISTANCE_FROM_DESTINATION, true);
        }
        if (storage.TryGetValue(MAX_DISTANCE_FROM_DESTINATION_STORAGE_ID, out value))
        {
            double distance;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
            {
                SetMaxDistanceFromDestination(distance, true);
            }
            else
            {
                SetMaxDistanceFromDestination(DEFAULT_MAX_DISTANCE_FROM_DESTINATION, true);
            }
        }
        else
        {
            SetMaxDistanceFromDestination(DEFAULT_MAX_DISTANCE_FROM_DESTINATION, true);
        }
        if (storage.TryGetValue(MAX_RDP_DISTANCE_FROM_DESTINATION_STORAGE_ID, out value))
        {
            double distance;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
            {
                SetMaxRdpDistanceFromDestination(distance, true);
            }
            else
            {
                SetMaxRdpDistanceFromDestination(DEFAULT_MAX_RDP_DISTANCE_FROM_DESTINATION, true);
            }
        }
        else
        {
            SetMaxRdpDistanceFromDestination(DEFAULT_MAX_RDP_DISTANCE_FROM_DESTINATION, true);
        }
        if (storage.TryGetValue(ENABLE_DYNAMIC_PATH_REFINEMENT_STORAGE_ID, out value))
        {
            bool enable;
            if (bool.TryParse(value, out enable))
            {
                SetEnableDynamicPathRefinement(enable, true);
            }
            else
            {
                SetEnableDynamicPathRefinement(DEFAULT_ENABLE_DYNAMIC_PATH_REFINEMENT, true);
            }
        }
        else
        {
            SetEnableDynamicPathRefinement(DEFAULT_ENABLE_DYNAMIC_PATH_REFINEMENT, true);
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
            _remoteControl.Storage.SetValue(DESTINATION_NAME_STORAGE_ID, _destinationName ?? string.Empty);
            _remoteControl.Storage.SetValue(DESTINATION_TOLERANCE_STORAGE_ID, _destinationTolerance.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(MIN_DISTANCE_FROM_DESTINATION_STORAGE_ID, _minDistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(MAX_DISTANCE_FROM_DESTINATION_STORAGE_ID, _maxDistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(MAX_RDP_DISTANCE_FROM_DESTINATION_STORAGE_ID, _maxRdpDistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(ENABLE_DYNAMIC_PATH_REFINEMENT_STORAGE_ID, _enableDynamicPathRefinement.ToString());
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

        private void SetDestinationInternal(Vector3D? newDestination, double tolerance, bool updateNameFromCoordinates)
        {
            var destinationChanged = !AreClose(_destination, newDestination, tolerance);
            _destination = newDestination;

            if (updateNameFromCoordinates)
            {
                UpdateDestinationNameFromCoordinates();
            }
            else
            {
                if (_destination.HasValue)
                {
                    _destinationNamePosition = _destination;
                }
                else
                {
                    _destinationNamePosition = null;
                }
            }

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

        private void SetDestinationForced(Vector3D? value)
        {
            SetDestinationInternal(value, _destinationTolerance, false);
        }

        private void SetDestinationName(string name, bool fromStorage)
        {
            var originalDestination = _destination;
            var newName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            var changed = !string.Equals(_destinationName, newName, StringComparison.OrdinalIgnoreCase);
            _destinationName = newName;

            Action apply = () =>
            {
                if (string.IsNullOrWhiteSpace(_destinationName))
                {
                    _destinationNamePosition = null;
                }
                else
                {
                    _destinationNamePosition = null;
                    UpdateDestinationFromName(true);
                }
            };

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
            }

            if (changed && !fromStorage)
            {
                if (AreClose(originalDestination, _destination, POSITION_EPSILON))
                {
                    Save();
                }
            }
        }

        private void SetDestinationTolerance(double value, bool fromStorage)
        {
            var sanitized = Math.Max(0.0, value);
            if (Math.Abs(sanitized - _destinationTolerance) < 0.01)
            {
                return;
            }

            Action apply = () => _destinationTolerance = sanitized;

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
                Save();
            }
        }

        private void SetMinDistanceFromDestination(double value, bool fromStorage)
        {
            var sanitized = Math.Max(0.0, value);
            var expandMax = sanitized > _maxDistanceFromDestination;
            if (Math.Abs(sanitized - _minDistanceFromDestination) < 0.01 && !expandMax)
            {
                return;
            }

            Action apply = () =>
            {
                _minDistanceFromDestination = sanitized;
                if (expandMax)
                {
                    _maxDistanceFromDestination = sanitized;
                }
            };

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
                _needsRecompute = true;
                Save();
            }
        }

        private void SetMaxDistanceFromDestination(double value, bool fromStorage)
        {
            var sanitized = Math.Max(0.0, value);
            var reduceMin = sanitized < _minDistanceFromDestination;
            if (Math.Abs(sanitized - _maxDistanceFromDestination) < 0.01 && !reduceMin)
            {
                return;
            }

            Action apply = () =>
            {
                _maxDistanceFromDestination = sanitized;
                if (reduceMin)
                {
                    _minDistanceFromDestination = sanitized;
                }
            };

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
                _needsRecompute = true;
                Save();
            }
        }

        private void SetMaxRdpDistanceFromDestination(double value, bool fromStorage)
        {
            var sanitized = Math.Max(0.0, value);
            if (Math.Abs(sanitized - _maxRdpDistanceFromDestination) < 0.01)
            {
                return;
            }

            Action apply = () =>
            {
                _maxRdpDistanceFromDestination = sanitized;
            };

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
                Save();
            }
        }

        private void SetEnableDynamicPathRefinement(bool value, bool fromStorage)
        {
            if (value == _enableDynamicPathRefinement)
            {
                return;
            }

            Action apply = () =>
            {
                _enableDynamicPathRefinement = value;
            };

            if (fromStorage)
            {
                WithSaveSuppressed(apply);
            }
            else
            {
                apply();
                Save();
            }
        }

        private void UpdateDestinationFromName(bool force = false)
        {
            if (string.IsNullOrWhiteSpace(_destinationName))
            {
                return;
            }

            var gpsList = Utils.GetGpsList();
            var match = gpsList.FirstOrDefault(g =>
            {
                if (g == null || string.IsNullOrWhiteSpace(g.Name))
                {
                    return false;
                }

                var gpsName = g.Name;
                if (string.Equals(gpsName, _destinationName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var separator = " - ";
                var separatorIndex = gpsName.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
                if (separatorIndex >= 0)
                {
                    var nameBeforeSeparator = gpsName.Substring(0, separatorIndex);
                    if (string.Equals(nameBeforeSeparator, _destinationName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (gpsName.StartsWith(_destinationName + separator, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            });

            if (match == null)
            {
                Utils.ShowHudMessage("No GPS found for destination name: " + _destinationName);
                return;
            }

            var coords = match.Coords;
            var coordsChanged = !_destinationNamePosition.HasValue || !AreClose(_destinationNamePosition, coords, _destinationTolerance);
            if (coordsChanged || force)
            {
                _destinationNamePosition = coords;
                SetDestinationForced(coords);
            }
        }

        private void UpdateDestinationNameFromCoordinates()
        {
            if (!_destination.HasValue)
            {
                _destinationNamePosition = null;
                if (!string.IsNullOrWhiteSpace(_destinationName))
                {
                    _destinationName = string.Empty;
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(_destinationName))
            {
                _destinationNamePosition = _destination;
                return;
            }

            var gpsList = Utils.GetGpsList();
            var match = gpsList.FirstOrDefault(g => g != null && Vector3D.DistanceSquared(g.Coords, _destination.Value) < 0.1);
            if (match != null)
            {
                _destinationName = match.Name ?? string.Empty;
                _destinationNamePosition = match.Coords;
            }
            else
            {
                _destinationNamePosition = _destination;
            }
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

        // private void TestRaycast()
        // {
        //     if (_remoteControl.CubeGrid.CustomName != "Medium Miner")
        //     {
        //         return;
        //     }
        //     var hits = new List<IHitInfo>();
        //     MyAPIGateway.Physics.CastRay(_testRaycastStart, _testRaycastEnd, hits, OctreeAStarSettings.Instance.TestCollisionLayer);
        //     foreach (var hit in hits)
        //     {
        //         Utils.ShowHudMessage(OctreeAStarSettings.Instance.TestCollisionLayer + ": " + hit.HitEntity.Name + " " + hit.HitEntity.DisplayName + " " + hit.HitEntity.GetFriendlyName() + " " + hit.HitEntity.GetType().Name);
        //     }
        // }

        // private void TestOOBCast()
        // {
        //     if (_remoteControl.CubeGrid.CustomName != "Medium Miner")
        //     {
        //         return;
        //     }
        //     var center = _testRaycastStart + (_testRaycastEnd - _testRaycastStart) * 0.5;
        //     var halfExtents = _remoteControl.CubeGrid.WorldAABB.Size * 0.5;
        //     var dist = Vector3D.Distance(_testRaycastStart, _testRaycastEnd);
        //     halfExtents.Z = dist * 0.5;
        //     var bounds = new BoundingBoxD(-halfExtents, halfExtents);
        //     _testOOB = new MyOrientedBoundingBoxD(bounds, _remoteControl.WorldMatrix);
        //     var result = new List<VRage.Game.Entity.MyEntity>();
        //     MyGamePruningStructure.GetAllEntitiesInOBB(ref _testOOB, result, MyEntityQueryType.Both);
        //     foreach (var entity in result)
        //     {
        //         Utils.ShowHudMessage(entity.Name + " " + entity.DisplayName + " " + entity.GetFriendlyName() + " " + entity.GetType().Name);

        //         var voxelMap = entity as MyVoxelBase;
        //         if (voxelMap != null)
        //         {
        //             var vol = voxelMap.GetVoxelContentInBoundingBox_Fast(bounds, _remoteControl.WorldMatrix);
        //             if (vol.Item2 > 0)
        //             {
        //                 Utils.ShowHudMessage("Voxel map found: "+ vol.Item1 + " " + vol.Item2);
        //             }
        //         }
        //     }
        // }
    }
}

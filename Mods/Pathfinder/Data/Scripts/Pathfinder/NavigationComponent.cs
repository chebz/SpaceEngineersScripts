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
        private const double DEFAULT_MIN_DISTANCE_FROM_DESTINATION = 0.0;
        private const double DEFAULT_MAX_DISTANCE_FROM_DESTINATION = 50.0;
        private const double DEFAULT_MAX_CA_DISTANCE_FROM_DESTINATION = 50.0;
        private const bool DEFAULT_ENABLE_COLLISION_AVOIDANCE = false;
        private const bool DEFAULT_ENABLE_PATHFINDING = true;

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
        private static readonly Guid MIN_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("1f1397f9-910a-4a4a-95da-63d74f7a3fd2");
        private static readonly Guid MAX_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("31cbad8e-6adb-4471-9f4a-67583515a69c");
        private static readonly Guid MAX_CA_DISTANCE_FROM_DESTINATION_STORAGE_ID = new Guid("a2b3c4d5-e6f7-8901-2345-6789abcdef01");
        private static readonly Guid ENABLE_COLLISION_AVOIDANCE_STORAGE_ID = new Guid("b3c4d5e6-f7a8-9012-3456-789abcdef012");
        private static readonly Guid ENABLE_PATHFINDING_STORAGE_ID = new Guid("c4d5e6f7-a8b9-0123-4567-89abcdef0123");
        private static readonly Guid DESTINATION_OFFSET_STORAGE_ID = new Guid("d5e6f7a8-b9c0-1234-5678-9abcdef01234");
        private Vector3D? _destination = null;
        private string _destinationName = string.Empty;
        private double _minDistanceFromDestination = DEFAULT_MIN_DISTANCE_FROM_DESTINATION;
        private double _maxDistanceFromDestination = DEFAULT_MAX_DISTANCE_FROM_DESTINATION;
        private double _maxCADistanceFromDestination = DEFAULT_MAX_CA_DISTANCE_FROM_DESTINATION;
        private bool _enableCollisionAvoidance = DEFAULT_ENABLE_COLLISION_AVOIDANCE;
        private bool _enablePathfinding = DEFAULT_ENABLE_PATHFINDING;
        private bool _suppressSave;
        private Vector3D? _lastStartPosition;
        private const double POSITION_EPSILON = 0.1;
        private double _minAltitude = DEFAULT_MIN_ALTITUDE;
        private double _maxAltitude = DEFAULT_MAX_ALTITUDE;
        private readonly CollisionAvoidance _collisionAvoidance = new CollisionAvoidance();
        private Vector3D? _caTarget = null;
        private int _currentWaypointIndex = -1;
        private IMyCubeBlock _cachedTargetEntity = null;
        private Vector3D _destinationOffset = Vector3D.Zero;

        public bool NeedsRecompute
        {
            get { return _needsRecompute; }
            set { _needsRecompute = value; }
        }
        
        public Vector3D? Destination
        {
            get 
            { 
                if (TargetMatrix != null)
                {
                    return TargetMatrix.Value.Translation + Vector3D.TransformNormal(_destinationOffset, TargetMatrix.Value);
                }
                return _destination; 
            }
            set 
            { 
                SetDestination(value); 
            }
        }

        public string DestinationName 
        {
            get { return _destinationName; }
            set { SetDestinationName(value, false); }
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

        public double MaxCADistanceFromDestination
        {
            get { return _maxCADistanceFromDestination; }
            set { SetMaxCADistanceFromDestination(value, false); }
        }

        public bool EnablePathfinding
        {
            get { return _enablePathfinding; }
            set { SetEnablePathfinding(value, false); }
        }

        public bool EnableCollisionAvoidance
        {
            get { return _enableCollisionAvoidance; }
            set { SetEnableCollisionAvoidance(value, false); }
        }

        public Vector3D? CATarget
        {
            get { return _caTarget; }
            set { _caTarget = value; }
        }

        public bool IsOnBaseApproach
        {
            get 
            { 
                var isOnBaseApproach = _enableCollisionAvoidance && _collisionAvoidance != null && _collisionAvoidance.IsOnBaseApproach; 
                return isOnBaseApproach;
            }
        }

        public Vector3D? TargetVelocity
        {
            get 
            {
                var remoteControl = _cachedTargetEntity as Sandbox.ModAPI.IMyRemoteControl;
                if (remoteControl != null)
                {
                    var linearVelocity = remoteControl.GetShipVelocities().LinearVelocity;
                    return linearVelocity;
                }
                return null;
             }
        }

        public MatrixD? TargetMatrix
        {
            get 
            {
                if (_cachedTargetEntity == null)
                {
                    return null;
                }
                return _cachedTargetEntity.WorldMatrix;
             }
        }

        public Vector3D DestinationOffset
        {
            get { return _destinationOffset; }
            set
            {
                if (_destinationOffset != value)
                {
                    _destinationOffset = value;
                    if (!string.IsNullOrEmpty(_destinationName))
                    {
                        _needsRecompute = true;
                    }
                }
            }
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

        public double AgentSize
        {
            get
            {
                if (_remoteControl?.CubeGrid != null)
                {
                    return _remoteControl.CubeGrid.WorldVolume.Radius * 2.0;
                }
                return 0.0;
            }
        }

        public int CurrentWaypointIndex => _currentWaypointIndex;
        
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
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void Close()
        {
            _collisionAvoidance.Clear();
            base.Close();
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
            if (_enableCollisionAvoidance)
            {
                _collisionAvoidance.Update(this);
                _collisionAvoidance.Render(this);
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            UpdateCATarget();
        }

        public override void UpdateBeforeSimulation100()
        {
            PathfinderSettings.Instance.Update();
            //UpdateCATarget();
        }

        private void UpdateCATarget()
        {
            if (_enableCollisionAvoidance)
            {
                _collisionAvoidance.RefinePath(this);
            }
            else
            {
                // When collision avoidance is disabled, set CA target to current waypoint to keep navigation working
                if (_graph != null && _graph.Path != null && _graph.Path.state == Path.State.Ready)
                {
                    var points = _graph.Path.points;
                    if (points != null && points.Count > 0 && _currentWaypointIndex > 0 && _currentWaypointIndex < points.Count)
                    {
                        // Set CA target to the current waypoint
                        _caTarget = points[_currentWaypointIndex];
                    }
                    else if (points != null && points.Count > 0 && _currentWaypointIndex >= points.Count)
                    {
                        // Reached the end, use the last waypoint
                        _caTarget = points[points.Count - 1];
                    }
                    else
                    {
                        _caTarget = null;
                    }
                }
                else
                {
                    _caTarget = null;
                }
            }
        }

        private void OnPathStateChanged(Path.State state)
        {
            if (state == Path.State.Ready)
            {
                _currentWaypointIndex = 1;
            }
            else if (state == Path.State.NoPath)
            {
                _collisionAvoidance.Clear();
                _currentWaypointIndex = -1;
            }
        }

        private void FindPath()
        {
            if (Destination == null) 
            {
                ClearPath();
                return;
            }

            var start = _remoteControl.CubeGrid.WorldAABB.Center;
            var end = Destination.Value;
            _graphReset = false;

            if (_needsRecompute)
            {
                _needsRecompute = false;

                if (!_enablePathfinding)
                {
                    _graph.SetSimplePath(start, end);
                }
                else
                {
                    var parameters = new PathfindingParameters
                    {
                        RemoteControl = _remoteControl,
                        Start = start,
                        End = end,
                        MinAltitude = MinAltitude,
                        MaxAltitude = MaxAltitude,
                        MinDistanceFromDestination = MinDistanceFromDestination,
                        MaxDistanceFromDestination = MaxDistanceFromDestination,
                        EnableDynamicPathRefinement = false,
                        DynamicObstacles = null,
                        AgentSize = AgentSize
                    };
                    _graph.BeginFindPath(parameters);
                    _pathStartTime = DateTime.Now;
                    _lastStartPosition = start;
                    Utils.ShowHudMessage($"Starting pathfinding to GPS '{end}'");
                }
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
            _collisionAvoidance.Clear();
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
        if (storage.TryGetValue(MAX_CA_DISTANCE_FROM_DESTINATION_STORAGE_ID, out value))
        {
            double distance;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
            {
                SetMaxCADistanceFromDestination(distance, true);
            }
            else
            {
                SetMaxCADistanceFromDestination(DEFAULT_MAX_CA_DISTANCE_FROM_DESTINATION, true);
            }
        }
        else
        {
            SetMaxCADistanceFromDestination(DEFAULT_MAX_CA_DISTANCE_FROM_DESTINATION, true);
        }
        if (storage.TryGetValue(ENABLE_COLLISION_AVOIDANCE_STORAGE_ID, out value))
        {
            bool enable;
            if (bool.TryParse(value, out enable))
            {
                SetEnableCollisionAvoidance(enable, true);
            }
            else
            {
                SetEnableCollisionAvoidance(DEFAULT_ENABLE_COLLISION_AVOIDANCE, true);
            }
        }
        else
        {
            SetEnableCollisionAvoidance(DEFAULT_ENABLE_COLLISION_AVOIDANCE, true);
        }
        if (storage.TryGetValue(ENABLE_PATHFINDING_STORAGE_ID, out value))
        {
            bool enable;
            if (bool.TryParse(value, out enable))
            {
                SetEnablePathfinding(enable, true);
            }
            else
            {
                SetEnablePathfinding(DEFAULT_ENABLE_PATHFINDING, true);
            }
        }
        else
        {
            SetEnablePathfinding(DEFAULT_ENABLE_PATHFINDING, true);
        }
        if (storage.TryGetValue(DESTINATION_OFFSET_STORAGE_ID, out value))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                WithSaveSuppressed(() => _destinationOffset = Vector3D.Zero);
            }
            else
            {
                var offset = Utils.ParseCoordsFromString(value);
                if (offset.HasValue)
                {
                    WithSaveSuppressed(() => _destinationOffset = offset.Value);
                }
                else
                {
                    WithSaveSuppressed(() => _destinationOffset = Vector3D.Zero);
                }
            }
        }
        else
        {
            WithSaveSuppressed(() => _destinationOffset = Vector3D.Zero);
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
            _remoteControl.Storage.SetValue(MIN_DISTANCE_FROM_DESTINATION_STORAGE_ID, _minDistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(MAX_DISTANCE_FROM_DESTINATION_STORAGE_ID, _maxDistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(MAX_CA_DISTANCE_FROM_DESTINATION_STORAGE_ID, _maxCADistanceFromDestination.ToString(CultureInfo.InvariantCulture));
            _remoteControl.Storage.SetValue(ENABLE_COLLISION_AVOIDANCE_STORAGE_ID, _enableCollisionAvoidance.ToString());
            _remoteControl.Storage.SetValue(ENABLE_PATHFINDING_STORAGE_ID, _enablePathfinding.ToString());
            var offsetString = Utils.GetStringFromCoords((Vector3D?)_destinationOffset);
            _remoteControl.Storage.SetValue(DESTINATION_OFFSET_STORAGE_ID, offsetString);
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

        private bool HasMoved(double tolerance)
        {
            if (!_lastStartPosition.HasValue || _remoteControl == null)
            {
                return false;
            }

            var currentPosition = _remoteControl.CubeGrid.WorldAABB.Center;
            var toleranceSquared = tolerance * tolerance;
            return Vector3D.DistanceSquared(currentPosition, _lastStartPosition.Value) > toleranceSquared;
        }

        private void SetDestination(Vector3D? newDestination)
        {
            DestinationName = string.Empty;

            if (AreClose(_destination, newDestination))
            {
                return;
            }

            _destination = newDestination;

            bool hasMoved = HasMoved(POSITION_EPSILON);

            if (hasMoved)
            {
                _needsRecompute = true;
                Save();
            }
        }

        private void SetDestinationName(string name, bool fromStorage)
        {
            var newName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            var changed = !string.Equals(_destinationName, newName, StringComparison.OrdinalIgnoreCase);
            _destinationName = newName;

            UpdateDestinationFromName();

            if (changed && !fromStorage)
            {
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

        private void SetMaxCADistanceFromDestination(double value, bool fromStorage)
        {
            var sanitized = Math.Max(0.0, value);
            if (Math.Abs(sanitized - _maxCADistanceFromDestination) < 0.01)
            {
                return;
            }

            Action apply = () =>
            {
                _maxCADistanceFromDestination = sanitized;
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

        private void SetEnablePathfinding(bool value, bool fromStorage)
        {
            if (value == _enablePathfinding)
            {
                return;
            }

            Action apply = () =>
            {
                _enablePathfinding = value;
                _needsRecompute = true;
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

        private void SetEnableCollisionAvoidance(bool value, bool fromStorage)
        {
            if (value == _enableCollisionAvoidance)
            {
                return;
            }

            Action apply = () =>
            {
                _enableCollisionAvoidance = value;
                if (!value)
                {
                    _collisionAvoidance.Clear();
                }
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

        private void UpdateDestinationFromName()
        {
            _cachedTargetEntity = null;
            if (string.IsNullOrWhiteSpace(_destinationName))
            {
                return;
            }
            if (FindTargetEntity())
            {
                return;
            }
            var gps = FindGpsByName(_destinationName);
            if (gps != null)
            {
                _destination = gps.Coords;
                return;
            }
            _destination = null;
        }


        private IMyGps FindGpsByName(string name)
        {
            var gpsList = Utils.GetGpsList();
            return gpsList.FirstOrDefault(g =>
            {
                if (g == null || string.IsNullOrWhiteSpace(g.Name))
                {
                    return false;
                }

                var gpsName = g.Name;
                if (string.Equals(gpsName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var separator = " - ";
                var separatorIndex = gpsName.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
                if (separatorIndex >= 0)
                {
                    var nameBeforeSeparator = gpsName.Substring(0, separatorIndex);
                    if (string.Equals(nameBeforeSeparator, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return gpsName.StartsWith(name + separator, StringComparison.OrdinalIgnoreCase);
            });
        }

        private bool FindTargetEntity()
        {
            _cachedTargetEntity = null;

            if (string.IsNullOrWhiteSpace(_destinationName))
            {
                return false;
            }

            var entityList = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entityList, entity =>
            {
                if (entity == null || entity.MarkedForClose || entity.Closed)
                {
                    return false;
                }

                var grid = entity as IMyCubeGrid;
                if (grid == null)
                {
                    return false;
                }
                if (!string.Equals(grid.DisplayName, _destinationName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // find beacon on the grid
                var slimBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(slimBlocks, block => block?.FatBlock as Sandbox.ModAPI.IMyBeacon != null);
                if (slimBlocks.Count == 0)
                {
                    return false;
                }

                _cachedTargetEntity = slimBlocks.First().FatBlock;

                // try to find remote control on the grid
                slimBlocks.Clear();
                grid.GetBlocks(slimBlocks, block => block?.FatBlock as Sandbox.ModAPI.IMyRemoteControl != null);
                if (slimBlocks.Count > 0)
                {
                    _cachedTargetEntity = slimBlocks.First().FatBlock;
                    return true;
                }

                return true;
            });

            return _cachedTargetEntity != null;
        }

        private static bool AreClose(Vector3D? a, Vector3D? b)
        {
            if (!a.HasValue && !b.HasValue)
            {
                return true;
            }

            if (!a.HasValue || !b.HasValue)
            {
                return false;
            }

            return Vector3D.DistanceSquared(a.Value, b.Value) <= POSITION_EPSILON;
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
    }
}

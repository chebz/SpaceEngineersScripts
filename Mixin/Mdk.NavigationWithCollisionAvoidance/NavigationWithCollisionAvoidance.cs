using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class NavigationWithCollisionAvoidance : Context
    {
        public enum NavigationState
        {
            Idle,
            Navigating,
            Stuck
        }

        public class NavigationWithCollisionAvoidanceSection : Section
        {
            public DoubleProperty DetourDistance { get; } = new DoubleProperty("DetourDistance", DETOUR_DISTANCE_DEFAULT);
            public DoubleProperty MinSpeed { get; } = new DoubleProperty("MinSpeed", MIN_SPEED_DEFAULT);
            public DoubleProperty SimpleNavigationDistance { get; } = new DoubleProperty("SimpleNavigationDistance", SIMPLE_NAVIGATION_DISTANCE_DEFAULT);
            public DoubleProperty DetourPrecision { get; } = new DoubleProperty("DetourPrecision", DETOUR_PRECISION_DEFAULT);

            public NavigationWithCollisionAvoidanceSection() : base("NavigationWithCollisionAvoidance")
            {
                _properties.Add(DetourDistance);
                _properties.Add(MinSpeed);
                _properties.Add(SimpleNavigationDistance);
                _properties.Add(DetourPrecision);
            }
        }

        #region Constants
        private const double DETOUR_DISTANCE_DEFAULT = 10.0; // meters
        private const double MIN_SPEED_DEFAULT = 5.0; // meters per second
        private const double SIMPLE_NAVIGATION_DISTANCE_DEFAULT = 2.0; // meters
        private const double DETOUR_PRECISION_DEFAULT = 1.0; // meters
        #endregion

        #region Fields
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private IMySensorBlock _fwdTopRightSensor;
        private IMySensorBlock _fwdTopLeftSensor;
        private IMySensorBlock _fwdBottomRightSensor;
        private IMySensorBlock _fwdBottomLeftSensor;
        private IMyCameraBlock _camera;
        private Alignment _alignment;
        private Navigation _navigation;
        private NavigationWithCollisionAvoidanceSection _section;
        private Vector3D _target;
        private bool _isDetour = false;
        private double _maxSpeed;
        private double _precision;
        private Vector3D _targetSpeed;
        private NavigationState _navigationState;
        #endregion

        #region Properties
        public bool IsInitialized { get; private set; } = false;
        #endregion

        #region Methods
        public bool Initialize(Program program, Alignment alignment, Navigation navigation, CustomDataConnector customDataConnector, out string errorMessage)
        {
            _program = program;
            _alignment = alignment;
            _navigation = navigation;
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No remote control found!";
                return false;
            }

            _camera = _program.GetLocalBlock<IMyCameraBlock>();
            if (_camera == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No camera found!";
                return false;
            }


            if (!InitializeSensors(out errorMessage))
            {
                return false;
            }

            _section = new NavigationWithCollisionAvoidanceSection();
            customDataConnector.AddSection(_section);
            TransitionTo(new IdleState(this));
            IsInitialized = true;
            errorMessage = string.Empty;
            return true;
        }

        public void NavigateTo(Vector3D target, double maxSpeed = 20.0, double precision = 0, Vector3D targetSpeed = default(Vector3D))
        {
            if (!IsInitialized)
            {
                _program.Echo("NavigationWithCollisionAvoidance: Not initialized!");
                return;
            }

            _maxSpeed = maxSpeed;
            _precision = precision;
            _targetSpeed = targetSpeed;
            if (_navigationState == NavigationState.Stuck || _navigationState == NavigationState.Idle)
            {
                _isDetour = false;
                _navigationState = NavigationState.Navigating;
                _camera.EnableRaycast = true;
                var distance = Vector3D.Distance(_remoteControl.GetPosition(), target);
                if (distance <= _section.SimpleNavigationDistance.Value)
                {
                    TransitionTo(new SimpleNavigatingState(this, target));
                }
                else
                {
                    TransitionTo(new NavigatingState(this, target));
                }
            }
            else if (!_isDetour)
            {
                if (CurrentState is SimpleNavigatingState)
                {
                    var simpleNavigatingState = (SimpleNavigatingState)CurrentState;
                    simpleNavigatingState.UpdateTarget(target);
                }
                else if (CurrentState is NavigatingState)
                {
                    var navigatingState = (NavigatingState)CurrentState;
                    navigatingState.UpdateTarget(target);
                }
            }
            _target = target;            
        }

        public void Stop()
        {
            _remoteControl.Log("CA: Stop");
            if (!IsInitialized)
                return;

            _navigationState = NavigationState.Idle;
            TransitionTo(new IdleState(this));
        }

        public NavigationState GetNavigationState()
        {
            return _navigationState;
        }

        private bool InitializeSensors(out string errorMessage)
        {
            errorMessage = string.Empty;
            var allSensors = _program.GetLocalBlocks<IMySensorBlock>();
            if (allSensors.Count < 4)
            {
                errorMessage = "NavigationWithCollisionAvoidance: Need at least 4 sensors for collision avoidance!";
                return false;
            }

            // Get grid's center of mass and remote control orientation
            var gridCenterOfMass = _remoteControl.CenterOfMass;
            var remoteMatrix = _remoteControl.WorldMatrix;
            var remoteForward = remoteMatrix.Forward;
            var remoteRight = remoteMatrix.Right;
            var remoteUp = remoteMatrix.Up;

            foreach (var sensor in allSensors)
            {
                var sensorPosition = sensor.GetPosition();
                var relativePosition = sensorPosition - gridCenterOfMass;

                // Calculate dot products to determine position relative to remote control orientation
                var forwardDot = Vector3D.Dot(relativePosition, remoteForward);
                var rightDot = Vector3D.Dot(relativePosition, remoteRight);
                var upDot = Vector3D.Dot(relativePosition, remoteUp);

                bool isForward = forwardDot > 0;
                bool isRight = rightDot > 0;
                bool isUp = upDot > 0;

                if (isForward && isRight && isUp) // Forward-Top-Right
                {
                    _fwdTopRightSensor = sensor;
                    sensor.CustomName = "Fwd Top Right Sensor";
                }
                else if (isForward && !isRight && isUp) // Forward-Top-Left
                {
                    _fwdTopLeftSensor = sensor;
                    sensor.CustomName = "Fwd Top Left Sensor";
                }
                else if (isForward && isRight && !isUp) // Forward-Bottom-Right
                {
                    _fwdBottomRightSensor = sensor;
                    sensor.CustomName = "Fwd Bottom Right Sensor";
                }
                else if (isForward && !isRight && !isUp) // Forward-Bottom-Left
                {
                    _fwdBottomLeftSensor = sensor;
                    sensor.CustomName = "Fwd Bottom Left Sensor";
                }
            }

            // Validate all sensors are found
            if (_fwdTopRightSensor == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No forward-top-right sensor found!";
                return false;
            }
            if (_fwdTopLeftSensor == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No forward-top-left sensor found!";
                return false;
            }
            if (_fwdBottomRightSensor == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No forward-bottom-right sensor found!";
                return false;
            }
            if (_fwdBottomLeftSensor == null)
            {
                errorMessage = "NavigationWithCollisionAvoidance: No forward-bottom-left sensor found!";
                return false;
            }

            IsInitialized = true;
            return true;
        }        
        #endregion

        #region State Machine Classes
        private class IdleState : State<NavigationWithCollisionAvoidance>
        {
            public IdleState(NavigationWithCollisionAvoidance context) : base(context) 
            { 
                _context._camera.EnableRaycast = false;
                _context._remoteControl.Log("CA: IdleState");
                _context._alignment.Stop();
                _context._navigation.Stop();
            }
        }


        private class ScanningState : State<NavigationWithCollisionAvoidance>
        {
            private List<Vector3D> _scannedPositions = new List<Vector3D>();
            private Vector3D _targetPosition;

            public ScanningState(NavigationWithCollisionAvoidance context, Vector3D targetPosition) : base(context) 
            { 
                _context._remoteControl.Log("CA: Scanning");
                _targetPosition = targetPosition; 
            }

                private bool IsScannedPosition(Vector3D position)
                {
                    foreach (var scannedPosition in _scannedPositions)
                    {
                        if (Vector3D.Distance(scannedPosition, position) < 0.1)
                        {
                            return true;
                        }
                    }
                    return false;
                }

            public override void Execute()
            {
                // Align with target first
                if (!_context._alignment.AlignWithTarget(_targetPosition))
                {
                    return;
                }

                // Check for obstacles during scanning
                bool tr = _context._fwdTopRightSensor.IsActive;
                bool tl = _context._fwdTopLeftSensor.IsActive;
                bool br = _context._fwdBottomRightSensor.IsActive;
                bool bl = _context._fwdBottomLeftSensor.IsActive;

                // no sensors triggered, we can move forward
                if (!tr && !tl && !br && !bl)
                {
                    _context._isDetour = true;
                    var distance = Vector3D.Distance(_context._remoteControl.GetPosition(), _targetPosition);
                    if (distance <= _context._section.SimpleNavigationDistance.Value)
                    {
                        _context.TransitionTo(new SimpleNavigatingState(_context, _targetPosition));
                    }
                    else
                    {
                        _context.TransitionTo(new NavigatingState(_context, _targetPosition));
                    }
                    return;
                }
                var remoteMatrix = _context._remoteControl.WorldMatrix;
                var forward = remoteMatrix.Forward;
                var right = remoteMatrix.Right;
                var up = remoteMatrix.Up;
                
                // Calculate all 6 positions at 45 degrees from forward vector
                var myPos = _context._remoteControl.GetPosition();
                var detourDistance = _context._section.DetourDistance.Value;
                var centerRightPosition = myPos + (forward + right).Normalized() * detourDistance;
                var topRightPosition = myPos + (forward + right + up).Normalized() * detourDistance;
                var bottomRightPosition = myPos + (forward + right - up).Normalized() * detourDistance;
                var centerLeftPosition = myPos + (forward - right).Normalized() * detourDistance;
                var bottomLeftPosition = myPos + (forward - right - up).Normalized() * detourDistance;
                var topLeftPosition = myPos + (forward - right + up).Normalized() * detourDistance;
                var topCenterPosition = myPos + (forward + up).Normalized() * detourDistance;
                var bottomCenterPosition = myPos + (forward - up).Normalized() * detourDistance;
                // check if we can rotate center right
                if (!tr && !br && !IsScannedPosition(centerRightPosition))
                {
                    _scannedPositions.Add(centerRightPosition);
                    _targetPosition = centerRightPosition;
                    return;
                }
                // check if we can rotate center left
                if (!tl && !bl && !IsScannedPosition(centerLeftPosition))
                {
                    _scannedPositions.Add(centerLeftPosition);
                    _targetPosition = centerLeftPosition;
                    return;
                }
                // check if we can rotate top center
                if (!tr && !tl && !IsScannedPosition(topCenterPosition))
                {
                    _scannedPositions.Add(topCenterPosition);
                    _targetPosition = topCenterPosition;
                    return;
                }
                // check if we can rotate bottom center
                if (!br && !bl && !IsScannedPosition(bottomCenterPosition))
                {
                    _scannedPositions.Add(bottomCenterPosition);
                    _targetPosition = bottomCenterPosition;
                    return;
                }
                // check if we can rotate top right
                if (!tr && !tl && !IsScannedPosition(topRightPosition))
                {
                    _scannedPositions.Add(topRightPosition);
                    _targetPosition = topRightPosition;
                    return;
                }
                // check if we can rotate bottom right
                if (!br && !bl && !IsScannedPosition(bottomRightPosition))
                {
                    _scannedPositions.Add(bottomRightPosition);
                    _targetPosition = bottomRightPosition;
                    return;
                }
                // check if we can rotate top left
                if (!tr && !tl && !IsScannedPosition(topLeftPosition))
                {
                    _scannedPositions.Add(topLeftPosition);
                    _targetPosition = topLeftPosition;
                    return;
                }
                // check if we can rotate bottom left
                if (!br && !bl && !IsScannedPosition(bottomLeftPosition))
                {
                    _scannedPositions.Add(bottomLeftPosition);
                    _targetPosition = bottomLeftPosition;
                    return;
                }
                // no free space found, rotate 90 degrees in unscanned direction and try again
                // right 90 degrees
                var right90Position = myPos + right.Normalized() * detourDistance;
                if (!IsScannedPosition(right90Position))
                {
                    _scannedPositions.Add(right90Position);
                    _targetPosition = right90Position;
                    return;
                }
                // left 90 degrees
                var left90Position = myPos - right.Normalized() * detourDistance;
                if (!IsScannedPosition(left90Position))
                {
                    _scannedPositions.Add(left90Position);
                    _targetPosition = left90Position;
                    return;
                }
                // top 90 degrees
                var top90Position = myPos + up.Normalized() * detourDistance;
                if (!IsScannedPosition(top90Position))
                {
                    _scannedPositions.Add(top90Position);
                    _targetPosition = top90Position;
                    return;
                }
                // bottom 90 degrees
                var bottom90Position = myPos - up.Normalized() * detourDistance;
                if (!IsScannedPosition(bottom90Position))
                {
                    _scannedPositions.Add(bottom90Position);
                    _targetPosition = bottom90Position;
                    return;
                }
                // no free space found, stop and handle
                _context._program.Echo("NavigationWithCollisionAvoidance: Stuck!");
                _context._navigationState = NavigationState.Stuck;
                _context.TransitionTo(new IdleState(_context));
            }
        }

        private class NavigatingState : State<NavigationWithCollisionAvoidance>
        {
            private Vector3D _targetPosition;

            public NavigatingState(NavigationWithCollisionAvoidance context, Vector3D targetPosition) : base(context)
            {
                _context._remoteControl.Log("CA: Navigating");
                _targetPosition = targetPosition;
            }

            public void UpdateTarget(Vector3D newTarget)
            {
                _targetPosition = newTarget;
            }

            public override void Execute()
            {
                _context._alignment.AlignWithTarget(_targetPosition);

                var tr = _context._fwdTopRightSensor.IsActive;
                var tl = _context._fwdTopLeftSensor.IsActive;
                var br = _context._fwdBottomRightSensor.IsActive;
                var bl = _context._fwdBottomLeftSensor.IsActive;
                var anyActive = tr || tl || br || bl;
                if (anyActive)
                {
                    _context._navigation.Stop();
                    _context.TransitionTo(new ScanningState(_context, _targetPosition));
                    return;
                }

                var distance = Vector3D.Distance(_context._remoteControl.GetPosition(), _targetPosition);
                var maxSpeed = Math.Max(_context._section.MinSpeed.Value, Math.Min(_context._maxSpeed, distance / 10.0));

                var raycastDistance = maxSpeed * 4.0;
                
                var bounds = _context._remoteControl.CubeGrid.WorldVolume.Radius;

                double closestDistance;
                if (CheckObstaclesWithRaycast(raycastDistance, bounds, out closestDistance))
                {
                    maxSpeed = Math.Max(_context._section.MinSpeed.Value, Math.Min(maxSpeed, closestDistance / 5.0));
                }

                var precision = _context._isDetour ? _context._section.DetourPrecision.Value : _context._precision;
                var targetSpeed = _context._isDetour ? Vector3D.Zero : _context._targetSpeed;
                if (_context._navigation.NavigateTo(_targetPosition, maxSpeed, precision, targetSpeed))
                {
                    if (_context._isDetour)
                    {
                        _context._isDetour = false;
                        var distanceToTarget = Vector3D.Distance(_context._remoteControl.GetPosition(), _context._target);
                        if (distanceToTarget <= _context._section.SimpleNavigationDistance.Value)
                        {
                            _context.TransitionTo(new SimpleNavigatingState(_context, _context._target));
                        }
                        else
                        {
                            _targetPosition = _context._target;
                        }
                    }
                    else
                    {
                        _context._navigationState = NavigationState.Idle;
                        _context.TransitionTo(new IdleState(_context));
                    }
                }
            }

            private bool CheckObstaclesWithRaycast(double distance, double bounds, out double closestDistance)
            {
                if (_context._camera == null)
                {
                    _context._program.Echo("No camera found for raycast!");
                    closestDistance = 0;
                    return false;
                }

                var camera = _context._camera;
                    var cameraPos = camera.GetPosition();
                    var forward = camera.WorldMatrix.Forward;
                    var right = camera.WorldMatrix.Right;
                    var up = camera.WorldMatrix.Up;

                    // Calculate the 9 raycast points in a square pattern
                    var raycastPoints = new Vector3D[9];
                    var halfBounds = bounds * 0.5;
                    
                    // Center point
                    raycastPoints[0] = cameraPos + forward * distance;
                    
                    // Corner points
                    raycastPoints[1] = cameraPos + forward * distance + up * halfBounds + right * halfBounds;      // Top-Right
                    raycastPoints[2] = cameraPos + forward * distance + up * halfBounds - right * halfBounds;     // Top-Left
                    raycastPoints[3] = cameraPos + forward * distance - up * halfBounds + right * halfBounds;     // Bottom-Right
                    raycastPoints[4] = cameraPos + forward * distance - up * halfBounds - right * halfBounds;     // Bottom-Left
                    
                    // Edge center points
                    raycastPoints[5] = cameraPos + forward * distance + up * halfBounds;                         // Top-Center
                    raycastPoints[6] = cameraPos + forward * distance - up * halfBounds;                         // Bottom-Center
                    raycastPoints[7] = cameraPos + forward * distance + right * halfBounds;                      // Right-Center
                    raycastPoints[8] = cameraPos + forward * distance - right * halfBounds;                       // Left-Center
                    
                    closestDistance = double.MaxValue;
                    // Perform raycasts
                    foreach (var targetPoint in raycastPoints)
                    {
                        var direction = Vector3D.Normalize(targetPoint - cameraPos);
                        // Transform direction to camera's local space
                        var cameraMatrix = camera.WorldMatrix;
                        var localDirection = Vector3D.TransformNormal(direction, MatrixD.Transpose(cameraMatrix));
                        var hitInfo = camera.Raycast(distance, localDirection);
                        
                        if (hitInfo.IsEmpty() == false)
                        {
                            var hitDistance = Vector3D.Distance(cameraPos, hitInfo.Position);
                            closestDistance = Math.Min(closestDistance, hitDistance);
                        }
                    }
                return closestDistance <= distance;
            }
        }

        private class SimpleNavigatingState : State<NavigationWithCollisionAvoidance>
        {
            private Vector3D _targetPosition;

            public SimpleNavigatingState(NavigationWithCollisionAvoidance context, Vector3D targetPosition) : base(context)
            {
                _context._remoteControl.Log("CA: Simple Navigating");
                _targetPosition = targetPosition;
            }

            public void UpdateTarget(Vector3D newTarget)
            {
                _targetPosition = newTarget;
            }

            public override void Execute()
            {
                var precision = _context._isDetour ? _context._section.DetourPrecision.Value : _context._precision;
                var targetSpeed = _context._isDetour ? Vector3D.Zero : _context._targetSpeed;
                if (_context._navigation.NavigateTo(_targetPosition, _context._maxSpeed, precision, targetSpeed))
                {
                    if (_context._isDetour)
                    {
                        _context._isDetour = false;
                        var distance = Vector3D.Distance(_context._remoteControl.GetPosition(), _context._target);
                        if (distance <= _context._section.SimpleNavigationDistance.Value)
                        {
                            _targetPosition = _context._target;
                        }
                        else
                        {
                            _context.TransitionTo(new NavigatingState(_context, _context._target));
                        }
                    }
                    else
                    {
                        _context._program.Echo("NavigationWithCollisionAvoidance: Arrived!");
                        _context._navigationState = NavigationState.Idle;
                        _context.TransitionTo(new IdleState(_context));
                    }
                }
            }
        }
        #endregion
    }
}
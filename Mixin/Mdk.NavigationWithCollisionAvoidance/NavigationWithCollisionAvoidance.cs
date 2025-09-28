using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class NavigationWithCollisionAvoidance
    {
        public enum NavigationState
        {
            Idle,
            Navigating,
            Stuck
        }

        #region Constants
        private const double DETOUR_DISTANCE = 10.0; // meters
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
        private NavigationContext _context;
        #endregion

        #region Properties
        public bool IsInitialized { get; private set; } = false;
        #endregion

        #region Methods
        public bool Initialize(Program program, Alignment alignment, Navigation navigation, out string errorMessage)
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

            _context = new NavigationContext(this);
            IsInitialized = true;
            return true;
        }

        public void NavigateTo(Vector3D target, double maxSpeed = 20.0)
        {
            if (!IsInitialized)
            {
                _program.Echo("NavigationWithCollisionAvoidance: Not initialized!");
                return;
            }

            _context.Start(target, maxSpeed);
        }

        public void Stop()
        {
            if (IsInitialized)
            {
                _context.Stop();
            }
        }

        public void Execute()
        {
            if (IsInitialized)
            {
                _context.Execute();
            }
        }

        public NavigationState GetNavigationState()
        {
            return _context.GetNavigationState();
        }

        private bool InitializeSensors(out string errorMessage)
        {
            errorMessage = string.Empty;
            var allSensors = new List<IMySensorBlock>();
            _program.GridTerminalSystem.GetBlocksOfType<IMySensorBlock>(allSensors);

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
        public class NavigationContext : Context
        {

            private NavigationWithCollisionAvoidance _navigation;
            private Vector3D _target;
            private bool _isDetour = false;
            private double _maxSpeed;
            private NavigationState _navigationState;

            public NavigationContext(NavigationWithCollisionAvoidance navigation)
            {
                _navigation = navigation;
                TransitionTo(new IdleState(this));
            }

            public void Start(Vector3D destination, double maxSpeed = 20.0)
            {
                if (!(CurrentState is IdleState))
                {
                    Stop();
                }

                _target = destination;
                _maxSpeed = maxSpeed;
                _isDetour = false;
                _navigationState = NavigationState.Navigating;
                _navigation._camera.EnableRaycast = true;
                TransitionTo(new AligningState(this, destination));
            }

            public void Stop()
            {
                _navigationState = NavigationState.Idle;
                TransitionTo(new IdleState(this));
            }

            public NavigationState GetNavigationState()
            {
                return _navigationState;
            }

            private class IdleState : State<NavigationContext>
            {
                public IdleState(NavigationContext context) : base(context) 
                { 
                    context._navigation._camera.EnableRaycast = false;
                    context._navigation._program.Echo("IdleState");
                    context._navigation._alignment.Stop();
                    context._navigation._navigation.Stop();
                }
            }

            private class AligningState : State<NavigationContext>
            {
                private Vector3D _targetPosition;

                public AligningState(NavigationContext context, Vector3D targetPosition) : base(context)
                {
                    _context._navigation._program.Echo("Aligning");
                    _targetPosition = targetPosition;
                }

                public override void Execute()
                {
                    if (_context._navigation._alignment.AlignWithTarget(_targetPosition))
                    {
                        _context.TransitionTo(new NavigatingState(_context, _targetPosition));
                    }
                }
            }

            private class ScanningState : State<NavigationContext>
            {
                private List<Vector3D> _scannedPositions = new List<Vector3D>();
                private Vector3D _targetPosition;

                public ScanningState(NavigationContext context, Vector3D targetPosition) : base(context) 
                { 
                    _context._navigation._program.Echo("Scanning");
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
                    if (!_context._navigation._alignment.AlignWithTarget(_targetPosition))
                    {
                        return;
                    }

                    // Check for obstacles during scanning
                    bool tr = _context._navigation._fwdTopRightSensor.IsActive;
                    bool tl = _context._navigation._fwdTopLeftSensor.IsActive;
                    bool br = _context._navigation._fwdBottomRightSensor.IsActive;
                    bool bl = _context._navigation._fwdBottomLeftSensor.IsActive;

                    // no sensors triggered, we can move forward
                    if (!tr && !tl && !br && !bl)
                    {
                        _context._isDetour = true;
                        _context.TransitionTo(new NavigatingState(_context, _targetPosition));
                        return;
                    }
                     var remoteMatrix = _context._navigation._remoteControl.WorldMatrix;
                     var forward = remoteMatrix.Forward;
                     var right = remoteMatrix.Right;
                     var up = remoteMatrix.Up;
                     
                     // Calculate all 6 positions at 45 degrees from forward vector
                     var myPos = _context._navigation._remoteControl.GetPosition();
                     var centerRightPosition = myPos + (forward + right).Normalized() * DETOUR_DISTANCE;
                     var topRightPosition = myPos + (forward + right + up).Normalized() * DETOUR_DISTANCE;
                     var bottomRightPosition = myPos + (forward + right - up).Normalized() * DETOUR_DISTANCE;
                     var centerLeftPosition = myPos + (forward - right).Normalized() * DETOUR_DISTANCE;
                     var bottomLeftPosition = myPos + (forward - right - up).Normalized() * DETOUR_DISTANCE;
                     var topLeftPosition = myPos + (forward - right + up).Normalized() * DETOUR_DISTANCE;
                     var topCenterPosition = myPos + (forward + up).Normalized() * DETOUR_DISTANCE;
                     var bottomCenterPosition = myPos + (forward - up).Normalized() * DETOUR_DISTANCE;
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
                    var right90Position = myPos + right.Normalized() * DETOUR_DISTANCE;
                    if (!IsScannedPosition(right90Position))
                    {
                        _scannedPositions.Add(right90Position);
                        _targetPosition = right90Position;
                        return;
                    }
                    // left 90 degrees
                    var left90Position = myPos - right.Normalized() * DETOUR_DISTANCE;
                    if (!IsScannedPosition(left90Position))
                    {
                        _scannedPositions.Add(left90Position);
                        _targetPosition = left90Position;
                        return;
                    }
                    // top 90 degrees
                    var top90Position = myPos + up.Normalized() * DETOUR_DISTANCE;
                    if (!IsScannedPosition(top90Position))
                    {
                        _scannedPositions.Add(top90Position);
                        _targetPosition = top90Position;
                        return;
                    }
                    // bottom 90 degrees
                    var bottom90Position = myPos - up.Normalized() * DETOUR_DISTANCE;
                    if (!IsScannedPosition(bottom90Position))
                    {
                        _scannedPositions.Add(bottom90Position);
                        _targetPosition = bottom90Position;
                        return;
                    }
                    // no free space found, stop and handle
                    _context._navigation._program.Echo("NavigationWithCollisionAvoidance: Stuck!");
                    _context._navigationState = NavigationState.Stuck;
                    _context.TransitionTo(new IdleState(_context));
                }
            }

            private class NavigatingState : State<NavigationContext>
            {
                private Vector3D _targetPosition;

                public NavigatingState(NavigationContext context, Vector3D targetPosition) : base(context)
                {
                    _context._navigation._program.Echo("Navigating");
                    _targetPosition = targetPosition;
                }

                public override void Execute()
                {
                    var tr = _context._navigation._fwdTopRightSensor.IsActive;
                    var tl = _context._navigation._fwdTopLeftSensor.IsActive;
                    var br = _context._navigation._fwdBottomRightSensor.IsActive;
                    var bl = _context._navigation._fwdBottomLeftSensor.IsActive;
                    var anyActive = tr || tl || br || bl;
                    if (anyActive)
                    {
                        // Obstacle detected - stop navigation and transition to scanning
                        _context._navigation._navigation.Stop();
                        _context.TransitionTo(new ScanningState(_context, _targetPosition));
                        return;
                    }

                    // limit max speed based on distance to target
                    var distance = Vector3D.Distance(_context._navigation._remoteControl.GetPosition(), _targetPosition);
                    var maxSpeed = Math.Max(2.0, Math.Min(_context._maxSpeed, distance / 5.0));

                    var raycastDistance = maxSpeed * 4.0;
                    
                    // Get ship orientation and bounds
                    var bounds = _context._navigation._remoteControl.CubeGrid.WorldVolume.Radius;

                    double closestDistance;
                    if (CheckObstaclesWithRaycast(raycastDistance, bounds, out closestDistance))
                    {
                        // Obstacle detected - slow down
                        maxSpeed = Math.Max(2.0, Math.Min(maxSpeed, closestDistance / 5.0));
                        _context._navigation._program.Echo($"Obstacle detected at distance: {closestDistance:F1}m, slowing down to {maxSpeed:F1}m/s");
                    }

                    // Clear path - continue navigation at full speed
                    if (_context._navigation._navigation.NavigateTo(_targetPosition, maxSpeed))
                    {
                        if (_context._isDetour)
                        {
                            _context._isDetour = false;
                            _context.TransitionTo(new AligningState(_context, _context._target));
                        }
                        else
                        {
                            _context._navigation._program.Echo("NavigationWithCollisionAvoidance: Arrived!");
                            _context.TransitionTo(new IdleState(_context));
                        }
                    }
                }

                private bool CheckObstaclesWithRaycast(double distance, double bounds, out double closestDistance)
                {
                    if (_context._navigation._camera == null)
                    {
                        _context._navigation._program.Echo("No camera found for raycast!");
                        closestDistance = 0;
                        return false;
                    }

                    var camera = _context._navigation._camera;
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
        }
        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class TiltNavigationImproved
    {
        #region Constants
        private const double PRECISION_DEFAULT = 1;
        private const double ALIGNMENT_PRECISION_DEFAULT = 0.01;
        private const double ACCEL_DEFAULT = 0.25;
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;
        private const double KP_TILT_DEFAULT = 2.0;
        private const double KI_TILT_DEFAULT = 0.1;
        private const double KD_TILT_DEFAULT = 0.5;
        private const double MAX_TILT_DEGREES = 15.0;
        private const double MAX_SPEED_ERROR = 10.0;
        
        // Improved constants for better stability
        private const double DAMPING_FACTOR = 0.5;  // Increased damping (lower value = more damping)
        private const double MIN_TILT_THRESHOLD = 0.03;  // Increased threshold to prevent micro-adjustments
        private const double APPROACH_SLOWDOWN_DISTANCE = 50.0;
        private const double DEADBAND = 0.5;  // Increased deadband to reduce constant adjustments
        private const double POSITION_STABILIZATION_FACTOR = 0.2;  // New factor for position stabilization
        private const double FINAL_APPROACH_DISTANCE = 5.0;  // Distance at which to use extra stabilization
        #endregion

        #region Fields
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private Alignment _alignment;
        private Navigation _navigation;
        private Vector3D _currentVelocity;
        private Vector3D _desiredVelocity;
        private Vector3D _lastVelError = Vector3D.Zero;
        private DateTime _lastUpdateTime = DateTime.Now;
        private Vector3D _lastPosition = Vector3D.Zero;
        private bool _isInitialized = false;
        #endregion

        #region Properties
        public double Precision { get; set; } = PRECISION_DEFAULT;
        public double AlignmentPrecision { get; set; } = ALIGNMENT_PRECISION_DEFAULT;
        public double Accel { get; set; } = ACCEL_DEFAULT;
        public PID PidTilt { get; } = new PID(KP_TILT_DEFAULT, KI_TILT_DEFAULT, KD_TILT_DEFAULT, PID_TIME_STEP_DEFAULT);
        public double MaxSpeedError { get; set; } = MAX_SPEED_ERROR;
        #endregion

        #region Methods
        public bool Initialize(Program program, Alignment alignment, Navigation navigation, out string errorMessage)
        {
            _program = program;
            _alignment = alignment;
            _navigation = navigation;
            errorMessage = string.Empty;

            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "TiltNavigation: No remote control found!";
                return false;
            }

            _lastPosition = _remoteControl.GetPosition();
            _isInitialized = true;
            return true;
        }

        public void Stop()
        {
            // Just reset alignment - Navigation handles thrusters
            _alignment.Stop();
        }

        public Vector3D GetCurrentPosition()
        {
            return _remoteControl.GetPosition();
        }

        public Vector3D GetCurrentVelocity()
        {
            return _remoteControl.GetShipVelocities().LinearVelocity;
        }

        public bool NavigateTo(Vector3D target, double maxSpeed = 20.0)
        {
            // Original NavigateTo implementation
            _currentVelocity = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            var currentPos = _remoteControl.GetPosition();

            var desiredSpeed = CalculateDesiredSpeedAtPosition(target, maxSpeed);
            var desiredVelocity = Vector3D.Normalize(target - currentPos) * desiredSpeed;

            var up = -Vector3D.Normalize(gravity);
            var fwd = _remoteControl.WorldMatrix.Forward;
            var right = _remoteControl.WorldMatrix.Right;
            
            // Add scaled distance error to current velocity
            var velError = _currentVelocity - desiredVelocity;

            var velErrorH = velError - Vector3D.Dot(velError, up) * up;
            var velErrorHDir = Vector3D.Normalize(velErrorH);
            var velErrorMag = velErrorH.Length();
            
            var velErrorDir = Vector3D.Normalize(velErrorH);
            // Direct YPR approach: set yaw based on velocity error direction, pitch based on magnitude
            double targetPitch, targetRoll;
            
            double yaw, pitch, roll;
            _alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
            var breakingDistance = MaxSpeedError / Accel;
            var distance = Vector3D.Distance(currentPos, target);
            
            if (velErrorMag > 0.001)
            {
                // Calculate yaw and pitch based on velocity error direction
                var rightDir = _remoteControl.WorldMatrix.Right;
                var angleRatio  = Math.Min(velErrorMag, MaxSpeedError) / MaxSpeedError;
                
                // Calculate targetPitch
                targetPitch = Vector3D.Dot(velErrorDir, fwd) * angleRatio;
                // clamp between +- 15 degrees
                targetPitch = MathHelper.Clamp(targetPitch, -15.0 * Math.PI / 180.0, 15.0 * Math.PI / 180.0);
                // Calculate targetRoll
                targetRoll = -Vector3D.Dot(velErrorDir, right) * angleRatio;
                // clamp between +- 15 degrees
                targetRoll = MathHelper.Clamp(targetRoll, -15.0 * Math.PI / 180.0, 15.0 * Math.PI / 180.0);
                 _program.Echo($"Fwd: {fwd.X:F2}, {fwd.Y:F2}, {fwd.Z:F2}");
                 _program.Echo($"Right: {right.X:F2}, {right.Y:F2}, {right.Z:F2}");
                 _program.Echo($"Vel Error Dir: {velErrorDir.X:F2}, {velErrorDir.Y:F2}, {velErrorDir.Z:F2}");
            }
            else
            {
                targetPitch = 0.0;
                targetRoll = 0.0;
                
                _program.Echo($"No velocity error - keeping current orientation");
            }
        
            // Convert clamped YPR back to matrix
            var clampedMatrix = _alignment.YawPitchRollToWorldMatrix(yaw, targetPitch, targetRoll);
            
            // Align the drone with clamped matrix
            _alignment.AlignWithWorldMatrix(clampedMatrix);

            var vectorToTarget = target - currentPos;
            // project vectorToTarget onto up
            var vectorToTargetProjected = Vector3D.Dot(vectorToTarget, up) * up;
            var targetAboveOrBelow = vectorToTargetProjected + currentPos;
            // Use the Navigation class for thrust control
            _navigation.NavigateTo(targetAboveOrBelow, maxSpeed);
            return distance < Precision;
        }

        public bool NavigateTo2(Vector3D target, double maxSpeed = 20.0)
        {
            if (!_isInitialized)
            {
                _program.Echo("TiltNavigation not initialized!");
                return false;
            }
            
            // Get current state
            var currentPos = _remoteControl.GetPosition();
            var currentVel = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            var distance = Vector3D.Distance(currentPos, target);
            
            // Calculate time delta for smooth transitions
            var now = DateTime.Now;
            var deltaTime = (now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;
            
            // Normalize gravity direction for "up" reference
            var up = Vector3D.Zero;
            if (gravity.LengthSquared() > 0.1)
            {
                up = -Vector3D.Normalize(gravity);
            }
            else
            {
                // If no gravity, use ship's up vector
                up = _remoteControl.WorldMatrix.Up;
            }
            
            // Calculate desired velocity based on distance and max speed
            double desiredSpeed = CalculateDesiredSpeedWithApproach(target, maxSpeed);
            Vector3D directionToTarget = Vector3D.Normalize(target - currentPos);
            Vector3D desiredVelocity = directionToTarget * desiredSpeed;
            
            // Calculate velocity error (horizontal component only)
            Vector3D velocityError = currentVel - desiredVelocity;
            
            // Remove vertical component to focus on horizontal movement
            Vector3D horizontalError = velocityError - Vector3D.Dot(velocityError, up) * up;
            double errorMagnitude = horizontalError.Length();
            
            // Get current orientation
            double yaw, pitch, roll;
            _alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
            
            // Reference vectors from ship's current orientation
            var forward = _remoteControl.WorldMatrix.Forward;
            var right = _remoteControl.WorldMatrix.Right;
            
            // Calculate target pitch and roll with deadband and damping
            double targetPitch = 0;
            double targetRoll = 0;
            
            // Special handling for final approach - use stronger stabilization
            bool inFinalApproach = distance < FINAL_APPROACH_DISTANCE;
            double effectiveDeadband = inFinalApproach ? DEADBAND * 1.5 : DEADBAND;
            double effectiveDamping = inFinalApproach ? DAMPING_FACTOR * 0.8 : DAMPING_FACTOR;
            
            if (errorMagnitude > effectiveDeadband)
            {
                // Normalize the horizontal error
                Vector3D errorDirection = Vector3D.Normalize(horizontalError);
                
                // Calculate how much to tilt based on error magnitude (with damping)
                double tiltFactor = Math.Min(errorMagnitude / MaxSpeedError, 1.0) * MAX_TILT_DEGREES * (Math.PI / 180.0);
                
                // Apply damping based on rate of change of error
                Vector3D errorDelta = horizontalError - _lastVelError;
                double dampingMultiplier = 1.0;
                
                if (deltaTime > 0.001 && _lastVelError.LengthSquared() > 0.001)
                {
                    // If error is decreasing, apply more damping
                    if (Vector3D.Dot(errorDelta, horizontalError) < 0)
                    {
                        dampingMultiplier = effectiveDamping;
                    }
                }
                
                // Calculate pitch (forward/backward tilt)
                targetPitch = Vector3D.Dot(errorDirection, forward) * tiltFactor * dampingMultiplier;
                
                // Calculate roll (left/right tilt)
                targetRoll = -Vector3D.Dot(errorDirection, right) * tiltFactor * dampingMultiplier;
                
                // Apply minimum threshold to avoid micro-adjustments
                if (Math.Abs(targetPitch) < MIN_TILT_THRESHOLD)
                    targetPitch = 0;
                    
                if (Math.Abs(targetRoll) < MIN_TILT_THRESHOLD)
                    targetRoll = 0;
                
                // Apply position stabilization when close to target
                if (inFinalApproach && currentVel.Length() < 1.0)
                {
                    // Calculate position drift since last frame
                    Vector3D positionDelta = currentPos - _lastPosition;
                    Vector3D horizontalDrift = positionDelta - Vector3D.Dot(positionDelta, up) * up;
                    
                    // Apply counter-tilt to resist drift
                    if (horizontalDrift.LengthSquared() > 0.001)
                    {
                        Vector3D driftDirection = Vector3D.Normalize(horizontalDrift);
                        double driftCompensation = Math.Min(horizontalDrift.Length() * POSITION_STABILIZATION_FACTOR, 0.05);
                        
                        // Apply drift compensation to pitch and roll
                        targetPitch -= Vector3D.Dot(driftDirection, forward) * driftCompensation;
                        targetRoll += Vector3D.Dot(driftDirection, right) * driftCompensation;
                    }
                }
                
                _program.Echo($"Error: {errorMagnitude:F2}m/s");
                _program.Echo($"Tilt: P={targetPitch*180/Math.PI:F1}° R={targetRoll*180/Math.PI:F1}°");
                _program.Echo($"Dist: {distance:F1}m");
            }
            else
            {
                _program.Echo("Within deadband - maintaining level");
                _program.Echo($"Dist: {distance:F1}m");
            }
            
            // Store current error for next iteration
            _lastVelError = horizontalError;
            _lastPosition = currentPos;
            
            // Create orientation matrix with calculated tilt
            var targetMatrix = _alignment.YawPitchRollToWorldMatrix(yaw, targetPitch, targetRoll);
            _alignment.AlignWithWorldMatrix(targetMatrix);
            
            // Handle vertical navigation separately
            var verticalTarget = currentPos + Vector3D.Dot(target - currentPos, up) * up;
            _navigation.NavigateTo(verticalTarget, maxSpeed);
            
            // Return true when we're close enough to target
            return distance < Precision;
        }

        private double CalculateDesiredSpeedAtPosition(Vector3D target, double velocity)
        {
            var currentPos = _remoteControl.GetPosition();
            var distance = Vector3D.Distance(currentPos, target);
            var breakingDistance = velocity / Accel;
            if (distance > breakingDistance)
            {
                return velocity;
            }
            return distance / breakingDistance * velocity;
        }
        
        private double CalculateDesiredSpeedWithApproach(Vector3D target, double maxSpeed)
        {
            var currentPos = _remoteControl.GetPosition();
            var distance = Vector3D.Distance(currentPos, target);
            
            // Calculate braking distance based on acceleration capability
            var brakingDistance = (maxSpeed * maxSpeed) / (2 * Accel);
            
            // Start slowing down earlier for smoother approach
            var approachDistance = Math.Max(brakingDistance, APPROACH_SLOWDOWN_DISTANCE);
            
            if (distance > approachDistance)
            {
                // Full speed when far away
                return maxSpeed;
            }
            else if (distance < Precision * 2)
            {
                // Very slow when very close - reduced from previous version
                return Math.Min(0.2, maxSpeed * 0.02);
            }
            else if (distance < FINAL_APPROACH_DISTANCE)
            {
                // Extra slow approach in final stage - cubic function for smoother deceleration
                double ratio = distance / FINAL_APPROACH_DISTANCE;
                return maxSpeed * 0.1 * ratio * ratio * ratio;
            }
            else
            {
                // Gradual slowdown using square root for smoother deceleration curve
                return maxSpeed * Math.Sqrt(distance / approachDistance);
            }
        }
        #endregion
    }
}
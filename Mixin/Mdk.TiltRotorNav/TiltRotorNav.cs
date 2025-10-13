
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class TiltRotorNav
    {
        #region Constants
        public const double MAX_YAW_PITCH_ROLL_ANGLE = 25.0 * Math.PI / 180.0; // 15 degrees in radians
        public const double ROTOR_ROTATION_SPEED_DEFAULT = 5.0;

        private const double PRECISION_DEFAULT = 1;
        private const double ALIGNMENT_PRECISION_DEFAULT = 0.01;
        private const double ACCEL_DEFAULT = 0.25;
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;
        private const double KP_TILT_DEFAULT = 2.0;
        private const double KI_TILT_DEFAULT = 0.1;
        private const double KD_TILT_DEFAULT = 0.5;
        private const double MAX_SPEED_ERROR = 10.0;
        private const double ROTOR_ROTATION_SPEED_EXP_DEFAULT = 0.5;
        private const double MINIMUM_YAW_DISTANCE_DEFAULT = 0.5;
        #endregion

        #region Fields
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private Alignment _alignment;
        private Navigation _navigation;
        private Vector3D _currentVelocity;
        private Vector3D _desiredVelocity;

        // Rotor collections
        private List<IMyMotorStator> _pitchRotors = new List<IMyMotorStator>();
        private List<IMyMotorStator> _rollRotors = new List<IMyMotorStator>();
        private Dictionary<IMyMotorStator, double> _rotorTargetAngles = new Dictionary<IMyMotorStator, double>();

        // Thruster collections
        private List<IMyThrust> _thrusters = new List<IMyThrust>();
        private PID _pidVertical = new PID(5.0, 0, 0, 1.0 / 6.0);
        
        // Vertical control constants
        private const double HOVER_DISTANCE_THRESHOLD = 0.3; // meters
        private const double HOVER_VELOCITY_THRESHOLD = 0.2; // m/s
        private const double MAX_VERTICAL_ACCELERATION = 3.0; // m/s² (reduced to prevent space flight)
        private const double BREAKING_DISTANCE_FACTOR = 0.5; // Reduce error when close to target
        
        // Cached position for yaw alignment
        private Vector3D? _cachedStartPosition = null;
        #endregion

        #region Properties
        public double Precision { get; set; } = PRECISION_DEFAULT;
        public double AlignmentPrecision { get; set; } = ALIGNMENT_PRECISION_DEFAULT;
        public double Accel { get; set; } = ACCEL_DEFAULT;
        public PID PidTilt { get; } = new PID(KP_TILT_DEFAULT, KI_TILT_DEFAULT, KD_TILT_DEFAULT, PID_TIME_STEP_DEFAULT);
        public double MaxSpeedError { get; set; } = MAX_SPEED_ERROR;
        public double RotorRotationSpeed { get; set; } = ROTOR_ROTATION_SPEED_DEFAULT;
        public double RotorRotationSpeedExp { get; set; } = ROTOR_ROTATION_SPEED_EXP_DEFAULT;
        public double MinimumYawDistance { get; set; } = MINIMUM_YAW_DISTANCE_DEFAULT;
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
                errorMessage = "TiltRotorNav: No remote control found!";
                return false;
            }

            // Find rotors and thrusters based on position relative to remote control
            if (!FindRotorsAndThrusters(out errorMessage))
                return false;

            // Enable rotors and set limits for full rotation
            foreach (var rotor in _pitchRotors.Concat(_rollRotors))
            {
                rotor.UpperLimitRad = (float)MAX_YAW_PITCH_ROLL_ANGLE;
                rotor.LowerLimitRad = (float)-MAX_YAW_PITCH_ROLL_ANGLE;
            }

            foreach (var rotor in _rollRotors)
            {
                rotor.UpperLimitRad = (float)MAX_YAW_PITCH_ROLL_ANGLE;
                rotor.LowerLimitRad = (float)-MAX_YAW_PITCH_ROLL_ANGLE;
            }

            return true;
        }

        private bool FindRotorsAndThrusters(out string errorMessage)
        {
            errorMessage = string.Empty;

            // Find and auto-name rotors based on position
            FindAndNameRotors();

            // Find and auto-name thrusters based on position
            FindAndNameThrusters();

            // Validate we found the required components
            if (_pitchRotors.Count == 0 && _rollRotors.Count == 0)
            {
                errorMessage = "TiltRotorNav: No rotors found!";
                return false;
            }

            return true;
        }

        private void FindAndNameRotors()
        {
            var allRotors = _program.GetLocalBlocks<IMyMotorStator>();

            if (allRotors.Count == 0)
            {
                return;
            }

            var rcPosition = _remoteControl.GetPosition();
            var rcForward = _remoteControl.WorldMatrix.Forward;
            var rcRight = _remoteControl.WorldMatrix.Right;
            var rcUp = _remoteControl.WorldMatrix.Up;

            var pitchRotorsFound = 0;
            var rollRotorsFound = 0;

            foreach (var rotor in allRotors)
            {
                var rotorPosition = rotor.GetPosition();
                var offset = rotorPosition - rcPosition;

                // Get rotor's orientation (which axis it's aligned with)
                var rotorRight = rotor.WorldMatrix.Right;
                var rotorForward = rotor.WorldMatrix.Forward;
                var rotorUp = rotor.WorldMatrix.Up;

                // Determine position (left/right side of RC)
                var rightAxisProjection = Vector3D.Dot(offset, rcRight);
                var side = rightAxisProjection > 0 ? "Right" : "Left";

                // Determine function based on orientation
                var rightAxisOrientation = Math.Abs(Vector3D.Dot(rotorRight, rcRight)) + Math.Abs(Vector3D.Dot(rotorUp, rcRight));
                var forwardAxisOrientation = Math.Abs(Vector3D.Dot(rotorForward, rcForward)) + Math.Abs(Vector3D.Dot(rotorUp, rcForward));

                // If rotor is primarily oriented along right axis → pitch control
                if (rightAxisOrientation > forwardAxisOrientation && rightAxisOrientation > 0.5)
                {
                    var newName = $"Rotor Pitch {side}";
                    rotor.CustomName = newName;
                    _pitchRotors.Add(rotor);
                    pitchRotorsFound++;
                }
                // If rotor is primarily oriented along forward axis → roll control
                else if (forwardAxisOrientation > rightAxisOrientation && forwardAxisOrientation > 0.5)
                {
                    var newName = $"Rotor Roll {side}";
                    rotor.CustomName = newName;
                    _rollRotors.Add(rotor);
                    rollRotorsFound++;
                }
            }
        }

        private void FindAndNameThrusters()
        {
            var allThrusters = _program.GetLocalBlocks<IMyThrust>();

            if (allThrusters.Count == 0)
            {
                return;
            }

            var rcPosition = _remoteControl.GetPosition();
            var rcRight = _remoteControl.WorldMatrix.Right;

            var thrustersFound = 0;

            foreach (var thruster in allThrusters)
            {
                var thrusterPosition = thruster.GetPosition();
                var offset = thrusterPosition - rcPosition;
                var rightAxisProjection = Vector3D.Dot(offset, rcRight);

                if (Math.Abs(rightAxisProjection) > 0.5) // Left or right side of RC
                {
                    var sideName = rightAxisProjection < 0 ? "Left" : "Right";
                    var newName = $"Thruster {sideName}";
                    thruster.CustomName = newName;
                    _thrusters.Add(thruster);
                    thrustersFound++;
                }
            }
        }

        public void Stop()
        {
            // Reset rotor angles to 0 and stop them
            foreach (var rotor in _pitchRotors.Concat(_rollRotors))
            {
                rotor.TargetVelocityRPM = 0;
            }

            // Reset thruster thrust to 0
            foreach (var thruster in _thrusters)
            {
                thruster.ThrustOverride = 0;
            }

            _pidVertical.Reset();
            // Just reset alignment - Navigation handles thrusters
            _alignment.Stop();
        }

        public void PowerOn()
        {
            foreach (var thruster in _thrusters)
            {
                thruster.Enabled = true;
            }
        }

        public void PowerOff()
        {
            foreach (var thruster in _thrusters)
            {
                thruster.Enabled = false;
            }
        }

        public bool NavigateTo(Vector3D target, double targetYaw, double maxSpeed = 20.0)
        {
            _currentVelocity = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            var currentPos = _remoteControl.GetPosition();
            
            // Check yaw alignment if targetYaw is specified
            double currentYaw, currentPitch, currentRoll;
            _alignment.CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);
            var yawError = Math.Abs(targetYaw - currentYaw);
            var yawAligned = yawError < 0.25;

            // Cache original start position if yaw is misaligned and we don't have a cached position
            if (!yawAligned && !_cachedStartPosition.HasValue)
            {
                _cachedStartPosition = currentPos;
            }
            
            // Clear cached position once yaw is aligned
            if (yawAligned)
            {
                _cachedStartPosition = null;
            }

            // Align
            _alignment.AlignWithYawPitchRoll(targetYaw, 0, 0);

            // If yaw is not aligned, hold at cached start position (or current position if no cache)
            Vector3D navigationTarget = yawAligned ? target : (_cachedStartPosition ?? currentPos);

            var desiredSpeed = CalculateDesiredSpeedAtPosition(navigationTarget, maxSpeed);
            var desiredVelocity = Vector3D.Normalize(navigationTarget - currentPos) * desiredSpeed;

            var up = -Vector3D.Normalize(gravity);
            var fwd = _remoteControl.WorldMatrix.Forward;
            var right = _remoteControl.WorldMatrix.Right;

            // Add scaled distance error to current velocity
            var velError = _currentVelocity - desiredVelocity;

            var velErrorH = velError - Vector3D.Dot(velError, up) * up;
            var velErrorMag = velErrorH.Length();

            var velErrorDir = Vector3D.Normalize(velErrorH);

            double targetPitch, targetRoll;

            if (velErrorMag > 0.001)
            {
                // Calculate pitch and roll based on velocity error direction
                var angleRatio = Math.Min(velErrorMag, MaxSpeedError) / MaxSpeedError;

                // Pitch control: based on forward component of velocity error
                targetPitch = -Vector3D.Dot(velErrorDir, fwd) * angleRatio;
                targetPitch = MathHelper.Clamp(targetPitch, -MAX_YAW_PITCH_ROLL_ANGLE, MAX_YAW_PITCH_ROLL_ANGLE);

                // Roll control: based on right component of velocity error
                targetRoll = Vector3D.Dot(velErrorDir, right) * angleRatio;
                targetRoll = MathHelper.Clamp(targetRoll, -MAX_YAW_PITCH_ROLL_ANGLE, MAX_YAW_PITCH_ROLL_ANGLE);
            }
            else
            {
                targetPitch = 0.0;
                targetRoll = 0.0;
            }

            // Control rotors based on target pitch and roll
            ControlRotors(targetPitch, targetRoll);

            var distance = Vector3D.Distance(currentPos, target);

            // Control vertical movement with up thrusters using PID
            ControlVerticalThrust(navigationTarget, up);
            _program.Echo($"Distance: {distance:F1}m");
            _program.Echo($"Precision: {Precision:F1}m");
            _program.Echo($"Yaw Aligned: {yawAligned}");
            _program.Echo($"Distance < Precision: {distance < Precision}");
            // Only consider navigation complete if yaw is aligned AND we're at target
            return yawAligned && distance < Precision;
        }

        private void ControlRotors(double targetPitch, double targetRoll)
        {
            // Target angles are already in radians, use directly for rotor limits

            // Control pitch rotors (right/left axis)
            // Note: Left and right rotors need opposite rotation for pitch control

            foreach (var rotor in _pitchRotors)
            {
                // Set velocity toward the target (left and right rotors rotate in opposite directions)
                var currentAngle = rotor.Angle;
                // For pitch control, left and right rotors need opposite rotation
                // We'll determine direction based on rotor position
                var rotorPosition = rotor.GetPosition();
                var rcPosition = _remoteControl.GetPosition();
                var offset = rotorPosition - rcPosition;
                var rightAxisProjection = Vector3D.Dot(offset, _remoteControl.WorldMatrix.Right);
                var isRightSide = rightAxisProjection > 0;
                currentAngle = isRightSide ? currentAngle : -currentAngle;

                // Exponential velocity control: smooth ramp-up from 0 to max speed
                var pitchAngleError = Math.Abs(targetPitch - currentAngle);
                var pitchVelocityMagnitude = RotorRotationSpeed * (1 - Math.Exp(-pitchAngleError * RotorRotationSpeedExp));
                var pitchVelocityDirection = Math.Sign(targetPitch - currentAngle);

                // If rotor is on the right side, invert the velocity direction for coordinated pitch
                // var finalVelocity = rightAxisProjection > 0 ? pitchVelocityDirection * pitchVelocityMagnitude : -pitchVelocityDirection * pitchVelocityMagnitude;
                var finalVelocity = pitchVelocityDirection * pitchVelocityMagnitude;
                finalVelocity = isRightSide ? finalVelocity : -finalVelocity;
                rotor.TargetVelocityRad = (float)finalVelocity;

                _rotorTargetAngles[rotor] = targetPitch;
            }

            // Control roll rotors (forward axis)
            foreach (var rotor in _rollRotors)
            {
                // Exponential velocity control: smooth ramp-up from 0 to max speed
                var currentAngle = rotor.Angle;
                var rollAngleError = Math.Abs(targetRoll - currentAngle);
                var rollVelocityMagnitude = RotorRotationSpeed * (1 - Math.Exp(-rollAngleError * RotorRotationSpeedExp));
                var rollVelocityDirection = Math.Sign(targetRoll - currentAngle);
                rotor.TargetVelocityRad = (float)(rollVelocityDirection * rollVelocityMagnitude);

                _rotorTargetAngles[rotor] = targetRoll;
            }
        }

        private void ControlVerticalThrust(Vector3D target, Vector3D up)
        {
            var currentPos = _remoteControl.GetPosition();
            var velocity = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            var shipMass = _remoteControl.CalculateShipMass().PhysicalMass;
            
            // Calculate vertical distance to target
            var vectorToTarget = target - currentPos;
            var verticalDistance = Vector3D.Dot(vectorToTarget, up);
            
            // Calculate vertical velocity
            var verticalVelocity = Vector3D.Dot(velocity, up);
            
            // Get gravity magnitude (positive value)
            var gravityMagnitude = gravity.Length();
            
            // Calculate desired velocity (like Navigation class)
            var desiredSpeed = Math.Min(5.0, Math.Max(0.5, Math.Abs(verticalDistance) / 3.0)); // Braking distance factor of 3
            var desiredVelocity = desiredSpeed * Math.Sign(verticalDistance); // Direction based on distance
            
            // Calculate velocity error
            var velocityError = desiredVelocity - verticalVelocity;
            
            // Use PID to control velocity error (like Navigation class)
            var desiredAcceleration = _pidVertical.Control(velocityError);
            
            // Compensate for gravity (like Navigation class)
            var gravityCompensation = Vector3D.Dot(gravity, up);
            var totalRequiredAcceleration = desiredAcceleration - gravityCompensation;
            
            // Calculate total thrust needed using F = ma
            var totalRequiredThrust = shipMass * totalRequiredAcceleration;
            
            // Apply minimum thrust to counteract gravity when very close to target
            if (Math.Abs(verticalDistance) < HOVER_DISTANCE_THRESHOLD && Math.Abs(verticalVelocity) < HOVER_VELOCITY_THRESHOLD)
            {
                // Very close to target - just hover
                var hoverThrust = shipMass * gravityMagnitude;
                totalRequiredThrust = hoverThrust;
            }
            
            // Divide thrust among all thrusters
            var thrustPerThruster = _thrusters.Count > 0 ? totalRequiredThrust / _thrusters.Count : 0.0;
            
            // Build debug info for CustomData
            var debugInfo = "";
            debugInfo += "=== VERTICAL THRUST DEBUG ===\n\n";
            debugInfo += $"DEBUG: Total Required Thrust: {totalRequiredThrust:F0}N\n";
            debugInfo += $"DEBUG: Number of Thrusters: {_thrusters.Count}\n";
            debugInfo += $"DEBUG: Thrust per Thruster (raw): {thrustPerThruster:F0}N\n\n";
            
            // Apply thrust to all thrusters (they point upward)
            foreach (var thruster in _thrusters)
            {
                // Get maximum thrust for this thruster
                var maxThrust = thruster.MaxThrust;
                var maxThrust100 = Math.Round(maxThrust / 100.0) * 100.0;
                debugInfo += $"Thruster: {thruster.CustomName}\n";
                debugInfo += $"Max Thrust: {maxThrust100:F0}N\n";
                var thrustOverride = MathHelper.Clamp(thrustPerThruster, 0.0, maxThrust);
                thruster.ThrustOverride = (float)thrustOverride;
                debugInfo += $"Applied Thrust: {thrustOverride:F0}N\n\n";
            }
            
            debugInfo += $"Vertical Distance: {verticalDistance:F2}m\n";
            debugInfo += $"Vertical Velocity: {verticalVelocity:F2}m/s\n";
            debugInfo += $"Desired Velocity: {desiredVelocity:F2}m/s\n";
            debugInfo += $"Velocity Error: {velocityError:F2}m/s\n";
            debugInfo += $"Gravity: {gravityMagnitude:F2}m/s²\n";
            debugInfo += $"Gravity Comp: {gravityCompensation:F2}m/s²\n";
            debugInfo += $"Desired Accel: {desiredAcceleration:F2}m/s²\n";
            debugInfo += $"Total Required Accel: {totalRequiredAcceleration:F2}m/s²\n";
            debugInfo += $"Ship Mass: {shipMass:F0}kg\n";
            debugInfo += $"Total Thrust: {totalRequiredThrust:F0}N\n";
            var roundedThrust = Math.Round(thrustPerThruster / 100.0) * 100.0;
            debugInfo += $"Thrust per Thruster: {roundedThrust:F0}N ({_thrusters.Count} thrusters)\n";
            
            // Save to remote control CustomData for easy copy/paste
            _remoteControl.CustomData = debugInfo;
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

        #endregion
    }
}
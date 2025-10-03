using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class TiltNavigation
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
        #endregion

        #region Fields
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private Alignment _alignment;
        private Navigation _navigation;
        private Vector3D _currentVelocity;
        private Vector3D _desiredVelocity;
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

            return true;
        }
        
        public void Stop()
        {
            // Just reset alignment - Navigation handles thrusters
            _alignment.Stop();
        }

        public bool NavigateTo(Vector3D target, double maxSpeed = 20.0)
        {
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
            var velErrorMag = velErrorH.Length();
            
            var velErrorDir = Vector3D.Normalize(velErrorH);
            // Direct YPR approach: set yaw based on velocity error direction, pitch based on magnitude
            double targetPitch, targetRoll;
            
            double yaw, pitch, roll;
            _alignment.CalculateYawPitchRoll(out yaw, out pitch, out roll);
            var distance = Vector3D.Distance(currentPos, target);
            
            if (velErrorMag > 0.001)
            {
                // Calculate yaw and pitch based on velocity error direction
                var angleRatio  = Math.Min(velErrorMag, MaxSpeedError) / MaxSpeedError;
                
                // TODO: calculate targetPitch
                targetPitch = Vector3D.Dot(velErrorDir, fwd) * angleRatio;
                // clamp between +- 15 degrees
                targetPitch = MathHelper.Clamp(targetPitch, -15.0 * Math.PI / 180.0, 15.0 * Math.PI / 180.0);
                // TODO: calculate targetRoll
                targetRoll = -Vector3D.Dot(velErrorDir, right) * angleRatio;
                // clamp between +- 15 degrees
                targetRoll = MathHelper.Clamp(targetRoll, -15.0 * Math.PI / 180.0, 15.0 * Math.PI / 180.0);
            }
            else
            {
                targetPitch = 0.0;
                targetRoll = 0.0;
                
                _program.Echo($"No velocity error - keeping current orientation");
            }
        
            // Convert clamped YPR back to matrix
            var clampedMatrix = _alignment.YawPitchRollToWorldMatrix(0, targetPitch, targetRoll);
            
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
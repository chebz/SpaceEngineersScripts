using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    // Handles ship orientation and gyro control
    public class Alignment
    {
        #region Fields
        private IMyGridTerminalSystem _gridTerminalSystem;
        private IMyRemoteControl _remoteControl;
        private readonly List<IMyGyro> _gyros = new List<IMyGyro>();

        // Gyro PID settings
        private const double KP_GYRO_DEFAULT = 7.0;
        private const double KI_GYRO_DEFAULT = 0.0;
        private const double KD_GYRO_DEFAULT = 0.0;
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;
        private const double PRECISION_DEFAULT = 0.01;
        #endregion

        #region Properties
        // Rotation PID controllers
        public AnglePID PidYaw { get; } =
            new AnglePID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public AnglePID PidPitch { get; } =
            new AnglePID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public AnglePID PidRoll { get; } =
            new AnglePID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);
            
        // Precision settings
        public double Precision { get; set; } = PRECISION_DEFAULT;
        #endregion

        #region Methods
        public bool Initialize(IMyGridTerminalSystem gridTerminalSystem, out string errorMessage)
        {
            _gridTerminalSystem = gridTerminalSystem;
            return InitializeRemoteControl(out errorMessage) && InitializeGyros(out errorMessage);
        }

        private bool InitializeRemoteControl(out string errorMessage)
        {
            errorMessage = string.Empty;
            var remoteControls = new List<IMyRemoteControl>();
            _gridTerminalSystem.GetBlocksOfType(remoteControls);
            _remoteControl = remoteControls.FirstOrDefault();
            if (_remoteControl == null)
            {
                errorMessage = "Alignment: No remote control found!";
                return false;
            }
            return true;
        }

        private bool InitializeGyros(out string errorMessage)
        {
            errorMessage = string.Empty;
            _gyros.Clear();
            _gridTerminalSystem.GetBlocksOfType(_gyros);
            
            if (_gyros.Count == 0)
            {
                errorMessage = "Alignment: No gyros found!";
                return false;
            }

            return true;
        }

        public bool AlignYawPitchRoll(double? targetYaw, double? targetPitch, double? targetRoll)
        {
            double tolerance = Precision;

            // Get current angles
            double currentYaw, currentPitch, currentRoll;
            CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);

            // Get current angular velocity
            var angularVelocity = _remoteControl.GetShipVelocities().AngularVelocity;

            var yawAligned = true;
            var pitchAligned = true;
            var rollAligned = true;

            // Calculate angle errors and apply PID control
            if (targetYaw.HasValue)
            {
                var yawError = CalculateAngleError(currentYaw, targetYaw.Value);
                var yawOutput = PidYaw.Control(yawError);
                ApplyGyroOverride("Yaw", yawOutput);
                yawAligned = Math.Abs(yawError) < tolerance &&
                             Math.Abs(angularVelocity.Y) < tolerance;
            }

            if (targetPitch.HasValue)
            {
                var pitchError = CalculateAngleError(currentPitch, targetPitch.Value);
                var pitchOutput = PidPitch.Control(pitchError);
                ApplyGyroOverride("Pitch", -pitchOutput); // Negate pitch output
                pitchAligned = Math.Abs(pitchError) < tolerance &&
                               Math.Abs(angularVelocity.X) < tolerance;
            }

            if (targetRoll.HasValue)
            {
                var rollError = CalculateAngleError(currentRoll, targetRoll.Value);
                var rollOutput = PidRoll.Control(rollError);
                ApplyGyroOverride("Roll", rollOutput);
                rollAligned = Math.Abs(rollError) < tolerance &&
                              Math.Abs(angularVelocity.Z) < tolerance;
            }

            // Return true only if all requested axes are aligned
            var allAligned = yawAligned && pitchAligned && rollAligned;

            // If all axes are aligned, disable gyro override
            if (allAligned)
            {
                ResetGyroOverride();
            }

            return allAligned;
        }

        public void CalculateYawPitchRoll(out double yaw, out double pitch, out double roll)
        {
            var gravity = _remoteControl.GetNaturalGravity();
            var gravityNormalized = Vector3D.Normalize(gravity);
            var fwd = _remoteControl.WorldMatrix.Forward;
            var right = _remoteControl.WorldMatrix.Right;
            var up = _remoteControl.WorldMatrix.Up;

            yaw = Math.Atan2(fwd.X, fwd.Y);

            // Pitch: angle from fwd to gravityNrm - 90 degrees
            var dotProduct = Vector3D.Dot(fwd, gravityNormalized);
            pitch = Math.Acos(Math.Max(-1, Math.Min(1, dotProduct))) - Math.PI / 2;

            // Roll: angle between right and gravityNrm - 90 degrees  
            var rightDotProduct = Vector3D.Dot(right, gravityNormalized);
            roll = Math.Acos(Math.Max(-1, Math.Min(1, rightDotProduct))) - Math.PI / 2;
        }

        public double CalculateYawToTarget(Vector3D destination)
        {
            var myPosition = _remoteControl.GetPosition();
            var distance = Vector3D.Distance(myPosition, destination);
            if (distance < 0.1)
            {
                return 0;
            }

            var dirToDestination = Vector3D.Normalize(destination - myPosition);
            var gravityNormalized = Vector3D.Normalize(_remoteControl.GetNaturalGravity());
            
            // Get current yaw from the drone
            double currentYaw, currentPitch, currentRoll;
            CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);
            
            // Get drone's current forward direction
            var currentForward = _remoteControl.WorldMatrix.Forward;
            
            // Rotate current forward around gravity axis by negative current yaw to get real forward direction
            var upVector = -gravityNormalized;
            var rotationAxis = upVector;
            var rotationAngle = -currentYaw;
            
            // Apply rotation: rotate currentForward around rotationAxis by rotationAngle
            var cosAngle = Math.Cos(rotationAngle);
            var sinAngle = Math.Sin(rotationAngle);
            var dotProduct = Vector3D.Dot(currentForward, rotationAxis);
            var crossProduct = Vector3D.Cross(currentForward, rotationAxis);
            
            var realForward = currentForward * cosAngle + 
                                crossProduct * sinAngle + 
                                rotationAxis * dotProduct * (1 - cosAngle);
            
            // Project direction to destination onto horizontal plane
            var projectedDirection = dirToDestination - Vector3D.Dot(dirToDestination, upVector) * upVector;
            projectedDirection = Vector3D.Normalize(projectedDirection);
            
            // Calculate desired yaw as angle between real forward and projected direction
            var dot = Vector3D.Dot(realForward, projectedDirection);
            var cross = Vector3D.Cross(realForward, projectedDirection);
            var crossDotUp = Vector3D.Dot(cross, upVector);
            
            return Math.Atan2(-crossDotUp, dot);
        }

        public void Stop()
        {
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = false;
            }
        }

        private void ResetGyroOverride()
        {
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = false;
            }
        }

        public bool IsAligned(double targetYaw, double targetPitch, double targetRoll)
        {
            const double angleTolerance = 0.01; // radians, about 0.57 degrees

            double currentYaw, currentPitch, currentRoll;
            CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);

            var yawError = Math.Abs(CalculateAngleError(currentYaw, targetYaw));
            var pitchError = Math.Abs(CalculateAngleError(currentPitch, targetPitch));
            var rollError = Math.Abs(CalculateAngleError(currentRoll, targetRoll));

            return yawError < angleTolerance && pitchError < angleTolerance && rollError < angleTolerance;
        }

        private double CalculateAngleError(double current, double target)
        {
            var error = target - current;

            // Normalize angle error to [-π, π]
            while (error > Math.PI) error -= 2 * Math.PI;
            while (error < -Math.PI) error += 2 * Math.PI;

            return error;
        }

        private void ApplyGyroOverride(string axis, double value)
        {
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = true;
                gyro.SetValueFloat(axis, (float)value);
                gyro.SetValueFloat("Power", 1.0f);
            }
        }
        #endregion
    }
}

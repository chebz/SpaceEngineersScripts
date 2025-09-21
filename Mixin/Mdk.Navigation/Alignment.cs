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
        // Gyro PID settings
        private const double KP_GYRO_DEFAULT = 10;
        private const double KI_GYRO_DEFAULT = 0.0;
        private const double KD_GYRO_DEFAULT = 0.0;
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;
        private const double PRECISION_DEFAULT = 0.01;

        private readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        private Program _program;
        private IMyRemoteControl _remoteControl;
        #endregion

        #region Properties
        // Vector-based PID controllers (for matrix alignment)
        public PID PidVectorYaw { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public PID PidVectorPitch { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public PID PidVectorRoll { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        // Precision settings
        public double Precision { get; set; } = PRECISION_DEFAULT;
        #endregion

        #region Methods
        public bool Initialize(Program program, out string errorMessage)
        {
            _program = program;
            return InitializeRemoteControl(out errorMessage) && InitializeGyros(out errorMessage);
        }

        private bool InitializeRemoteControl(out string errorMessage)
        {
            errorMessage = string.Empty;
            var remoteControls = new List<IMyRemoteControl>();
            _program.GridTerminalSystem.GetBlocksOfType(remoteControls);
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
            _program.GridTerminalSystem.GetBlocksOfType(_gyros);

            if (_gyros.Count == 0)
            {
                errorMessage = "Alignment: No gyros found!";
                return false;
            }

            return true;
        }

        public bool AlignWithWorldMatrix(MatrixD targetMatrix)
        {
            var currentMatrix = _remoteControl.WorldMatrix;
            var angularVelocity = _remoteControl.GetShipVelocities().AngularVelocity;

            // Ship forward, right, and up vectors (world space)
            var forward = currentMatrix.Forward;
            var right = currentMatrix.Right;
            var up = currentMatrix.Up;

            // Target forward, right, and up (world space)
            var tForward = targetMatrix.Forward;
            var tRight = targetMatrix.Right;
            var tUp = targetMatrix.Up;

            // Calculate each axis error separately to avoid interference
            var forwardErrorAxis = Vector3D.Cross(tForward, forward);
            var rightErrorAxis = Vector3D.Cross(tRight, right);
            var upErrorAxis = Vector3D.Cross(up, tUp);

            // Transform each error axis to local coordinates
            var localForwardError = Vector3D.TransformNormal(forwardErrorAxis, MatrixD.Transpose(currentMatrix));
            var localUpError = Vector3D.TransformNormal(upErrorAxis, MatrixD.Transpose(currentMatrix));

            // Extract errors from the most relevant components
            var yawError = localForwardError.Y; // Forward error in Y = yaw needed
            var pitchError = -localForwardError.X; // Forward error in X = pitch needed
            var rollError = -localUpError.Z; // Up error in Z = roll needed (negated)

            // PID outputs
            var yawOutput = PidVectorYaw.Control(yawError);
            var pitchOutput = PidVectorPitch.Control(pitchError);
            var rollOutput = PidVectorRoll.Control(rollError);

            ApplyGyroOverrides(yawOutput, pitchOutput, rollOutput);

            // Check alignment using individual error magnitudes
            var allAligned =
                Math.Abs(yawError) < Precision &&
                Math.Abs(pitchError) < Precision &&
                Math.Abs(rollError) < Precision &&
                angularVelocity.Length() < Precision;

            if (allAligned)
            {
                Stop();
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

            // Calculate yaw relative to world coordinate system
            // Project forward vector onto horizontal plane (perpendicular to gravity)
            var upVector = -gravityNormalized;
            var forwardHorizontal = fwd - Vector3D.Dot(fwd, upVector) * upVector;

            if (forwardHorizontal.LengthSquared() > 1e-6)
            {
                forwardHorizontal = Vector3D.Normalize(forwardHorizontal);
                // Use world X-axis as reference direction (projected onto horizontal plane)
                var worldXAxis = new Vector3D(1, 0, 0);
                var referenceDirection = worldXAxis - Vector3D.Dot(worldXAxis, upVector) * upVector;

                if (referenceDirection.LengthSquared() > 1e-6)
                {
                    referenceDirection = Vector3D.Normalize(referenceDirection);
                    var referenceRight = Vector3D.Cross(referenceDirection, upVector);
                    yaw = Math.Atan2(Vector3D.Dot(forwardHorizontal, referenceRight),
                        Vector3D.Dot(forwardHorizontal, referenceDirection));
                }
                else
                {
                    // Edge case: gravity is parallel to world X-axis, use Y-axis as reference
                    var worldYAxis = new Vector3D(0, 1, 0);
                    referenceDirection = Vector3D.Normalize(worldYAxis - Vector3D.Dot(worldYAxis, upVector) * upVector);
                    var referenceRight = Vector3D.Cross(referenceDirection, upVector);
                    yaw = Math.Atan2(Vector3D.Dot(forwardHorizontal, referenceRight),
                        Vector3D.Dot(forwardHorizontal, referenceDirection));
                }
            }
            else
            {
                yaw = 0; // Edge case: forward vector is parallel to gravity
            }

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

        public MatrixD YawPitchRollToWorldMatrix(double yaw, double pitch, double roll)
        {
            var gravityNormalized = Vector3D.Normalize(_remoteControl.GetNaturalGravity());
            var upVector = -gravityNormalized;
            var currentPosition = _remoteControl.GetPosition();

            // Use the same reference direction as CalculateYawPitchRoll
            var worldXAxis = new Vector3D(1, 0, 0);
            var referenceDirection = worldXAxis - Vector3D.Dot(worldXAxis, upVector) * upVector;

            if (referenceDirection.LengthSquared() <= 1e-6)
            {
                // Edge case: gravity is parallel to world X-axis, use Y-axis as reference
                var worldYAxis = new Vector3D(0, 1, 0);
                referenceDirection = worldYAxis - Vector3D.Dot(worldYAxis, upVector) * upVector;
            }

            referenceDirection = Vector3D.Normalize(referenceDirection);
            var referenceRight = Vector3D.Cross(referenceDirection, upVector);

            // Apply yaw rotation around up vector
            var yawMatrix = MatrixD.CreateFromAxisAngle(upVector, yaw);
            var forwardAfterYaw = Vector3D.Transform(referenceDirection, yawMatrix);
            var rightAfterYaw = Vector3D.Transform(referenceRight, yawMatrix);

            // Apply pitch rotation around right vector
            var pitchMatrix = MatrixD.CreateFromAxisAngle(rightAfterYaw, pitch);
            var forwardAfterPitch = Vector3D.Transform(forwardAfterYaw, pitchMatrix);
            var upAfterPitch = Vector3D.Transform(upVector, pitchMatrix);

            // Apply roll rotation around forward vector
            var rollMatrix = MatrixD.CreateFromAxisAngle(forwardAfterPitch, roll);
            var finalUp = Vector3D.Transform(upAfterPitch, rollMatrix);

            return MatrixD.CreateWorld(currentPosition, forwardAfterPitch, finalUp);
        }

        public void WorldMatrixToYawPitchRoll(MatrixD worldMatrix, out double yaw, out double pitch, out double roll)
        {
            var gravity = _remoteControl.GetNaturalGravity();
            var gravityNormalized = Vector3D.Normalize(gravity);
            var fwd = worldMatrix.Forward;
            var right = worldMatrix.Right;
            var up = worldMatrix.Up;

            yaw = Math.Atan2(fwd.X, fwd.Y);

            // Pitch: angle from fwd to gravityNrm - 90 degrees
            var dotProduct = Vector3D.Dot(fwd, gravityNormalized);
            pitch = Math.Acos(Math.Max(-1, Math.Min(1, dotProduct))) - Math.PI / 2;

            // Roll: angle between right and gravityNrm - 90 degrees  
            var rightDotProduct = Vector3D.Dot(right, gravityNormalized);
            roll = Math.Acos(Math.Max(-1, Math.Min(1, rightDotProduct))) - Math.PI / 2;
        }

        public void Stop()
        {
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = false;
            }
        }

        private void ApplyGyroOverrides(double yawOutput, double pitchOutput, double rollOutput)
        {
            var remoteMatrix = _remoteControl.WorldMatrix;
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = true;
                gyro.SetValueFloat("Power", 1.0f);

                // Get only the rotation parts (no translation)
                var remoteRotationMatrix = MatrixD.CreateWorld(Vector3D.Zero, remoteMatrix.Forward, remoteMatrix.Up);
                var gyroRotationMatrix =
                    MatrixD.CreateWorld(Vector3D.Zero, gyro.WorldMatrix.Forward, gyro.WorldMatrix.Up);

                // Transform remote control's desired rotation to gyro's local coordinate system
                var remoteToGyro = MatrixD.Transpose(remoteRotationMatrix) * gyroRotationMatrix;

                // Transform YPR outputs to gyro's coordinate system
                var remoteRotation = new Vector3D(pitchOutput, yawOutput, rollOutput);
                var gyroRotation = Vector3D.Transform(remoteRotation, remoteToGyro);

                // Apply to gyro
                gyro.SetValueFloat("Pitch", (float)gyroRotation.X);
                gyro.SetValueFloat("Yaw", (float)gyroRotation.Y);
                gyro.SetValueFloat("Roll", (float)gyroRotation.Z);
            }
        }
        #endregion
    }
}
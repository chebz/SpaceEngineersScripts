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
        public class AlignmentSection : Section
        {
            private Alignment _alignment;
            public DoubleProperty Precision { get; } = new DoubleProperty("Precision", PRECISION_DEFAULT);
            public DoubleProperty KP { get; } = new DoubleProperty("KP", KP_GYRO_DEFAULT);
            public DoubleProperty KI { get; } = new DoubleProperty("KI", KI_GYRO_DEFAULT);
            public DoubleProperty KD { get; } = new DoubleProperty("KD", KD_GYRO_DEFAULT);

            public AlignmentSection(Alignment alignment) : base("Alignment")
            {
                _alignment = alignment;
                _properties.Add(Precision);
                _properties.Add(KP);
                _properties.Add(KI);
                _properties.Add(KD);
                KP.ValueChanged += (value) => _alignment.PidVectorYaw.Kp = value;
                KI.ValueChanged += (value) => {
                    _alignment.PidVectorYaw.Ki = value;
                    _alignment.PidVectorPitch.Ki = value;
                    _alignment.PidVectorRoll.Ki = value;
                };
                KD.ValueChanged += (value) => {
                    _alignment.PidVectorYaw.Kd = value;
                    _alignment.PidVectorPitch.Kd = value;
                    _alignment.PidVectorRoll.Kd = value;
                };
                KD.ValueChanged += (value) => {
                    _alignment.PidVectorYaw.Kd = value;
                    _alignment.PidVectorPitch.Kd = value;
                    _alignment.PidVectorRoll.Kd = value;
                };
            }
        }

        #region Fields
        // Gyro PID settings
        private const double KP_GYRO_DEFAULT = 1;
        private const double KI_GYRO_DEFAULT = 0.0;
        private const double KD_GYRO_DEFAULT = 0.0;
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;
        private const double PRECISION_DEFAULT = 0.01;

        private List<IMyGyro> _gyros = new List<IMyGyro>();
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private CustomDataConnector _customDataConnector;
        private AlignmentSection _section;
        #endregion

        #region Properties
        // Vector-based PID controllers (for matrix alignment)
        public PID PidVectorYaw { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public PID PidVectorPitch { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);

        public PID PidVectorRoll { get; } =
            new PID(KP_GYRO_DEFAULT, KI_GYRO_DEFAULT, KD_GYRO_DEFAULT, PID_TIME_STEP_DEFAULT);
        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, out string errorMessage)
        {
            _program = program;
            _customDataConnector = customDataConnector;
            errorMessage = string.Empty;

            // Initialize remote control
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "Alignment: No remote control found!";
                return false;
            }

            // Initialize gyros
            _gyros = _program.GetLocalBlocks<IMyGyro>();
            if (_gyros.Count == 0)
            {
                errorMessage = "Alignment: No gyros found!";
                return false;
            }

            _section = new AlignmentSection(this);
            _customDataConnector.AddSection(_section);

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
            var pitchError = localForwardError.X; // Forward error in X = pitch needed
            var rollError = -localUpError.Z; // Up error in Z = roll needed (negated)

            // PID outputs
            var yawOutput = PidVectorYaw.Control(yawError);
            var pitchOutput = PidVectorPitch.Control(pitchError);
            var rollOutput = PidVectorRoll.Control(rollError);

            ApplyGyroOverrides(yawOutput, pitchOutput, rollOutput);

            // Check alignment using individual error magnitudes
            var allAligned =
                Math.Abs(yawError) < _section.Precision.Value &&
                Math.Abs(pitchError) < _section.Precision.Value &&
                Math.Abs(rollError) < _section.Precision.Value &&
                angularVelocity.Length() < _section.Precision.Value;

            if (allAligned)
            {
                Stop();
            }

            return allAligned;
        }

        public bool AlignWithYawPitchRoll(double? yaw, double pitch, double roll)
        {
            var angularVelocity = _remoteControl.GetShipVelocities().AngularVelocity;

            // Calculate ship's current YPR relative to planet
            double currentYaw, currentPitch, currentRoll;
            CalculateYawPitchRoll(out currentYaw, out currentPitch, out currentRoll);

            // Calculate errors
            var yawError = yaw.HasValue ? NormalizeAngle(yaw.Value + currentYaw) : 0.0;
            var pitchError = pitch + currentPitch;
            var rollError = roll - currentRoll;

            // Apply PID control
            var yawOutput = yaw.HasValue ? PidVectorYaw.Control(yawError) : 0.0;
            var pitchOutput = PidVectorPitch.Control(pitchError);
            var rollOutput = PidVectorRoll.Control(rollError);

            var allAligned =
                Math.Abs(yawError) < _section.Precision.Value &&
                Math.Abs(pitchError) < _section.Precision.Value &&
                Math.Abs(rollError) < _section.Precision.Value &&
                angularVelocity.Length() < _section.Precision.Value;

            var velocityFactor = 0.1;
            var finalYaw = yawOutput * velocityFactor;
            var finalPitch = pitchOutput * velocityFactor;
            var finalRoll = rollOutput * velocityFactor;

            ApplyGyroOverrides(finalYaw, finalPitch, finalRoll);

            if (allAligned)
            {
                Stop();
            }

            return allAligned;
        }

        private double NormalizeAngle(double angle)
        {
            // Normalize angle to [-π, π] range
            while (angle > Math.PI)
                angle -= 2 * Math.PI;
            while (angle < -Math.PI)
                angle += 2 * Math.PI;
            return angle;
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
                    yaw = -Math.Atan2(Vector3D.Dot(forwardHorizontal, referenceRight),
                        Vector3D.Dot(forwardHorizontal, referenceDirection));
                }
                else
                {
                    // Edge case: gravity is parallel to world X-axis, use Y-axis as reference
                    var worldYAxis = new Vector3D(0, 1, 0);
                    referenceDirection = Vector3D.Normalize(worldYAxis - Vector3D.Dot(worldYAxis, upVector) * upVector);
                    var referenceRight = Vector3D.Cross(referenceDirection, upVector);
                    yaw = -Math.Atan2(Vector3D.Dot(forwardHorizontal, referenceRight),
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
            roll = -(Math.Acos(Math.Max(-1, Math.Min(1, rightDotProduct))) - Math.PI / 2);
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
            var gravity = _remoteControl.GetNaturalGravity();
            var upVector = gravity.LengthSquared() > 0 ? -Vector3D.Normalize(gravity) : Vector3D.Up;

            // Project direction to destination onto horizontal plane
            var targetDirectionHorizontal = dirToDestination - Vector3D.Dot(dirToDestination, upVector) * upVector;
            if (targetDirectionHorizontal.LengthSquared() < 1e-6)
            {
                return 0;
            }
            targetDirectionHorizontal = Vector3D.Normalize(targetDirectionHorizontal);

            // Use world X-axis (1,0,0) as reference direction, projected onto horizontal plane
            var worldXAxis = new Vector3D(1, 0, 0);
            var referenceDirection = worldXAxis - Vector3D.Dot(worldXAxis, upVector) * upVector;
            
            if (referenceDirection.LengthSquared() < 1e-6)
            {
                // If gravity is parallel to world X-axis, use Y-axis as reference
                var worldYAxis = new Vector3D(0, 1, 0);
                referenceDirection = worldYAxis - Vector3D.Dot(worldYAxis, upVector) * upVector;
            }
            
            if (referenceDirection.LengthSquared() < 1e-6)
            {
                return 0;
            }
            referenceDirection = Vector3D.Normalize(referenceDirection);

            // Calculate yaw angle from reference direction to target direction
            // Use same formula as CalculateYawPitchRoll for consistency
            var referenceRight = Vector3D.Cross(referenceDirection, upVector);
            referenceRight = Vector3D.Normalize(referenceRight);
            
            return -Math.Atan2(Vector3D.Dot(targetDirectionHorizontal, referenceRight),
                Vector3D.Dot(targetDirectionHorizontal, referenceDirection));
        }

        public Quaternion YawPitchRollToQuaternion(double yaw, double pitch, double roll)
        {
            var gravity = _remoteControl.GetNaturalGravity();
            var planetUp = gravity.LengthSquared() > 0 ? -Vector3D.Normalize(gravity) : Vector3D.Up;
            var currentForward = _remoteControl.WorldMatrix.Forward;

            var currentForwardHorizontal = currentForward - Vector3D.Dot(currentForward, planetUp) * planetUp;
            if (currentForwardHorizontal.LengthSquared() < 1e-6)
            {
                currentForwardHorizontal = _remoteControl.WorldMatrix.Right;
            }
            currentForwardHorizontal = Vector3D.Normalize(currentForwardHorizontal);

            var yawQuat = Quaternion.CreateFromAxisAngle(new Vector3((float)planetUp.X, (float)planetUp.Y, (float)planetUp.Z), (float)yaw);
            
            var forwardAfterYaw = Vector3D.Transform(currentForwardHorizontal, yawQuat);
            var rightAfterYaw = Vector3D.Cross(planetUp, forwardAfterYaw);
            rightAfterYaw = Vector3D.Normalize(rightAfterYaw);

            var pitchQuat = Quaternion.CreateFromAxisAngle(new Vector3((float)rightAfterYaw.X, (float)rightAfterYaw.Y, (float)rightAfterYaw.Z), (float)pitch);
            
            var forwardAfterPitch = Vector3D.Transform(forwardAfterYaw, pitchQuat);
            var upAfterPitch = Vector3D.Transform(planetUp, pitchQuat);
            
            var rollQuat = Quaternion.CreateFromAxisAngle(new Vector3((float)forwardAfterPitch.X, (float)forwardAfterPitch.Y, (float)forwardAfterPitch.Z), (float)roll);
            
            var finalUp = Vector3D.Transform(upAfterPitch, rollQuat);
            
            var targetMatrix = MatrixD.CreateWorld(Vector3D.Zero, forwardAfterPitch, finalUp);
            var targetQuat = Quaternion.CreateFromRotationMatrix(targetMatrix);

            return targetQuat;
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
                gyro.Pitch = 0;
                gyro.Roll = 0;
                gyro.Yaw = 0;
                gyro.GyroOverride = false;
            }
        }

        private void ApplyGyroOverrides(double yawOutput, double pitchOutput, double rollOutput)
        {
            var remoteMatrix = _remoteControl.WorldMatrix;

            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = true;
                gyro.GyroPower = 1.0f;

                var remoteRotationMatrix = MatrixD.CreateWorld(Vector3D.Zero, remoteMatrix.Forward, remoteMatrix.Up);
                var gyroRotationMatrix = MatrixD.CreateWorld(Vector3D.Zero, gyro.WorldMatrix.Forward, gyro.WorldMatrix.Up);
                
                var remoteQuat = Quaternion.CreateFromRotationMatrix(remoteRotationMatrix);
                var gyroQuat = Quaternion.CreateFromRotationMatrix(gyroRotationMatrix);
                
                var remoteToGyroQuat = Quaternion.Inverse(gyroQuat) * remoteQuat;
                
                var remoteRotation = new Vector3D(yawOutput, pitchOutput, rollOutput);
                var gyroRotation = Vector3D.Transform(remoteRotation, remoteToGyroQuat);

                gyro.Yaw = (float)gyroRotation.X;
                gyro.Pitch = (float)gyroRotation.Y;
                gyro.Roll = (float)gyroRotation.Z;
            }
        }

        public bool AlignWithTarget(Vector3D targetPosition)
        {
            if (_remoteControl == null) 
            {
                return true;
            }
            var currentPosition = _remoteControl.CenterOfMass;
            var directionToTarget = Vector3D.Normalize(targetPosition - currentPosition);
            var gravityVector = _remoteControl.GetNaturalGravity();
            var upDirection = gravityVector.LengthSquared() > 0 ? -Vector3D.Normalize(gravityVector) : _remoteControl.WorldMatrix.Up;
            var targetMatrix = MatrixD.CreateWorld(Vector3D.Zero, directionToTarget, upDirection);
            return AlignWithWorldMatrix(targetMatrix);
        }

        public void ApplyYawForce(double yawForce)
        {
            foreach (var gyro in _gyros)
            {
                gyro.GyroOverride = true;
                gyro.Pitch = 0;
                gyro.Roll = 0;
                gyro.Yaw = (float)yawForce;
            }
        }
        #endregion
    }
}
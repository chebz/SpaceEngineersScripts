using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    // Handles ship movement and thruster control
    public class Navigation
    {
        #region ThrusterDir enum
        public enum ThrusterDir
        {
            Forward,
            Backward,
            Up,
            Down,
            Right,
            Left
        }
        #endregion


        #region Fields
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;

        // Default Position PID settings
        private const double KP_POS_DEFAULT = 3.0;
        private const double KI_POS_DEFAULT = 1.0;
        private const double KD_POS_DEFAULT = 0.0;
        private const bool FACTOR_GRAVITY_DEFAULT = false;
        private const double BRAKING_DISTANCE_FACTOR_DEFAULT = 3.0;
        private const double PRECISION_DEFAULT = 0.1;


        private readonly Dictionary<ThrusterDir, List<IMyThrust>> _thrusters =
            new Dictionary<ThrusterDir, List<IMyThrust>>();

        private Program _program;
        private IMyRemoteControl _remoteControl;
        #endregion

        #region Properties
        public bool FactorGravity { get; set; } = FACTOR_GRAVITY_DEFAULT;
        public double BrakingDistanceFactor { get; set; } = BRAKING_DISTANCE_FACTOR_DEFAULT;
        public double Precision { get; set; } = PRECISION_DEFAULT;

        // Position PID controllers
        public PID PidXPos { get; } = new PID(KP_POS_DEFAULT, KI_POS_DEFAULT, KD_POS_DEFAULT, PID_TIME_STEP_DEFAULT);
        public PID PidYPos { get; } = new PID(KP_POS_DEFAULT, KI_POS_DEFAULT, KD_POS_DEFAULT, PID_TIME_STEP_DEFAULT);
        public PID PidZPos { get; } = new PID(KP_POS_DEFAULT, KI_POS_DEFAULT, KD_POS_DEFAULT, PID_TIME_STEP_DEFAULT);

        #endregion

        #region Methods
        public bool Initialize(Program program, out string errorMessage)
        {
            _program = program;
            return
                InitializeRemoteControl(out errorMessage) &&
                InitializeThrusters(out errorMessage);
        }

        private bool InitializeRemoteControl(out string errorMessage)
        {
            errorMessage = string.Empty;
            var remoteControls = new List<IMyRemoteControl>();
            _program.GridTerminalSystem.GetBlocksOfType(remoteControls);
            _remoteControl = remoteControls.FirstOrDefault();
            if (_remoteControl == null)
            {
                errorMessage = "Navigation: No remote control found!";
                return false;
            }

            return true;
        }

        private bool InitializeThrusters(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!_thrusters.ContainsKey(ThrusterDir.Forward))
            {
                _thrusters[ThrusterDir.Forward] = new List<IMyThrust>();
            }

            if (!_thrusters.ContainsKey(ThrusterDir.Backward))
            {
                _thrusters[ThrusterDir.Backward] = new List<IMyThrust>();
            }

            if (!_thrusters.ContainsKey(ThrusterDir.Up))
            {
                _thrusters[ThrusterDir.Up] = new List<IMyThrust>();
            }

            if (!_thrusters.ContainsKey(ThrusterDir.Down))
            {
                _thrusters[ThrusterDir.Down] = new List<IMyThrust>();
            }

            if (!_thrusters.ContainsKey(ThrusterDir.Left))
            {
                _thrusters[ThrusterDir.Left] = new List<IMyThrust>();
            }

            if (!_thrusters.ContainsKey(ThrusterDir.Right))
            {
                _thrusters[ThrusterDir.Right] = new List<IMyThrust>();
            }

            // Clear all thruster lists
            _thrusters[ThrusterDir.Forward].Clear();
            _thrusters[ThrusterDir.Backward].Clear();
            _thrusters[ThrusterDir.Up].Clear();
            _thrusters[ThrusterDir.Down].Clear();
            _thrusters[ThrusterDir.Left].Clear();
            _thrusters[ThrusterDir.Right].Clear();

            // Get all thrusters on the grid
            var allThrusters = new List<IMyThrust>();
            _program.GridTerminalSystem.GetBlocksOfType(allThrusters);

            // Get remote control's orientation vectors
            var remoteMatrix = _remoteControl.WorldMatrix;
            var forward = remoteMatrix.Forward;
            var backward = -forward;
            var up = remoteMatrix.Up;
            var down = -up;
            var right = remoteMatrix.Right;
            var left = -right;

            // Categorize thrusters based on their thrust direction relative to remote control
            foreach (var thruster in allThrusters)
            {
                var thrusterDirection =
                    thruster.WorldMatrix.Backward; // Thrust direction is opposite of thruster's backward

                // Calculate dot products to determine which direction the thruster points
                var forwardDot = Vector3D.Dot(thrusterDirection, forward);
                var backwardDot = Vector3D.Dot(thrusterDirection, backward);
                var upDot = Vector3D.Dot(thrusterDirection, up);
                var downDot = Vector3D.Dot(thrusterDirection, down);
                var rightDot = Vector3D.Dot(thrusterDirection, right);
                var leftDot = Vector3D.Dot(thrusterDirection, left);

                // Add to appropriate list based on alignment threshold
                if (forwardDot > 0.7) // 0.7 threshold for alignment
                {
                    _thrusters[ThrusterDir.Forward].Add(thruster);
                    thruster.CustomName = $"Forward Thruster {_thrusters[ThrusterDir.Forward].Count}";
                }
                else if (backwardDot > 0.7)
                {
                    _thrusters[ThrusterDir.Backward].Add(thruster);
                    thruster.CustomName = $"Backward Thruster {_thrusters[ThrusterDir.Backward].Count}";
                }
                else if (upDot > 0.7)
                {
                    _thrusters[ThrusterDir.Up].Add(thruster);
                    thruster.CustomName = $"Up Thruster {_thrusters[ThrusterDir.Up].Count}";
                }
                else if (downDot > 0.7)
                {
                    _thrusters[ThrusterDir.Down].Add(thruster);
                    thruster.CustomName = $"Down Thruster {_thrusters[ThrusterDir.Down].Count}";
                }
                else if (rightDot > 0.7)
                {
                    _thrusters[ThrusterDir.Right].Add(thruster);
                    thruster.CustomName = $"Right Thruster {_thrusters[ThrusterDir.Right].Count}";
                }
                else if (leftDot > 0.7)
                {
                    _thrusters[ThrusterDir.Left].Add(thruster);
                    thruster.CustomName = $"Left Thruster {_thrusters[ThrusterDir.Left].Count}";
                }
            }

            if (_thrusters[ThrusterDir.Forward].Count == 0)
            {
                errorMessage = "Navigation: No forward thrusters found!";
                return false;
            }

            if (_thrusters[ThrusterDir.Backward].Count == 0)
            {
                errorMessage = "Navigation: No backward thrusters found!";
                return false;
            }

            if (_thrusters[ThrusterDir.Up].Count == 0)
            {
                errorMessage = "Navigation: No up thrusters found!";
                return false;
            }

            if (_thrusters[ThrusterDir.Down].Count == 0)
            {
                errorMessage = "Navigation: No down thrusters found!";
                return false;
            }

            if (_thrusters[ThrusterDir.Right].Count == 0)
            {
                errorMessage = "Navigation: No right thrusters found!";
                return false;
            }

            if (_thrusters[ThrusterDir.Left].Count == 0)
            {
                errorMessage = "Navigation: No left thrusters found!";
                return false;
            }

            return true;
        }

        public void SetThrusterGroupPower(ThrusterDir dir, double totalPower)
        {
            foreach (var thruster in _thrusters[dir])
            {
                var maxPower = thruster.MaxThrust;
                var power = Math.Min(totalPower, maxPower);
                thruster.ThrustOverride = (float)power;
                totalPower = Math.Max(0, totalPower - power);
            }
        }

        public void Stop()
        {
            foreach (var key in _thrusters.Keys)
            {
                SetThrusterGroupPower(key, 0);
            }
        }

        public Vector3D GetCurrentPosition()
        {
            return _remoteControl.GetPosition();
        }

        private void DisableThrusterOverrides()
        {
            foreach (var key in _thrusters.Keys)
            {
                SetThrusterGroupPower(key, 0);
            }
        }

        public bool NavigateTo(Vector3D target, double maxSpeed = 20.0)
        {
            var pos = _remoteControl.GetPosition();
            var vel = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            double mass = _remoteControl.CalculateShipMass().TotalMass;

            var toTarget = target - pos;
            var distance = toTarget.Length();

            // --- Arrival check ---
            if (distance < Precision && vel.Length() < Precision)
            {
                DisableThrusterOverrides();
                PidXPos.Reset(); // reset PID state
                PidYPos.Reset();
                PidZPos.Reset();
                return true;
            }

            // --- Step 1: Calculate desired velocity ---
            // Some adjustments for faster approach when close to target
            var actualMinSpeed = Precision;
            if (distance < Precision * 2)
            {
                actualMinSpeed = Math.Max(Precision / 2, distance / 2);
            }
            else if (distance < Precision * 10)
            {
                actualMinSpeed = Precision * 5;
            }

            var desiredSpeed = Math.Min(maxSpeed, Math.Max(actualMinSpeed, distance / BrakingDistanceFactor));
            var desiredVel = Vector3D.Normalize(toTarget) * desiredSpeed;
            var velError = desiredVel - vel;

            // --- Step 2: Use PID controllers for velocity control ---
            var accelX = PidXPos.Control(velError.X);
            var accelY = PidYPos.Control(velError.Y);
            var accelZ = PidZPos.Control(velError.Z);

            var desiredAccel = new Vector3D(accelX, accelY, accelZ);

            // --- Step 3: Compensate gravity ---
            if (FactorGravity)
            {
                desiredAccel -= gravity;
            }

            // --- Step 4: Convert to force ---
            var desiredForce = desiredAccel * mass;

            // --- Step 5: Apply via thrusters ---
            ApplyForce(desiredForce);

            return false;
        }

        private void ApplyForce(Vector3D desiredForce)
        {
            var wm = _remoteControl.WorldMatrix;

            // Forward / Backward
            var forwardN = Math.Max(0, Vector3D.Dot(desiredForce, wm.Forward));
            var backwardN = Math.Max(0, -Vector3D.Dot(desiredForce, wm.Forward));

            // Left / Right
            var leftN = Math.Max(0, Vector3D.Dot(desiredForce, wm.Left));
            var rightN = Math.Max(0, -Vector3D.Dot(desiredForce, wm.Left));

            // Up / Down
            var upN = Math.Max(0, Vector3D.Dot(desiredForce, wm.Up));
            var downN = Math.Max(0, -Vector3D.Dot(desiredForce, wm.Up));

            SetThrusterOverride(ThrusterDir.Forward, forwardN);
            SetThrusterOverride(ThrusterDir.Backward, backwardN);
            SetThrusterOverride(ThrusterDir.Left, leftN);
            SetThrusterOverride(ThrusterDir.Right, rightN);
            SetThrusterOverride(ThrusterDir.Up, upN);
            SetThrusterOverride(ThrusterDir.Down, downN);
        }

        private void SetThrusterOverride(ThrusterDir dir, double requiredForce)
        {
            if (_thrusters.Count == 0)
            {
                return;
            }

            var maxForce = _thrusters[dir].Sum(t => t.MaxEffectiveThrust);
            var ratio = MathHelper.Clamp(requiredForce / maxForce, 0, 1);

            foreach (var thruster in _thrusters[dir])
            {
                thruster.ThrustOverridePercentage = (float)ratio;
            }
        }


        #endregion
    }
}
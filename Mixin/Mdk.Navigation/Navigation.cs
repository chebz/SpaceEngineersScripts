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

        public class NavigationSection : Section
        {
            private Navigation _navigation;
            public BoolProperty FactorGravity { get; } = new BoolProperty("FactorGravity", FACTOR_GRAVITY_DEFAULT);
            public DoubleProperty Accel { get; } = new DoubleProperty("Accel", ACCEL_DEFAULT);
            public DoubleProperty Precision { get; } = new DoubleProperty("Precision", PRECISION_DEFAULT);
            public DoubleProperty KP { get; } = new DoubleProperty("KP", KP_DEFAULT);
            public DoubleProperty KI { get; } = new DoubleProperty("KI", KI_DEFAULT);
            public DoubleProperty KD { get; } = new DoubleProperty("KD", KD_DEFAULT);
            
            public NavigationSection(Navigation navigation) : base("Navigation")
            {
                _navigation = navigation;
                _properties.Add(FactorGravity);
                _properties.Add(Accel);
                _properties.Add(Precision);
                _properties.Add(KP);
                _properties.Add(KI);
                _properties.Add(KD);
                KP.ValueChanged += (value) => {
                    _navigation.PidX.Kp = value;
                    _navigation.PidY.Kp = value;
                    _navigation.PidZ.Kp = value;
                };
                KI.ValueChanged += (value) => {
                    _navigation.PidX.Ki = value;
                    _navigation.PidY.Ki = value;
                    _navigation.PidZ.Ki = value;
                };
                KD.ValueChanged += (value) => {
                    _navigation.PidX.Kd = value;
                    _navigation.PidY.Kd = value;
                    _navigation.PidZ.Kd = value;
                };
            }
        }


        #region Fields
        private const double PID_TIME_STEP_DEFAULT = 1.0 / 6.0;

        // Default Position PID settings
        private const double KP_DEFAULT = 3.0;
        private const double KI_DEFAULT = 1.0;
        private const double KD_DEFAULT = 0.0;
        private const bool FACTOR_GRAVITY_DEFAULT = true;
        private const double ACCEL_DEFAULT = 0.25;
        private const double PRECISION_DEFAULT = 0.1;


        private readonly Dictionary<ThrusterDir, List<IMyThrust>> _thrusters =
            new Dictionary<ThrusterDir, List<IMyThrust>>();

        private Program _program;
        private IMyRemoteControl _remoteControl;
        private CustomDataConnector _customDataConnector;
        private NavigationSection _section;
        #endregion

        #region Properties
        // Position PID controllers
        public PID PidX { get; } = new PID(KP_DEFAULT, KI_DEFAULT, KD_DEFAULT, PID_TIME_STEP_DEFAULT);
        public PID PidY { get; } = new PID(KP_DEFAULT, KI_DEFAULT, KD_DEFAULT, PID_TIME_STEP_DEFAULT);
        public PID PidZ { get; } = new PID(KP_DEFAULT, KI_DEFAULT, KD_DEFAULT, PID_TIME_STEP_DEFAULT);

        #endregion

        #region Methods
        public bool Initialize(Program program, CustomDataConnector customDataConnector, out string errorMessage)
        {
            _program = program;
            _customDataConnector = customDataConnector;
            // Initialize remote control
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "Navigation: No remote control found!";
                return false;
            }

            _section = new NavigationSection(this);
            _customDataConnector.AddSection(_section);

            return InitializeThrusters(out errorMessage);
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
            _program.GridTerminalSystem.GetBlocksOfType<IMyThrust>(allThrusters);

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

            // Check if we have at least some thrusters (any direction)
            var totalThrusters = _thrusters.Values.Sum(list => list.Count);
            if (totalThrusters == 0)
            {
                errorMessage = "Navigation: No thrusters found!";
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
            PidX.Reset();
            PidY.Reset();
            PidZ.Reset();
        }

        public void PowerOff()
        {
            foreach (var thruster in _thrusters.Values.SelectMany(list => list))
            {
                thruster.Enabled = false;
            }
        }

        public void PowerOn()
        {
            foreach (var thruster in _thrusters.Values.SelectMany(list => list))
            {
                thruster.Enabled = true;
            }
        }

        public Vector3D GetCurrentPosition()
        {
            return _remoteControl.GetPosition();
        }

        public bool NavigateTo(Vector3D target, double maxSpeed = 20.0, double precision = 0, Vector3D targetSpeed = default(Vector3D))
        {
            precision = precision == 0 ? _section.Precision.Value : precision;
            var pos = _remoteControl.GetPosition();
            var vel = _remoteControl.GetShipVelocities().LinearVelocity;
            var gravity = _remoteControl.GetNaturalGravity();
            double mass = _remoteControl.CalculateShipMass().TotalMass;

            var toTarget = target - pos;
            var distance = toTarget.Length();

            // --- Arrival check ---
            var velDifference = vel - targetSpeed;
            if (distance < precision && velDifference.Length() < precision)
            {
                Stop();
                return true;
            }

            // --- Step 1: Calculate desired velocity ---
            var desiredSpeed = CalculateDesiredSpeedAtPosition(target, maxSpeed);
            var desiredVel = Vector3D.Normalize(toTarget) * desiredSpeed + targetSpeed;
            var velError = desiredVel - vel;

            // --- Step 2: Use PID controllers for velocity control ---
            // var accelX = PidX.Control(velError.X);
            // var accelY = PidY.Control(velError.Y);
            // var accelZ = PidZ.Control(velError.Z);

            var accelX = velError.X;
            var accelY = velError.Y;
            var accelZ = velError.Z;

            var desiredAccel = new Vector3D(accelX, accelY, accelZ);

            // --- Step 3: Compensate gravity ---
            if (_section.FactorGravity.Value)
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
            if (_thrusters.Count == 0 || !_thrusters.ContainsKey(dir) || _thrusters[dir].Count == 0)
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

        private double CalculateDesiredSpeedAtPosition(Vector3D target, double maxSpeed)
        {
            var currentPos = _remoteControl.GetPosition();
            var distance = Vector3D.Distance(currentPos, target);
            
            // Correct breaking distance formula: v²/(2a)
            // This gives us the distance needed to stop from maxSpeed
            var breakingDistance = (maxSpeed * maxSpeed) / (2.0 * _section.Accel.Value);
            
            if (distance > breakingDistance)
            {
                return maxSpeed;
            }
            
            // When we're within breaking distance, scale down the speed
            // Use a more gradual approach: sqrt(distance/breakingDistance) * maxSpeed
            // This gives a smoother deceleration curve
            var speedRatio = Math.Sqrt(distance / breakingDistance);
            return speedRatio * maxSpeed;
        }
        #endregion
    }
}
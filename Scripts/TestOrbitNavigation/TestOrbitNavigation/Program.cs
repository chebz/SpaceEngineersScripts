using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private const double DEFAULT_MAX_SPEED = 5.0;

        private CustomDataConnector _customDataConnector;
        private Navigation _navigation;
        private Alignment _alignment;
        private OrbitController _orbitController;
        private IMyRemoteControl _remoteControl;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            _navigation = new Navigation();
            _alignment = new Alignment();

            string errorMessage;
            if (!_navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!_alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                Echo("Error: No remote control found on this grid");
            }

            _orbitController = new OrbitController(this, _navigation, _alignment, _remoteControl);

            _customDataConnector.Load();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                HandleCommand(argument);
            }

            _orbitController.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            var trimmed = argument.Trim();
            var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var command = parts[0].ToLower();

            switch (command)
            {
                case "start":
                    if (parts.Length < 2)
                    {
                        Echo("Usage: start <radius> [maxSpeed]");
                        break;
                    }

                    double radius;
                    if (!double.TryParse(parts[1], out radius) || radius <= 0)
                    {
                        Echo("Radius must be a positive number");
                        break;
                    }

                    double maxSpeed = DEFAULT_MAX_SPEED;
                    if (parts.Length >= 3)
                    {
                        if (!double.TryParse(parts[2], out maxSpeed) || maxSpeed <= 0)
                        {
                            Echo("Max speed must be a positive number");
                            break;
                        }
                    }

                    if (_orbitController.StartOrbit(radius, maxSpeed))
                    {
                        Echo($"Starting orbit at radius {radius:F1} m (max speed {maxSpeed:F1} m/s)");
                    }
                    else
                    {
                        Echo("Unable to start orbiting");
                    }
                    break;

                case "stop":
                    _orbitController.StopOrbit();
                    Echo("Stopping orbit");
                    break;

                default:
                    Echo("Commands: start <radius> [maxSpeed], stop");
                    break;
            }
        }

        private class OrbitController : Context
        {
            private readonly Program _program;
            private readonly Navigation _navigation;
            private readonly Alignment _alignment;
            private readonly IMyRemoteControl _remoteControl;

            private double _orbitRadius;
            private double _maxSpeed;
            private Vector3D _orbitCenter;
            private Vector3D _orbitUp;
            private double _yawOffset;

            public OrbitController(Program program, Navigation navigation, Alignment alignment, IMyRemoteControl remoteControl)
            {
                _program = program;
                _navigation = navigation;
                _alignment = alignment;
                _remoteControl = remoteControl;

                TransitionTo(new IdleState(this));
            }

            public bool StartOrbit(double radius, double maxSpeed)
            {
                if (_navigation == null || _alignment == null || _remoteControl == null)
                {
                    return false;
                }

                _orbitRadius = radius;
                _maxSpeed = maxSpeed;
                _orbitCenter = _remoteControl.GetPosition();
                _yawOffset = 180.0;
                var gravity = _remoteControl.GetNaturalGravity();
                if (gravity.LengthSquared() > 1e-6)
                {
                    _orbitUp = Vector3D.Normalize(-gravity);
                    _program.Echo($"Gravity: {gravity.Length():F1} m/s^2");
                }
                else
                {
                    _orbitUp = _remoteControl.WorldMatrix.Up;
                }

                TransitionTo(new OrbitingState(this));
                return true;
            }

            public void StopOrbit()
            {
                _navigation.Stop();
                _alignment.Stop();
                TransitionTo(new IdleState(this));
            }

            private class IdleState : State<OrbitController>
            {
                public IdleState(OrbitController context) : base(context) { }
            }

            private class OrbitingState : State<OrbitController>
            {
                public OrbitingState(OrbitController context) : base(context) { }

                public override void Execute()
                {
                    if (_context._navigation == null || _context._alignment == null || _context._remoteControl == null)
                    {
                        return;
                    }

                    var onOrbit = _context._navigation.OrbitPoint(
                        _context._orbitCenter,
                        _context._orbitRadius,
                        _context._orbitUp,
                        _context._maxSpeed);

                    //_context._alignment.AlignWithTarget(_context._orbitCenter, _context._yawOffset);
                }
            }
        }
    }
}

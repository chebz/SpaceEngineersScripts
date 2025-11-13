using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private readonly CustomDataConnector _customDataConnector;
        private PathfinderPatrolController _controller;
        private readonly List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();
        private bool _initialized;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!_initialized)
            {
                if (!Initialize())
                {
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(argument))
            {
                HandleCommand(argument);
            }

            var deltaTime = Runtime.TimeSinceLastRun;
            _controller.Execute(deltaTime);
            _customDataConnector.Update();
        }

        private bool Initialize()
        {
            var remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (remoteControl == null)
            {
                Echo("PathfinderPatrol: Remote control not found");
                return false;
            }

            _statusSurfaces.Clear();

            var panels = this.GetLocalBlocks<IMyTextPanel>();
            foreach (var panel in panels)
            {
                if (panel.CustomName.IndexOf("[PP]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                    _statusSurfaces.Add(panel);
                }
            }

            var surface = Me.GetSurface(0);
            surface.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            _statusSurfaces.Add(surface);

            _customDataConnector.Initialize(this);

            var pathfinderProgrammableBlock = FindPathfinderProgrammableBlock();

            _controller = new PathfinderPatrolController(this, _customDataConnector, remoteControl, _statusSurfaces, pathfinderProgrammableBlock);
            _customDataConnector.Load();

            _initialized = true;
            return true;
        }

        private void HandleCommand(string argument)
        {
            var trimmed = argument.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            var command = trimmed.ToLowerInvariant();
            switch (command)
            {
                case "start":
                    if (_controller.StartPatrol())
                    {
                        Echo("PathfinderPatrol: Starting patrol");
                    }
                    break;

                case "stop":
                    _controller.StopPatrol();
                    Echo("PathfinderPatrol: Stopping patrol");
                    break;

                case "pf_start":
                    _controller.OnPathfinderEvent("start");
                    break;

                case "pf_stop":
                    _controller.OnPathfinderEvent("stop");
                    break;

                case "pf_nopath":
                    _controller.OnPathfinderEvent("nopath");
                    break;

                default:
                    Echo("Commands: start, stop");
                    break;
            }
        }

        private IMyProgrammableBlock FindPathfinderProgrammableBlock()
        {
            var programmableBlocks = this.GetLocalBlocks<IMyProgrammableBlock>();
            foreach (var programmableBlock in programmableBlocks)
            {
                if (programmableBlock.EntityId == Me.EntityId)
                {
                    continue;
                }

                if (programmableBlock.CustomName.IndexOf("[PF]", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return programmableBlock;
                }
            }

            return null;
        }

        private class PathfinderPatrolController : Context
        {
            private readonly Program _program;
            private readonly CustomDataConnector _customDataConnector;
            private readonly IMyRemoteControl _remoteControl;
            private readonly List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();
            private IMyProgrammableBlock _pathfinderProgrammableBlock;
            private readonly PatrolSection _section;

            private bool _isRunning;
            private int _nextDestinationIndex = -1;
            private int _lastDestinationIndex = -1;
            private double _deltaSeconds;

            public PathfinderPatrolController(Program program, CustomDataConnector customDataConnector, IMyRemoteControl remoteControl, List<IMyTextSurface> statusSurfaces, IMyProgrammableBlock pathfinderProgrammableBlock)
            {
                _program = program;
                _customDataConnector = customDataConnector;
                _remoteControl = remoteControl;
                if (statusSurfaces != null)
                {
                    _statusSurfaces.AddRange(statusSurfaces);
                }

                _pathfinderProgrammableBlock = pathfinderProgrammableBlock;
                _section = new PatrolSection();
                _customDataConnector.AddSection(_section);

                TransitionTo(new IdleState(this));
            }

            public void Execute(TimeSpan deltaTime)
            {
                _deltaSeconds = Math.Max(0, deltaTime.TotalSeconds);
                base.Execute();
            }

            public bool StartPatrol()
            {
                if (_isRunning)
                {
                    return false;
                }

                if (!HasValidGps(_section.PointA) || !HasValidGps(_section.PointB))
                {
                    _program.Echo("PathfinderPatrol: Configure PointA and PointB GPS values");
                    return false;
                }

                _section.StartOnLoad.Value = true;

                _isRunning = true;
                _lastDestinationIndex = -1;
                _nextDestinationIndex = GetInitialDestinationIndex();
                NavigateToCurrentDestination();
                return true;
            }

            public void StopPatrol()
            {
                _section.StartOnLoad.Value = false;
                _isRunning = false;
                _nextDestinationIndex = -1;
                _lastDestinationIndex = -1;

                TransitionTo(new IdleState(this));
                _pathfinderProgrammableBlock?.TryRun("stop");
                UpdateStatus("Idle", "Patrol stopped");
            }

            public void OnPathfinderEvent(string eventName)
            {
                var state = CurrentState as PathfinderPatrolState;
                if (state == null)
                {
                    return;
                }

                switch (eventName)
                {
                    case "start":
                        state.OnPathfinderStarted();
                        break;
                    case "stop":
                        state.OnPathfinderStopped();
                        break;
                    case "nopath":
                        state.OnPathfinderNoPath();
                        break;
                }
            }

            public double DeltaSeconds => _deltaSeconds;

            private int GetInitialDestinationIndex()
            {
                if (_remoteControl == null)
                {
                    return 0;
                }

                var position = _remoteControl.GetPosition();
                var distanceToA = Vector3D.DistanceSquared(position, _section.PointA.Value);
                var distanceToB = Vector3D.DistanceSquared(position, _section.PointB.Value);
                return distanceToA <= distanceToB ? 0 : 1;
            }

            private bool HasValidGps(GPSProperty property)
            {
                return property != null && property.Value.LengthSquared() >= 1e-3;
            }

            private GPSProperty GetPointProperty(int index)
            {
                return index == 0 ? _section.PointA : _section.PointB;
            }

            private string BuildGpsArgument(GPSProperty property, string defaultName)
            {
                if (property == null)
                {
                    return null;
                }

                var gpsString = property.ValueToString();
                if (string.IsNullOrEmpty(gpsString) || gpsString.StartsWith(":", StringComparison.Ordinal))
                {
                    var coords = property.Value;
                    gpsString = $"{defaultName}:{coords.X}:{coords.Y}:{coords.Z}";
                }

                return gpsString;
            }

            private bool SendPathfinderCommand(int destinationIndex)
            {
                if (_pathfinderProgrammableBlock == null)
                {
                    return false;
                }

                var property = GetPointProperty(destinationIndex);
                var defaultName = destinationIndex == 0 ? "PointA" : "PointB";
                var gpsArgument = BuildGpsArgument(property, defaultName);
                if (string.IsNullOrWhiteSpace(gpsArgument))
                {
                    _program.Echo($"PathfinderPatrol: GPS '{defaultName}' not configured");
                    return false;
                }

                var command = $"start {gpsArgument} {_program.Me.CustomName}";
                if (!_pathfinderProgrammableBlock.TryRun(command))
                {
                    _program.Echo($"PathfinderPatrol: Failed to start pathfinding with '{command}'");
                    return false;
                }

                UpdateStatus("Navigating", $"Destination: {defaultName}");
                return true;
            }

            private void NavigateToCurrentDestination()
            {
                if (!_isRunning)
                {
                    return;
                }

                if (_nextDestinationIndex < 0)
                {
                    _nextDestinationIndex = 0;
                }

                TransitionTo(new NavigatingState(this, _nextDestinationIndex));
            }

            private void OnNavigationCompleted(int destinationIndex)
            {
                _lastDestinationIndex = destinationIndex;
                _nextDestinationIndex = destinationIndex == 0 ? 1 : 0;

                var waitSeconds = Math.Max(0, _section.WaitDuration.Value);
                if (!_isRunning)
                {
                    TransitionTo(new IdleState(this));
                    return;
                }

                if (waitSeconds <= 0.01)
                {
                    NavigateToCurrentDestination();
                }
                else
                {
                    TransitionTo(new WaitingState(this, waitSeconds, _nextDestinationIndex));
                }
            }

            private void OnNavigationFailed(string reason)
            {
                UpdateStatus("Idle", reason);
                StopPatrol();
            }

            private void UpdateStatus(string state, params string[] details)
            {
                if (_statusSurfaces.Count == 0)
                {
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("=== Pathfinder Patrol ===");
                builder.AppendLine($"State: {state}");
                if (details != null)
                {
                    foreach (var line in details)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            builder.AppendLine(line);
                        }
                    }
                }
                var text = builder.ToString();
                foreach (var surface in _statusSurfaces)
                {
                    surface.WriteText(text);
                }
            }

            private abstract class PathfinderPatrolState : State<PathfinderPatrolController>
            {
                protected PathfinderPatrolState(PathfinderPatrolController context) : base(context)
                {
                }

                public virtual void OnPathfinderStarted() { }
                public virtual void OnPathfinderStopped() { }
                public virtual void OnPathfinderNoPath() { }
            }

            private class IdleState : PathfinderPatrolState
            {
                public IdleState(PathfinderPatrolController context) : base(context)
                {
                }

                public override void Enter()
                {
                    _context.UpdateStatus("Idle");
                    _context._program.Echo("Idle");
                }

                public override void Execute()
                {
                    if (!_context._isRunning && _context._section.StartOnLoad.Value)
                    {
                        _context.StartPatrol();
                    }
                }
            }

            private class NavigatingState : PathfinderPatrolState
            {
                private readonly int _destinationIndex;
                private bool _commandIssued;

                public NavigatingState(PathfinderPatrolController context, int destinationIndex) : base(context)
                {
                    _destinationIndex = destinationIndex;
                }

                public override void Enter()
                {
                    if (!_context.SendPathfinderCommand(_destinationIndex))
                    {
                        _context.OnNavigationFailed("Failed to start pathfinding");
                        return;
                    }

                    _context._program.Echo("Navigating");

                    _commandIssued = true;
                }

                public override void OnPathfinderStopped()
                {
                    if (_commandIssued)
                    {
                        _context.OnNavigationCompleted(_destinationIndex);
                    }
                }

                public override void OnPathfinderNoPath()
                {
                    _context.OnNavigationFailed("Path not found");
                }
            }

            private class WaitingState : PathfinderPatrolState
            {
                private double _remainingSeconds;
                private readonly int _nextDestinationIndex;

                public WaitingState(PathfinderPatrolController context, double waitSeconds, int nextDestinationIndex) : base(context)
                {
                    _remainingSeconds = waitSeconds;
                    _nextDestinationIndex = nextDestinationIndex;
                }

                public override void Enter()
                {
                    _context.UpdateStatus("Waiting", $"Delay: {_remainingSeconds:F1} s");
                    _context._program.Echo("Waiting");
                }

                public override void Execute()
                {
                    if (!_context._isRunning)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    _remainingSeconds -= _context.DeltaSeconds;
                    if (_remainingSeconds <= 0)
                    {
                        _context.TransitionTo(new NavigatingState(_context, _nextDestinationIndex));
                    }
                    else
                    {
                        _context.UpdateStatus("Waiting", $"Delay: {_remainingSeconds:F1} s");
                    }
                }
            }

            private class PatrolSection : Section
            {
                private const double WAIT_DURATION_DEFAULT = 5.0;

                public GPSProperty PointA { get; } = new GPSProperty("PointA", Vector3D.Zero, "PointA");
                public GPSProperty PointB { get; } = new GPSProperty("PointB", Vector3D.Zero, "PointB");
                public DoubleProperty WaitDuration { get; } = new DoubleProperty("WaitDuration", WAIT_DURATION_DEFAULT, comment: "Seconds to wait at each waypoint");
                public BoolProperty StartOnLoad { get; } = new BoolProperty("StartOnLoad", false);

                public PatrolSection() : base("PathfinderPatrol")
                {
                    _properties.Add(PointA);
                    _properties.Add(PointB);
                    _properties.Add(WaitDuration);
                    _properties.Add(StartOnLoad);
                }
            }
        }
    }
}


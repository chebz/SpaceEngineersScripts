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
        private PathfinderFollowController _controller;
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
                Echo("PathfinderFollow: Remote control not found");
                return false;
            }

            _statusSurfaces.Clear();

            var panels = this.GetLocalBlocks<IMyTextPanel>();
            foreach (var panel in panels)
            {
                if (panel.CustomName.IndexOf("[PF]", StringComparison.OrdinalIgnoreCase) >= 0)
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

            _controller = new PathfinderFollowController(this, _customDataConnector, remoteControl, _statusSurfaces, pathfinderProgrammableBlock);
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
                    _controller.StartFollowing();
                    break;

                case "stop":
                    _controller.StopFollowing();
                    Echo("PathfinderFollow: Stopping follow");
                    break;

                case "pf_start":
                    _controller.OnPathfinderEvent("pf_start");
                    break;

                case "pf_stop":
                    _controller.OnPathfinderEvent("pf_stop");
                    break;

                case "pf_nopath":
                    _controller.OnPathfinderEvent("pf_nopath");
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

        private class PathfinderFollowController : Context
        {
            private readonly Program _program;
            private readonly CustomDataConnector _customDataConnector;
            private readonly IMyRemoteControl _remoteControl;
            private readonly List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();
            private IMyProgrammableBlock _pathfinderProgrammableBlock;
            private readonly FollowSection _section;

            private double _deltaSeconds;

            public PathfinderFollowController(Program program, CustomDataConnector customDataConnector, IMyRemoteControl remoteControl, List<IMyTextSurface> statusSurfaces, IMyProgrammableBlock pathfinderProgrammableBlock)
            {
                _program = program;
                _customDataConnector = customDataConnector;
                _remoteControl = remoteControl;
                if (statusSurfaces != null)
                {
                    _statusSurfaces.AddRange(statusSurfaces);
                }

                _pathfinderProgrammableBlock = pathfinderProgrammableBlock;
                _section = new FollowSection();
                _customDataConnector.AddSection(_section);

                TransitionTo(new IdleState(this));
            }

            public void Execute(TimeSpan deltaTime)
            {
                _deltaSeconds = Math.Max(0, deltaTime.TotalSeconds);
                base.Execute();
            }

            public void StartFollowing()
            {
                var gpsName = _section.TargetGpsName.Value;
                if (string.IsNullOrWhiteSpace(gpsName))
                {
                    _program.Echo("PathfinderFollow: Configure TargetGpsName property");
                    return;
                }

                _section.StartOnLoad.Value = true;

                NavigateToTarget();
            }

            public void StopFollowing()
            {
                _section.StartOnLoad.Value = false;
                _program.Echo("PathfinderFollow: Stopping follow");
                TransitionTo(new IdleState(this));
                _pathfinderProgrammableBlock?.TryRun("stop");
                UpdateStatus("Idle", "Follow stopped");
            }

            public void OnPathfinderEvent(string eventName)
            {
                var state = CurrentState as PathfinderFollowState;
                if (state == null)
                {
                    return;
                }

                switch (eventName)
                {
                    case "pf_start":
                        state.OnPathfinderStarted();
                        break;
                    case "pf_stop":
                        state.OnPathfinderStopped();
                        break;
                    case "pf_nopath":
                        state.OnPathfinderNoPath();
                        break;
                }
            }

            public double DeltaSeconds => _deltaSeconds;

            private bool HasValidGpsName(string gpsName)
            {
                return !string.IsNullOrWhiteSpace(gpsName);
            }

            private bool SendPathfinderCommand(string gpsName)
            {
                if (_pathfinderProgrammableBlock == null)
                {
                    return false;
                }

                if (!HasValidGpsName(gpsName))
                {
                    _program.Echo("PathfinderFollow: GPS name not configured");
                    return false;
                }

                var programmableBlockName = _program.Me.CustomName;
                var command = $"start {gpsName}|{programmableBlockName}";
                if (!_pathfinderProgrammableBlock.TryRun(command))
                {
                    _program.Echo($"PathfinderFollow: Failed to start pathfinding with '{command}'");
                    return false;
                }

                UpdateStatus("Following", $"Target: {gpsName}");
                return true;
            }

            private void NavigateToTarget()
            {
                var gpsName = _section.TargetGpsName.Value;
                _program.Echo("PathfinderFollow: Navigating to target: " + gpsName);
                if (string.IsNullOrWhiteSpace(gpsName))
                {
                    _program.Echo("PathfinderFollow: No GPS name configured");
                    StopFollowing();
                    return;
                }

                TransitionTo(new FollowingState(this, gpsName));
            }

            private void OnFollowingStopped()
            {
                if (!_section.StartOnLoad.Value)
                {
                    TransitionTo(new IdleState(this));
                    return;
                }

                var gpsName = _section.TargetGpsName.Value;
                if (string.IsNullOrWhiteSpace(gpsName))
                {
                    TransitionTo(new IdleState(this));
                    return;
                }

                var waitSeconds = Math.Max(0, _section.WaitSeconds.Value);
                TransitionTo(new WaitingState(this, waitSeconds, gpsName));
            }

            private void OnFollowingFailed(string reason)
            {
                UpdateStatus("Idle", reason);
                _program.Echo("PathfinderFollow: Following failed: " + reason);
                StopFollowing();
            }

            private void UpdateStatus(string state, params string[] details)
            {
                if (_statusSurfaces.Count == 0)
                {
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("=== Pathfinder Follow ===");
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

            private abstract class PathfinderFollowState : State<PathfinderFollowController>
            {
                protected PathfinderFollowState(PathfinderFollowController context) : base(context)
                {
                }

                public virtual void OnPathfinderStarted() { }
                public virtual void OnPathfinderStopped() { }
                public virtual void OnPathfinderNoPath() { }
            }

            private class IdleState : PathfinderFollowState
            {
                public IdleState(PathfinderFollowController context) : base(context)
                {
                }

                public override void Enter()
                {
                    _context.UpdateStatus("Idle");
                    _context._program.Echo("Idle");
                }

                public override void Execute()
                {
                    if (_context._section.StartOnLoad.Value)
                    {
                        _context.StartFollowing();
                    }
                }
            }

            private class FollowingState : PathfinderFollowState
            {
                private readonly string _gpsName;
                private bool _commandIssued;

                public FollowingState(PathfinderFollowController context, string gpsName) : base(context)
                {
                    _gpsName = gpsName;
                    _context._program.Echo("FollowingState: Entering: " + gpsName);
                }

                public override void Enter()
                {
                    if (!_context.SendPathfinderCommand(_gpsName))
                    {
                        _context.OnFollowingFailed("Failed to start pathfinding");
                        return;
                    }

                    _context._program.Echo("Following");

                    _commandIssued = true;
                }

                public override void Execute()
                {
                    _context._program.Echo("FollowingState: Executing");
                    var currentGpsName = _context._section.TargetGpsName.Value;
                    if (string.IsNullOrWhiteSpace(currentGpsName) || !string.Equals(currentGpsName, _gpsName, StringComparison.OrdinalIgnoreCase))
                    {
                        _context.NavigateToTarget();
                        return;
                    }
                }

                public override void OnPathfinderStopped()
                {
                    if (_commandIssued)
                    {
                        _context.OnFollowingStopped();
                    }
                }

                public override void OnPathfinderNoPath()
                {
                    _context.OnFollowingFailed("Path not found");
                }
            }

            private class WaitingState : PathfinderFollowState
            {
                private double _remainingSeconds;
                private readonly string _gpsName;

                public WaitingState(PathfinderFollowController context, double waitSeconds, string gpsName) : base(context)
                {
                    _remainingSeconds = waitSeconds;
                    _gpsName = gpsName;
                }

                public override void Enter()
                {
                    _context.UpdateStatus("Waiting", $"Resuming in {_remainingSeconds:F1} s");
                    _context._program.Echo("Waiting before resuming follow");
                }

                public override void Execute()
                {
                    if (!_context._section.StartOnLoad.Value)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    var currentGpsName = _context._section.TargetGpsName.Value;
                    if (string.IsNullOrWhiteSpace(currentGpsName) || !string.Equals(currentGpsName, _gpsName, StringComparison.OrdinalIgnoreCase))
                    {
                        _context.NavigateToTarget();
                        return;
                    }

                    _remainingSeconds -= _context.DeltaSeconds;
                    if (_remainingSeconds <= 0)
                    {
                        _context.TransitionTo(new FollowingState(_context, _gpsName));
                    }
                    else
                    {
                        _context.UpdateStatus("Waiting", $"Resuming in {_remainingSeconds:F1} s");
                    }
                }
            }

            private class FollowSection : Section
            {
                private const double WAIT_SECONDS_DEFAULT = 1.0;

                public StringProperty TargetGpsName { get; } = new StringProperty("TargetGpsName", "", comment: "GPS name to follow");
                public DoubleProperty WaitSeconds { get; } = new DoubleProperty("WaitSeconds", WAIT_SECONDS_DEFAULT, comment: "Seconds to wait before resuming follow after stop");
                public BoolProperty StartOnLoad { get; } = new BoolProperty("StartOnLoad", false);

                public FollowSection() : base("PathfinderFollow")
                {
                    _properties.Add(TargetGpsName);
                    _properties.Add(WaitSeconds);
                    _properties.Add(StartOnLoad);
                }
            }
        }
    }
}

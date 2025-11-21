using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private readonly CustomDataConnector _customDataConnector;
        private SquadNavigationController _controller;
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

            HandlePathfinderEvents(argument);
        }

        private bool Initialize()
        {
            var remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (remoteControl == null)
            {
                Echo("SquadNavigation: Remote control not found");
                return false;
            }

            _statusSurfaces.Clear();

            var panels = this.GetLocalBlocks<IMyTextPanel>();
            foreach (var panel in panels)
            {
                if (panel.CustomName.IndexOf("[SN]", StringComparison.OrdinalIgnoreCase) >= 0)
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
            _controller = new SquadNavigationController(this, _customDataConnector, remoteControl, _statusSurfaces, pathfinderProgrammableBlock);
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
                    break;

                default:
                    Echo("Commands: start, stop");
                    break;
            }
        }

        private void HandlePathfinderEvents(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            var trimmed = argument.Trim().ToLowerInvariant();
            if (trimmed == "pf_start" || trimmed == "pf_stop" || trimmed == "pf_nopath")
            {
                var eventName = trimmed.Substring(3);
                _controller?.OnPathfinderEvent(eventName);
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

        private class SquadNavigationController : Context
        {
            private const double FORMATION_DISTANCE_DEFAULT = 20.0;

            private readonly Program _program;
            private readonly CustomDataConnector _customDataConnector;
            private readonly IMyRemoteControl _remoteControl;
            private readonly List<IMyTextSurface> _statusSurfaces = new List<IMyTextSurface>();
            private readonly SquadSection _section;
            private IMyProgrammableBlock _pathfinderProgrammableBlock;

            private bool _isFollowing;
            private double _deltaSeconds;

            public SquadNavigationController(Program program, CustomDataConnector customDataConnector, IMyRemoteControl remoteControl, List<IMyTextSurface> statusSurfaces, IMyProgrammableBlock pathfinderProgrammableBlock)
            {
                _program = program;
                _customDataConnector = customDataConnector;
                _remoteControl = remoteControl;
                if (statusSurfaces != null)
                {
                    _statusSurfaces.AddRange(statusSurfaces);
                }

                _pathfinderProgrammableBlock = pathfinderProgrammableBlock;
                _section = new SquadSection();
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
                if (string.IsNullOrWhiteSpace(_section.LeaderGpsName.Value))
                {
                    _program.Echo("SquadNavigation: LeaderGpsName not configured");
                    return;
                }

                if (_pathfinderProgrammableBlock == null)
                {
                    _program.Echo("SquadNavigation: Pathfinder programmable block not found (tag with [PF])");
                    return;
                }

                _isFollowing = true;
                NavigateToCurrentDestination();
            }

            public void StopFollowing()
            {
                _isFollowing = false;
                _pathfinderProgrammableBlock?.TryRun("stop");
                TransitionTo(new IdleState(this));
                UpdateStatus("Idle", "Following stopped");
            }

            private bool SendPathfinderCommand(string gpsName, Vector3D offset)
            {
                if (_pathfinderProgrammableBlock == null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(gpsName))
                {
                    return false;
                }

                var programmableBlockName = _program.Me.CustomName;
                string command;
                if (offset.LengthSquared() > 1e-6)
                {
                    var offsetString = $"{offset.X},{offset.Y},{offset.Z}";
                    command = $"start {gpsName}|{programmableBlockName}|{offsetString}|true";
                }
                else
                {
                    command = $"start {gpsName}|{programmableBlockName}||true";
                }
                
                if (!_pathfinderProgrammableBlock.TryRun(command))
                {
                    _program.Echo($"SquadNavigation: Failed to start pathfinding with '{command}'");
                    return false;
                }

                return true;
            }

            private void NavigateToCurrentDestination()
            {
                if (!_isFollowing)
                {
                    return;
                }

                var leaderGpsName = _section.LeaderGpsName.Value;
                var squadPosition = _section.SquadPosition.Value;
                var offset = CalculateFormationOffset();

                TransitionTo(new FollowingState(this, leaderGpsName, offset));
            }

            public double DeltaSeconds => _deltaSeconds;

            private Vector3D CalculateFormationOffset()
            {
                if (_section.SquadPosition.Value <= 0)
                {
                    return Vector3D.Zero;
                }

                var dist = _section.Distance.Value;
                var angle45Rad = Math.PI / 4.0;
                var cos45 = Math.Cos(angle45Rad);
                var sin45 = Math.Sin(angle45Rad);

                var offset = Vector3D.Zero;

                switch (_section.SquadPosition.Value)
                {
                    case 1:
                        offset = new Vector3D(dist, 0, dist);
                        break;
                    case 2:
                        offset = new Vector3D(-dist, 0, dist);
                        break;
                    case 3:
                        offset = new Vector3D(dist * 2.0, 0, dist * 2.0);
                        break;
                    case 4:
                        offset = new Vector3D(0, 0, dist * 2.0);
                        break;
                    case 5:
                        offset = new Vector3D(-dist * 2.0, 0, dist * 2.0);
                        break;
                    default:
                        var row = (_section.SquadPosition.Value - 1) / 3;
                        var col = (_section.SquadPosition.Value - 1) % 3;
                        var backDistance = dist * (row + 1);
                        var sideOffset = (col - 1) * dist;
                        offset = new Vector3D(sideOffset, 0, backDistance);
                        break;
                }

                return offset;
            }


            private void UpdateStatus(string state, params string[] details)
            {
                if (_statusSurfaces.Count == 0)
                {
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine("=== Squad Navigation ===");
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

            private abstract class SquadNavigationState : State<SquadNavigationController>
            {
                protected SquadNavigationState(SquadNavigationController context) : base(context)
                {
                }

                public virtual void OnPathfinderStarted() { }
                public virtual void OnPathfinderStopped() { }
                public virtual void OnPathfinderNoPath() { }
            }

            private class IdleState : SquadNavigationState
            {
                public IdleState(SquadNavigationController context) : base(context)
                {
                }

                public override void Enter()
                {
                    _context.UpdateStatus("Idle");
                    _context._program.Echo("Idle");
                }

                public override void Execute()
                {
                }
            }

            private class FollowingState : SquadNavigationState
            {
                private readonly string _leaderGpsName;
                private readonly Vector3D _offset;
                private bool _commandIssued;

                public FollowingState(SquadNavigationController context, string leaderGpsName, Vector3D offset) : base(context)
                {
                    _leaderGpsName = leaderGpsName;
                    _offset = offset;
                }

                public override void Enter()
                {
                    if (!_context.SendPathfinderCommand(_leaderGpsName, _offset))
                    {
                        _context.OnNavigationFailed("Failed to start pathfinding");
                        return;
                    }

                    _context.UpdateStatus("Following", $"Leader: {_leaderGpsName}", $"Position: {_context._section.SquadPosition.Value}");
                    _context._program.Echo("Following");

                    _commandIssued = true;
                }

                public override void Execute()
                {
                    if (!_context._isFollowing)
                    {
                        _context.TransitionTo(new IdleState(_context));
                        return;
                    }

                    // Check distance to target and enable/disable pathfinding accordingly
                    UpdatePathfindingBasedOnDistance();
                }

                private void UpdatePathfindingBasedOnDistance()
                {
                    var currentPosition = _context._remoteControl.GetPosition();
                    
                    // Try to get destination position from PathfinderDestination property
                    var destinationProperty = _context._remoteControl.GetProperty("PathfinderDestination") as ITerminalProperty<Vector3D?>;
                    if (destinationProperty == null)
                    {
                        return;
                    }

                    var destination = destinationProperty.GetValue(_context._remoteControl);
                    if (!destination.HasValue)
                    {
                        return;
                    }

                    var distance = Vector3D.Distance(currentPosition, destination.Value);
                    
                    // Get pathfinding enable property
                    var enablePathfindingProperty = _context._remoteControl.GetProperty("PathfinderEnablePathfinding") as ITerminalProperty<bool>;
                    if (enablePathfindingProperty == null)
                    {
                        return;
                    }

                    var currentPathfindingEnabled = enablePathfindingProperty.GetValue(_context._remoteControl);
                    var pathfindingDistanceThreshold = _context._section.PathfindingDistanceThreshold.Value;
                    var shouldEnablePathfinding = distance > pathfindingDistanceThreshold;

                    // Only update if the state needs to change
                    if (currentPathfindingEnabled != shouldEnablePathfinding)
                    {
                        enablePathfindingProperty.SetValue(_context._remoteControl, shouldEnablePathfinding);
                    }
                }

                public override void OnPathfinderStopped()
                {
                    if (_commandIssued)
                    {
                        _context.OnNavigationCompleted();
                    }
                }

                public override void OnPathfinderNoPath()
                {
                    _context.OnNavigationFailed("Path not found");
                }
            }

            public void OnPathfinderEvent(string eventName)
            {
                var state = CurrentState as SquadNavigationState;
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

            private void OnNavigationCompleted()
            {
            }

            private void OnNavigationFailed(string reason)
            {
                UpdateStatus("Idle", reason);
            }

            private class SquadSection : Section
            {
                private const double DISTANCE_DEFAULT = 20.0;
                private const double PATHFINDING_DISTANCE_THRESHOLD_DEFAULT = 100.0;

                public StringProperty LeaderGpsName { get; } = new StringProperty("LeaderGpsName", "", comment: "GPS name of the leader to follow");
                public IntProperty SquadPosition { get; } = new IntProperty("SquadPosition", 0, comment: "Position in formation (0=leader, 1=right behind, 2=left behind, 3=right side, 4=directly behind, etc.)");
                public DoubleProperty Distance { get; } = new DoubleProperty("Distance", DISTANCE_DEFAULT, comment: "Formation distance offset (meters)");
                public DoubleProperty PathfindingDistanceThreshold { get; } = new DoubleProperty("PathfindingDistanceThreshold", PATHFINDING_DISTANCE_THRESHOLD_DEFAULT, comment: "Distance threshold (meters) above which pathfinding is enabled");

                public SquadSection() : base("SquadNavigation")
                {
                    _properties.Add(LeaderGpsName);
                    _properties.Add(SquadPosition);
                    _properties.Add(Distance);
                    _properties.Add(PathfindingDistanceThreshold);
                }
            }
        }
    }
}

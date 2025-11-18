using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private CustomDataConnector _customDataConnector;
        private PathfindingNavigation _pathfindingNavigation;
        private IMyProgrammableBlock _linkedProgrammableBlock;
        private bool _pendingPathfinderStop;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;

            _customDataConnector = new CustomDataConnector();
            _customDataConnector.Initialize(this);

            var navigation = new Navigation();
            var alignment = new Alignment();
            _pathfindingNavigation = new PathfindingNavigation();

            string errorMessage;
            if (!navigation.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Navigation Error: {errorMessage}");
            }

            if (!alignment.Initialize(this, _customDataConnector, out errorMessage))
            {
                Echo($"Alignment Error: {errorMessage}");
            }

            if (!_pathfindingNavigation.Initialize(this, _customDataConnector, navigation, alignment, out errorMessage))
            {
                Echo($"TestPathfinding Error: {errorMessage}");
            }

            _pathfindingNavigation.SetStartCallback(OnPathfindingStart);
            _pathfindingNavigation.SetStopCallback(OnPathfindingStop);
            _pathfindingNavigation.SetNoPathCallback(OnPathfindingNoPath);

            _customDataConnector.Load();
        }

        public void Save()
        {
            Storage = _customDataConnector.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (_pendingPathfinderStop)
            {
                _pendingPathfinderStop = false;
                OnPathfindingStop();
            }

            if (!string.IsNullOrEmpty(argument))
            {
                HandleCommand(argument);
            }

            _pathfindingNavigation.Execute();
            _customDataConnector.Update();
        }

        private void HandleCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                Echo("Available commands: start, stop");
                return;
            }

            Echo($"Handling command: {argument}");

            var trimmedArgument = argument.Trim();
            var spaceIndex = trimmedArgument.IndexOf(' ');
            var command = spaceIndex == -1 ? trimmedArgument : trimmedArgument.Substring(0, spaceIndex);
            var payload = spaceIndex == -1 ? string.Empty : trimmedArgument.Substring(spaceIndex + 1).Trim();

            switch (command.ToLower())
            {
                case "start":
                    Vector3D? destination = null;
                    var gpsName = string.Empty;
                    var programmableBlockName = string.Empty;

                    if (!string.IsNullOrEmpty(payload))
                    {
                        var pipeIndex = payload.IndexOf('|');
                        if (pipeIndex >= 0)
                        {
                            var gpsFragment = payload.Substring(0, pipeIndex).TrimEnd();
                            programmableBlockName = payload.Substring(pipeIndex + 1).Trim();

                            Vector3D parsedDestination;
                            if (TryParseGps(gpsFragment, out parsedDestination))
                            {
                                destination = parsedDestination;
                            }
                            else
                            {
                                gpsName = gpsFragment;
                            }
                        }
                        else
                        {
                            Vector3D parsedDestination;
                            if (TryParseGps(payload, out parsedDestination))
                            {
                                destination = parsedDestination;
                            }
                            else
                            {
                                gpsName = payload;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(programmableBlockName))
                    {
                        LinkProgrammableBlock(programmableBlockName);
                    }

                    if (destination.HasValue)
                    {
                        Echo($"Destination: {destination.Value}");
                        var currentPosition = Me.GetPosition();
                        var distance = Vector3D.Distance(currentPosition, destination.Value);
                        var gridSize = Me?.CubeGrid != null ? Me.CubeGrid.WorldAABB.Size.Length() : 0;

                        if (gridSize > 0 && distance <= gridSize * 2)
                        {
                            Echo("Destination is on the same grid, stopping pathfinding navigation...");
                            _pathfindingNavigation.Stop();
                            _pendingPathfinderStop = true;
                        }
                        else
                        {
                            _pathfindingNavigation.Start(destination.Value);
                            Echo("Starting pathfinding navigation...");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(gpsName))
                    {
                        _pathfindingNavigation.Start(gpsName);
                        Echo($"Starting pathfinding navigation to GPS '{gpsName}'...");
                    }
                    else
                    {
                        _pathfindingNavigation.Start();
                        Echo("Starting pathfinding navigation...");
                    }

                    break;

                case "stop":
                    _pathfindingNavigation.Stop();
                    Echo("Stopping pathfinding navigation...");
                    break;

                default:
                    Echo("Available commands: start, stop");
                    break;
            }
        }

        private void OnPathfindingStart()
        {
            TriggerProgrammableBlock("pf_start");
        }

        private void OnPathfindingStop()
        {
            TriggerProgrammableBlock("pf_stop");
        }

        private void OnPathfindingNoPath()
        {
            TriggerProgrammableBlock("pf_nopath");
        }

        private void TriggerProgrammableBlock(string command)
        {
            if (_linkedProgrammableBlock == null)
            {
                return;
            }

            if (!_linkedProgrammableBlock.IsFunctional)
            {
                return;
            }

            _linkedProgrammableBlock.TryRun(command);
        }

        private bool TryParseGps(string gpsString, out Vector3D destination)
        {
            destination = Vector3D.Zero;

            var parts = gpsString.Split(':');
            if (parts.Length < 4)
            {
                Echo($"Less than 4 parts in GPS string: {gpsString}");
                return false;
            }

            var offset = parts[0].Equals("GPS", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (parts.Length - offset < 4)
            {
                Echo($"Less than 4 parts in GPS string: {gpsString}");
                return false;
            }

            double x;
            if (!double.TryParse(parts[offset + 1], out x))
            {
                Echo($"Failed to parse X coordinate: {parts[offset + 1]}");
                return false;
            }

            double y;
            if (!double.TryParse(parts[offset + 2], out y))
            {
                Echo($"Failed to parse Y coordinate: {parts[offset + 2]}");
                return false;
            }

            double z;
            if (!double.TryParse(parts[offset + 3], out z))
            {
                Echo($"Failed to parse Z coordinate: {parts[offset + 3]}");
                return false;
            }

            destination = new Vector3D(x, y, z);
            return true;
        }

        private void LinkProgrammableBlock(string programmableBlockName)
        {
            var programmableBlock = FindProgrammableBlock(programmableBlockName);
            if (programmableBlock == null)
            {
                Echo($"Programmable block '{programmableBlockName}' not found");
                return;
            }

            _linkedProgrammableBlock = programmableBlock;
            Echo($"Linked programmable block '{_linkedProgrammableBlock.CustomName}'");
        }

        private IMyProgrammableBlock FindProgrammableBlock(string programmableBlockName)
        {
            if (string.IsNullOrEmpty(programmableBlockName))
            {
                return null;
            }

            var blocks = new List<IMyProgrammableBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, block =>
                block.IsSameConstructAs(Me) &&
                block.CustomName.IndexOf(programmableBlockName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (blocks.Count == 0)
            {
                return null;
            }

            return blocks[0];
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
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
                    Vector3D? offset = null;
                    bool track = false;

                    if (!string.IsNullOrEmpty(payload))
                    {
                        var parts = payload.Split('|');
                        if (parts.Length >= 1)
                        {
                            var gpsFragment = parts[0].Trim();
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
                        if (parts.Length >= 2)
                        {
                            programmableBlockName = parts[1].Trim();
                        }
                        if (parts.Length >= 3)
                        {
                            var offsetString = parts[2].Trim();
                            Vector3D parsedOffset;
                            if (TryParseVector3D(offsetString, out parsedOffset))
                            {
                                offset = parsedOffset;
                            }
                        }
                        if (parts.Length >= 4)
                        {
                            var trackString = parts[3].Trim();
                            bool.TryParse(trackString, out track);
                        }
                    }

                    if (!string.IsNullOrEmpty(programmableBlockName))
                    {
                        LinkProgrammableBlock(programmableBlockName);
                    }

                    if (destination.HasValue)
                    {
                        var finalDestination = destination.Value;
                        if (offset.HasValue)
                        {
                            finalDestination += offset.Value;
                        }
                        Echo($"Destination: {finalDestination}");
                        var currentPosition = Me.GetPosition();
                        var distance = Vector3D.Distance(currentPosition, finalDestination);
                        var gridSize = Me?.CubeGrid != null ? Me.CubeGrid.WorldAABB.Size.Length() : 0;

                        if (gridSize > 0 && distance <= gridSize * 2)
                        {
                            Echo("Destination is on the same grid, stopping pathfinding navigation...");
                            _pathfindingNavigation.Stop();
                            _pendingPathfinderStop = true;
                        }
                        else
                        {
                            _pathfindingNavigation.Start(finalDestination);
                            Echo("Starting pathfinding navigation...");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(gpsName))
                    {
                        if (offset.HasValue)
                        {
                            _pathfindingNavigation.Start(gpsName, offset.Value, track);
                        }
                        else
                        {
                            _pathfindingNavigation.Start(gpsName, Vector3D.Zero, track);
                        }
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

        private bool TryParseVector3D(string vectorString, out Vector3D vector)
        {
            vector = Vector3D.Zero;
            var parts = vectorString.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }

            double x, y, z;
            if (double.TryParse(parts[0].Trim(), out x) &&
                double.TryParse(parts[1].Trim(), out y) &&
                double.TryParse(parts[2].Trim(), out z))
            {
                vector = new Vector3D(x, y, z);
                return true;
            }

            return false;
        }

        private bool TryParseGps(string gpsString, out Vector3D destination)
        {
            destination = Vector3D.Zero;

            var parts = gpsString.Split(':');
            if (parts.Length < 4)
            {
                return false;
            }

            var offset = parts[0].Equals("GPS", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (parts.Length - offset < 4)
            {
                return false;
            }

            double x;
            if (!double.TryParse(parts[offset + 1], out x))
            {
                return false;
            }

            double y;
            if (!double.TryParse(parts[offset + 2], out y))
            {
                return false;
            }

            double z;
            if (!double.TryParse(parts[offset + 3], out z))
            {
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

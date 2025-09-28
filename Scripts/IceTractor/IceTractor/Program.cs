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
using System.Xml;
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
        #region Constants
        private const double FORWARD_PROPULSION = 0.25;
        private const double BACKWARD_PROPULSION = 0.25;
        private const double TURN_PROPULSION = 0.2;
        private const string STATUS_LIGHTS_GROUP_NAME = "Status Lights";
        private const string FWD_LIGHTS_GROUP_NAME = "Forward Lights";
        private const string REAR_LIGHTS_GROUP_NAME = "Rear Lights";
        private const string TURN_SIGNAL_RIGHT_LIGHT_NAME = "TL R";
        private const string TURN_SIGNAL_LEFT_LIGHT_NAME = "TL L";
        private const string BROADCAST_TAG = "DroneControl";
        private const double APPROACH_OFFSET = 20.0; // meters above docking connector
        private const double PISTON_EXTENSION_SPEED = 0.02;
        private const double PISTON_RETRACTION_SPEED = 1;
        #endregion

        #region Fields
        private RoverNavigation _roverNavigation;
        // Sensor fields - corner-based (one sensor per corner)
        private IMySensorBlock _forwardRightSensor;
        private IMySensorBlock _forwardLeftSensor;
        private IMySensorBlock _rearRightSensor;
        private IMySensorBlock _rearLeftSensor;
        private List<IMyLightingBlock> _statusLights;
        private List<IMyLightingBlock> _fwdLights;
        private List<IMyLightingBlock> _rearLights;
        private IMyLightingBlock _turnSignalRightLight;
        private IMyLightingBlock _turnSignalLeftLight;
        private IMyRemoteControl _remoteControl;
        private List<IMyCargoContainer> _cargoContainers;
        private List<IMyPistonBase> _pistonsUp;
        private List<IMyPistonBase> _pistonsDown;
        private List<IMyShipDrill> _drills;
        private List<IMyTextPanel> _lcdPanels;
        private IceTractorContext _context;
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;


            _roverNavigation = new RoverNavigation();
            _statusLights = this.GetLocalBlocksInGroup<IMyLightingBlock>(STATUS_LIGHTS_GROUP_NAME);
            _fwdLights = this.GetLocalBlocksInGroup<IMyLightingBlock>(FWD_LIGHTS_GROUP_NAME);
            _rearLights = this.GetLocalBlocksInGroup<IMyLightingBlock>(REAR_LIGHTS_GROUP_NAME);
            _turnSignalRightLight = this.GetLocalBlock<IMyLightingBlock>(TURN_SIGNAL_RIGHT_LIGHT_NAME);
            _turnSignalLeftLight = this.GetLocalBlock<IMyLightingBlock>(TURN_SIGNAL_LEFT_LIGHT_NAME);
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            _cargoContainers = this.GetLocalBlocks<IMyCargoContainer>();
            _drills = this.GetLocalBlocks<IMyShipDrill>();
            _lcdPanels = this.GetLocalBlocks<IMyTextPanel>();
            
            // Initialize piston lists
            _pistonsUp = new List<IMyPistonBase>();
            _pistonsDown = new List<IMyPistonBase>();
            
            InitializeSensors();
            InitializePistons();
            _context = new IceTractorContext(this);
                        
            string errorMessage;
            if (!_roverNavigation.Initialize(this, out errorMessage))
            {
                Echo($"Rover Navigation Error: {errorMessage}");
            }
            else
            {
                Echo("Rover Navigation initialized successfully!");
            }

            if (_forwardRightSensor == null && _forwardLeftSensor == null)
            {
                Echo("Warning: No forward sensors found!");
            }

            if (_statusLights.Count == 0)
            {
                Echo("Warning: No status lights found in 'StatusLights' group!");
            }
            else
            {
                Echo($"Found {_statusLights.Count} status lights");
            }

            if (_fwdLights.Count == 0)
            {
                Echo("Warning: No forward lights found in 'FwdLights' group!");
            }
            else
            {
                Echo($"Found {_fwdLights.Count} forward lights");
            }

            if (_rearLights.Count == 0)
            {
                Echo("Warning: No rear lights found in 'RearLights' group!");
            }
            else
            {
                Echo($"Found {_rearLights.Count} rear lights");
            }

            if (_turnSignalRightLight == null)
            {
                Echo("Warning: No right turn signal light found with name 'TL Right'!");
            }
            else
            {
                Echo("Found right turn signal light");
            }

            if (_turnSignalLeftLight == null)
            {
                Echo("Warning: No left turn signal light found with name 'TL Left'!");
            }
            else
            {
                Echo("Found left turn signal light");
            }
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // Handle IGC messages
            if ((updateSource & UpdateType.IGC) > 0)
            {
                _context.HandleIGCMessages();
            }
            else if (!string.IsNullOrEmpty(argument))
            {
                // Regular command
                HandleCommand(argument);
            }

            _context.Execute();
            UpdateLcdPanels();
        }

        private void HandleCommand(string argument)
        {
            var args = argument.Split(' ');
            var command = args[0].ToLower();

            switch (command)
            {
                case "start":
                    _context.Start();
                    Echo("Starting Ice Tractor...");
                    break;
                case "stop":
                    _context.Stop();
                    Echo("Stopping Ice Tractor...");
                    break;
                case "cargofull":
                    _context.BroadcastCargoFull();
                    Echo("Broadcasting Cargo Full...");
                    break;
                default:
                    Echo($"Unknown command: {command}");
                    break;
            }
        }

        public void SetWorkLightSettings(Color color, float blinkInterval = 0, float blinkLength = 0)
        {
            foreach (var light in _statusLights)
            {
                light.Color = color;
                light.BlinkIntervalSeconds = blinkInterval;
                light.BlinkLength = blinkLength;
                light.Enabled = true;
            }
        }

        public void SetTurnSignalRight(bool enabled)
        {
            if (_turnSignalRightLight != null)
            {
                _turnSignalRightLight.Enabled = enabled;
                if (enabled)
                {
                    _turnSignalRightLight.Color = Color.Yellow;
                    _turnSignalRightLight.BlinkIntervalSeconds = 1.0f;
                    _turnSignalRightLight.BlinkLength = 0.5f;
                }
            }
        }

        public void SetTurnSignalLeft(bool enabled)
        {
            if (_turnSignalLeftLight != null)
            {
                _turnSignalLeftLight.Enabled = enabled;
                if (enabled)
                {
                    _turnSignalLeftLight.Color = Color.Yellow;
                    _turnSignalLeftLight.BlinkIntervalSeconds = 1.0f;
                    _turnSignalLeftLight.BlinkLength = 0.5f;
                }
            }
        }

        public void SetRearLights(bool enabled, Color color, float blinkInterval = 0, float blinkLength = 0)
        {
            foreach (var light in _rearLights)
            {
                light.Enabled = enabled;
                if (enabled)
                {
                    light.Color = color;
                    if (blinkInterval > 0)
                    {
                        light.BlinkIntervalSeconds = blinkInterval;
                        light.BlinkLength = blinkLength;
                    }
                    else
                    {
                        light.BlinkIntervalSeconds = 0;
                    }
                }
            }
        }

        private double GetYaw()
        {
            var fwd = _remoteControl.WorldMatrix.Forward;
            return Math.Atan2(fwd.X, fwd.Y);
        }

        
        public class IceTractorContext : Context
        {
            private enum MoveDirection
            {
                Forward,
                ForwardRight,
                ForwardLeft,
                Backward,
                BackwardRight,
                BackwardLeft,
                Stop
            }

            public Program program;
            private IMyUnicastListener _unicastListener;
            private MoveDirection _backingRecoverDirection = MoveDirection.ForwardRight;

            public IceTractorContext(Program program)
            {
                this.program = program;
                _unicastListener = program.IGC.UnicastListener;
                _unicastListener.SetMessageCallback(Program.BROADCAST_TAG);
                TransitionTo(new IdleState(this));
            }

            #region IGC Messages

            public void HandleIGCMessages()
            {
                if (_unicastListener != null && _unicastListener.HasPendingMessage)
                {
                    while (_unicastListener.HasPendingMessage)
                    {
                        var igcMessage = _unicastListener.AcceptMessage();
                        if (igcMessage.Tag == Program.BROADCAST_TAG && igcMessage.Data is string)
                        {
                            var message = igcMessage.Data.ToString();
                            program.Echo($"IGC Message: {message}");
                            
                            if (message == "acknowledged")
                            {
                                // If we're in broadcasting state, acknowledge the request
                                if (CurrentState is BroadcastCargoFullState)
                                {
                                    program.Echo("Request acknowledged, stopping broadcast");
                                    TransitionTo(new IdleState(this));
                                }
                            }
                            else if (message == "requestdockcoord")
                            {
                                HandleDockingCoordinateRequest(igcMessage.Source);
                            }
                            else if (message == "cargostatus")
                            {
                                HandleCargoStatusRequest(igcMessage.Source);
                            }
                            else if (message == "requestapproach")
                            {
                                HandleApproachPositionRequest(igcMessage.Source);
                            }
                            else if (message == "clear")
                            {
                                HandleClearMessage();
                            }
                        }
                    }
                }
            }

            public void HandleDockingCoordinateRequest(long requesterEntityId)
            {
                program.Echo($"Docking coordinate request from {requesterEntityId}");
                
                // Find docking connector (assuming it's named "Docking Connector")
                var dockingConnector = program.GridTerminalSystem.GetBlockWithName("Docking Connector") as IMyShipConnector;
                if (dockingConnector != null)
                {
                    var connectorMatrix = dockingConnector.WorldMatrix;
                    
                    // Send matrix data as pipe-separated values
                    var matrixMessage = $"dockcoord|{connectorMatrix.M11}|{connectorMatrix.M12}|{connectorMatrix.M13}|{connectorMatrix.M14}|" +
                                      $"{connectorMatrix.M21}|{connectorMatrix.M22}|{connectorMatrix.M23}|{connectorMatrix.M24}|" +
                                      $"{connectorMatrix.M31}|{connectorMatrix.M32}|{connectorMatrix.M33}|{connectorMatrix.M34}|" +
                                      $"{connectorMatrix.M41}|{connectorMatrix.M42}|{connectorMatrix.M43}|{connectorMatrix.M44}";
                    
                    program.IGC.SendUnicastMessage(requesterEntityId, Program.BROADCAST_TAG, matrixMessage);
                    program.Echo($"Sent docking coordinates: {connectorMatrix.Translation}");
                }
                else
                {
                    program.Echo("ERROR: No 'Docking Connector' found!");
                }
            }

            public void HandleCargoStatusRequest(long requesterEntityId)
            {
                program.Echo($"Cargo status request from {requesterEntityId}");
                
                // Check if cargo containers are empty
                if (program.AreCargoContainersEmpty())
                {
                    program.IGC.SendUnicastMessage(requesterEntityId, Program.BROADCAST_TAG, "cargostatus|empty");
                    program.Echo("Sent cargo status: empty");
                }
                else
                {
                    program.IGC.SendUnicastMessage(requesterEntityId, Program.BROADCAST_TAG, "cargostatus|full");
                    program.Echo("Sent cargo status: full");
                }
            }

            public void HandleApproachPositionRequest(long requesterEntityId)
            {
                program.Echo($"Approach position request from {requesterEntityId}");
                
                // Find docking connector (assuming it's named "Docking Connector")
                var dockingConnector = program.GridTerminalSystem.GetBlockWithName("Docking Connector") as IMyShipConnector;
                if (dockingConnector != null)
                {
                    var connectorPosition = dockingConnector.GetPosition();
                    var connectorForward = dockingConnector.WorldMatrix.Forward;
                    var approachPosition = connectorPosition + (connectorForward * Program.APPROACH_OFFSET);
                    
                    // Send position data as pipe-separated values
                    var positionMessage = $"approachpos|{approachPosition.X}|{approachPosition.Y}|{approachPosition.Z}";
                    
                    program.IGC.SendUnicastMessage(requesterEntityId, Program.BROADCAST_TAG, positionMessage);
                    program.Echo($"Sent approach position: {approachPosition}");
                }
                else
                {
                    program.Echo("ERROR: No 'Docking Connector' found!");
                }
            }

            public void HandleClearMessage()
            {
                program.Echo("Received 'clear' message - transitioning to MovingState");
                TransitionTo(new MovingState(this));
            }
            #endregion
            public void Start()
            {
                if (program._forwardRightSensor == null)
                {
                    program.Echo("Error: Forward-right sensor not found! Cannot start.");
                    return;
                }
                if (program._forwardLeftSensor == null)
                {
                    program.Echo("Error: Forward-left sensor not found! Cannot start.");
                    return;
                }
                if (program._rearRightSensor == null)
                {
                    program.Echo("Error: Rear-right sensor not found! Cannot start.");
                    return;
                }
                if (program._rearLeftSensor == null)
                {
                    program.Echo("Error: Rear-left sensor not found! Cannot start.");
                    return;
                }
                
                program.Echo("All sensors found - starting navigation");
                TransitionTo(new MovingState(this));
            }

            public void Stop()
            {
                TransitionTo(new IdleState(this));
            }


            public void BroadcastCargoFull()
            {
                TransitionTo(new BroadcastCargoFullState(this));
            }

            private MoveDirection CalculateMoveDirection()
            {
                // Check corner sensors
                var forwardClear = !program._forwardRightSensor.IsActive && !program._forwardLeftSensor.IsActive;
                var forwardRightClear = !program._forwardRightSensor.IsActive;
                var forwardLeftClear = !program._forwardLeftSensor.IsActive;
                var rearClear = !program._rearRightSensor.IsActive && !program._rearLeftSensor.IsActive;
                var rearRightClear = !program._rearRightSensor.IsActive;
                var rearLeftClear = !program._rearLeftSensor.IsActive;

                // Priority 1: Forward movement
                if (forwardClear)
                {
                    return MoveDirection.Forward;
                }
                // Priority 2: Diagonal forward movement
                if (forwardLeftClear)
                {
                    return MoveDirection.ForwardLeft;
                }
                if (forwardRightClear)
                {
                    return MoveDirection.ForwardRight;
                }
                // Priority 3: Backward movement
                if (rearClear)
                {
                    return MoveDirection.Backward;
                }

                // Priority 4: Diagonal backward movement
                if (rearLeftClear)
                {
                    return MoveDirection.BackwardLeft;
                }
                if (rearRightClear)
                {
                    return MoveDirection.BackwardRight;
                }

                // If everything is blocked, stop
                return MoveDirection.Stop;
            }


            private class IdleState : State<IceTractorContext>
            {
                public IdleState(IceTractorContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program._roverNavigation.Stop();
                    _context.program.SetWorkLightSettings(Color.Green, 0, 0); // Solid green
                    _context.program.RaiseDrills();
                    _context.program.TurnOffDrills();
                    _context.program.Echo("Idle State");
                }

                public override void Execute()
                {
                }
            }

            private class MovingState : State<IceTractorContext>
            {
                private Vector3D _startPosition;
                private const double MINING_DISTANCE = 10.0; // meters

                public MovingState(IceTractorContext context) : base(context)
                {
                }

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Yellow, 1.0f, 0.25f); // Flashing yellow

                    // Turn off turn signals and rear lights when moving forward
                    _context.program.SetTurnSignalRight(false);
                    _context.program.SetTurnSignalLeft(false);
                    _context.program.SetRearLights(false, Color.Red, 0, 0);

                    // Release handbrake
                    _context.program.SetHandbrake(false);

                    // Cache starting position
                    _startPosition = _context.program._remoteControl.GetPosition();
                    _context.program.Echo("MovingState started - position cached");
                }

                public override void Execute()
                {
                    // Check if cargo is full
                    if (_context.program.AreCargoContainersFull())
                    {
                        _context.program.Echo("Cargo full - transitioning to BroadcastCargoFullState");
                        _context.TransitionTo(new BroadcastCargoFullState(_context));
                        return;
                    }

                    // Check if we've moved far enough to start mining
                    var currentPosition = _context.program._remoteControl.GetPosition();
                    var distanceMoved = Vector3D.Distance(currentPosition, _startPosition);
                    if (distanceMoved >= MINING_DISTANCE)
                    {
                        _context.program.Echo($"Moved {distanceMoved:F1}m - transitioning to MiningState");
                        _context.TransitionTo(new MiningState(_context));
                        return;
                    }

                    var moveDirection = _context.CalculateMoveDirection();
                    if (moveDirection == MoveDirection.Forward)
                    {
                        _context.program._roverNavigation.NavigateForward(Program.FORWARD_PROPULSION);
                        return;
                    }

                    if (moveDirection == MoveDirection.ForwardRight)
                    {
                        _context.program.Echo("Forward Right - transitioning to TurningState");
                        _context.TransitionTo(new TurningState(_context, MoveDirection.ForwardRight));
                        return;
                    }

                    if (moveDirection == MoveDirection.ForwardLeft)
                    {
                        _context.program.Echo("Forward Left - transitioning to TurningState");
                        _context.TransitionTo(new TurningState(_context, MoveDirection.ForwardLeft));
                        return;
                    }

                    if (moveDirection == MoveDirection.Backward)
                    {
                        _context.program.Echo("Backward - transitioning to BackingUpState");
                        _context.TransitionTo(new BackingUpState(_context));
                        return;
                    }

                    if (moveDirection == MoveDirection.BackwardRight)
                    {
                        _context.program.Echo("Backward Right - transitioning to TurningState");
                        _context.TransitionTo(new TurningState(_context, MoveDirection.BackwardRight));
                        return;
                    }

                    if (moveDirection == MoveDirection.BackwardLeft)
                    {
                        _context.program.Echo("Backward Left - transitioning to TurningState");
                        _context.TransitionTo(new TurningState(_context, MoveDirection.BackwardLeft));
                        return;
                    }

                    if (moveDirection == MoveDirection.Stop)
                    {
                        _context.program.Echo("All paths blocked - transitioning to WaitingForClearanceState");
                        _context.TransitionTo(new WaitingForClearanceState(_context));
                        return;
                    }
                }
            }


            private class TurningState : State<IceTractorContext>
            {
                private MoveDirection _turnDirection;
                private double _startYaw;
                private const double TURN_ANGLE = Math.PI / 2; // 90 degrees

                public TurningState(IceTractorContext context, MoveDirection turnDirection) : base(context)
                {
                    _turnDirection = turnDirection;
                }

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Orange, 1.0f, 0.25f); // Flashing orange
                    
                    // Set turn signal lights
                    if (_turnDirection == MoveDirection.ForwardRight || _turnDirection == MoveDirection.BackwardRight)
                    {
                        _context.program.SetTurnSignalRight(true);
                    }
                    else if (_turnDirection == MoveDirection.ForwardLeft || _turnDirection == MoveDirection.BackwardLeft)
                    {
                        _context.program.SetTurnSignalLeft(true);
                    }
                    
                    _startYaw = _context.program.GetYaw();
                    
                    _context.program.Echo($"Starting {_turnDirection} turn");
                }

                public override void Execute()
                {
                    IMySensorBlock dirSensor = null;
                    // Apply turning force
                    if (_turnDirection == MoveDirection.ForwardRight)
                    {
                        dirSensor = _context.program._forwardRightSensor;
                        _context.program._roverNavigation.NavigateForwardRight(Program.TURN_PROPULSION);
                    }
                    else if (_turnDirection == MoveDirection.ForwardLeft)
                    {
                        dirSensor = _context.program._forwardLeftSensor;
                        _context.program._roverNavigation.NavigateForwardLeft(Program.TURN_PROPULSION);
                    }
                    else if (_turnDirection == MoveDirection.BackwardRight)
                    {
                        dirSensor = _context.program._rearRightSensor;
                        _context.program._roverNavigation.NavigateBackwardRight(Program.TURN_PROPULSION);
                    }
                    else if (_turnDirection == MoveDirection.BackwardLeft)
                    {
                        dirSensor = _context.program._rearLeftSensor;
                        _context.program._roverNavigation.NavigateBackwardLeft(Program.TURN_PROPULSION);
                    }

                    // Check if we've turned enough
                    double curYaw = _context.program.GetYaw();

                    double yawDifference = Math.Abs(curYaw - _startYaw);
                    if (yawDifference > Math.PI)
                    {
                        yawDifference = 2 * Math.PI - yawDifference; // Handle wrap-around
                    }

                    if (dirSensor.IsActive || yawDifference >= TURN_ANGLE)
                    {
                        _context.TransitionTo(new MovingState(_context));
                        return;
                    }
                }
            }

            private class MiningState : State<IceTractorContext>
            {
                private bool _miningStarted = false;
                private bool _raisingDrills = false;

                public MiningState(IceTractorContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Orange, 1.0f, 0.25f); // Flashing orange
                    _context.program._roverNavigation.Stop();
                    
                    // Turn on handbrake on all wheels
                    _context.program.SetHandbrake(true);
                    
                    // Turn on drills
                    _context.program.TurnOnDrills();
                    
                    // Lower drills for mining
                    _context.program.LowerDrills();
                    
                    _miningStarted = true;
                    _context.program.Echo("Mining started - drills on, pistons moving");
                }

                public override void Execute()
                {
                    // If we're raising drills, wait until they're raised
                    if (_raisingDrills)
                    {
                        if (_context.program.IsDrillRaised())
                        {
                            _context.program.Echo("Drills raised - mining complete");
                            _context.program.TurnOffDrills();
                            _context.TransitionTo(new BroadcastCargoFullState(_context));
                            return;
                        }
                        _context.program.Echo("Raising drills...");
                        return;
                    }

                    // Check if cargo is full
                    if (_context.program.AreCargoContainersFull())
                    {
                        _context.program.Echo("Cargo full - raising drills and stopping mining");
                        _context.program.RaiseDrills();
                        _raisingDrills = true;
                        return;
                    }

                    // Check if drills are lowered
                    if (_context.program.IsDrillLowered())
                    {
                        _context.program.Echo("Drills lowered - raising drills and mining complete");
                        _context.program.RaiseDrills();
                        _raisingDrills = true;
                        return;
                    }

                    // Show progress
                    _context.program.Echo($"Mining in progress - Drills: {(_context.program.IsDrillLowered() ? "Lowered" : "Lowering")}");
                }

            }

            private class WaitingForClearanceState : State<IceTractorContext>
            {
                private double _waitStartTime;
                private const double WAIT_DURATION = 5.0; // seconds

                public WaitingForClearanceState(IceTractorContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Red, 1.0f, 0.5f); // Flashing red
                    _context.program._roverNavigation.Stop();
                    _waitStartTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    _context.program.Echo("Waiting for clearance - all paths blocked");
                }

                public override void Execute()
                {
                    var currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    var waitTime = currentTime - _waitStartTime;

                    if (waitTime >= WAIT_DURATION)
                    {
                        _context.program.Echo("Wait complete - attempting to move again");
                        _context.TransitionTo(new MovingState(_context));
                        return;
                    }
                }
            }

            private class BackingUpState : State<IceTractorContext>
            {
                private Vector3D _startPosition;
                private MoveDirection _backingRecoverDirection;
                private bool _traveledBackupDistance = false;
                private const double BACKUP_DISTANCE = 10.0; // meters

                public BackingUpState(IceTractorContext context) : base(context) {}

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Red, 1.0f, 0.25f); // Flashing red
                    
                    // Turn on rear lights (blinking red)
                    _context.program.SetRearLights(true, Color.Red, 1.0f, 0.25f);
                    
                    _backingRecoverDirection = _context._backingRecoverDirection;
                    _backingRecoverDirection = _backingRecoverDirection == MoveDirection.ForwardRight ? MoveDirection.ForwardLeft : MoveDirection.ForwardRight;
                    _startPosition = _context.program._remoteControl.GetPosition();
                }

                public override void Execute()
                {
                    if (!_traveledBackupDistance)
                    {
                        var currentPosition = _context.program._remoteControl.GetPosition();
                        var distanceBacked = Vector3D.Distance(currentPosition, _startPosition);
                        if (distanceBacked >= BACKUP_DISTANCE)
                        {
                            _traveledBackupDistance = true;
                            return;
                        }
                        bool rearClear = !_context.program._rearRightSensor.IsActive && !_context.program._rearLeftSensor.IsActive;
                        if (rearClear)
                        {
                            _context.program._roverNavigation.NavigateBackward(Program.BACKWARD_PROPULSION);
                            return;
                        }
                        var rearRightClear = !_context.program._rearRightSensor.IsActive;
                        if (rearRightClear)
                        {
                            _context.program._roverNavigation.NavigateBackwardRight(Program.BACKWARD_PROPULSION);
                            return;
                        }   
                        var rearLeftClear = !_context.program._rearLeftSensor.IsActive;
                        if (rearLeftClear)
                        {
                            _context.program._roverNavigation.NavigateBackwardLeft(Program.BACKWARD_PROPULSION);
                            return;
                        }
                    }
                    // try to make a turn to the side so we dont end up in the same spot we were backing up from
                    var forwardRightClear = !_context.program._forwardRightSensor.IsActive;
                    if (forwardRightClear)
                    {
                        _context.TransitionTo(new TurningState(_context, MoveDirection.ForwardRight));
                        return;
                    }
                    var forwardLeftClear = !_context.program._forwardLeftSensor.IsActive;
                    if (forwardLeftClear)
                    {
                        _context.TransitionTo(new TurningState(_context, MoveDirection.ForwardLeft));
                        return;
                    }
                    // cant turn, so just keep backing up
                    _context.TransitionTo(new BackingUpState(_context));
                }
            }

            private class BroadcastCargoFullState : State<IceTractorContext>
            {
                private double _lastBroadcastTime;

                public BroadcastCargoFullState(IceTractorContext context) : base(context) { }

                public override void Enter()
                {
                    _context.program.SetWorkLightSettings(Color.Red, 1.0f, 0.5f); // Flashing red
                    _context.program.Echo("Broadcasting Cargo Full");
                    _lastBroadcastTime = 0;
                    
                }


                public override void Execute()
                {
                    var currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    
                    
                    // Broadcast every 5 seconds
                    if (currentTime - _lastBroadcastTime >= 5.0)
                    {
                        if (_context.program._remoteControl != null)
                        {
                            var position = _context.program._remoteControl.GetPosition();
                            var entityId = _context.program.Me.EntityId;
                            var message = $"cargofull|{entityId}";
                            
                            _context.program.IGC.SendBroadcastMessage(BROADCAST_TAG, message);
                            _context.program.Echo($"Broadcasting: {message}");
                            _lastBroadcastTime = currentTime;
                        }
                    }
                }
            }
        }

        public bool AreCargoContainersEmpty()
        {
            if (_cargoContainers.Count == 0) return true;

            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                if (inventory != null)
                {
                    var currentVolume = (double)inventory.CurrentVolume;
                    if (currentVolume > 0.1) // Not empty if more than 0.1L
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void InitializeSensors()
        {
            var allSensors = this.GetLocalBlocks<IMySensorBlock>();
            Echo($"Initializing sensors - found {allSensors.Count} total sensors");

            if (_remoteControl == null)
            {
                Echo("ERROR: Remote control not found for sensor initialization!");
                return;
            }

            // Get grid's center of mass and remote control orientation
            var gridCenterOfMass = _remoteControl.CenterOfMass;
            var remoteMatrix = _remoteControl.WorldMatrix;
            var remoteForward = remoteMatrix.Forward;
            var remoteRight = remoteMatrix.Right;

            foreach (var sensor in allSensors)
            {
                var sensorPosition = sensor.GetPosition();
                var relativePosition = sensorPosition - gridCenterOfMass;

                // Calculate dot products to determine position relative to remote control orientation
                var forwardDot = Vector3D.Dot(relativePosition, remoteForward);
                var rightDot = Vector3D.Dot(relativePosition, remoteRight);

                bool isForward = forwardDot > 0;
                bool isRight = rightDot > 0;

                Echo($"Sensor {sensor.CustomName}: forwardDot={forwardDot:F2}, rightDot={rightDot:F2}");

                if (isForward && isRight) // Forward-Right
                {
                    _forwardRightSensor = sensor;
                    sensor.CustomName = "Forward Right Sensor";
                    Echo($"Found forward-right sensor: {sensor.CustomName}");
                }
                else if (isForward && !isRight) // Forward-Left
                {
                    _forwardLeftSensor = sensor;
                    sensor.CustomName = "Forward Left Sensor";
                    Echo($"Found forward-left sensor: {sensor.CustomName}");
                }
                else if (!isForward && isRight) // Rear-Right
                {
                    _rearRightSensor = sensor;
                    sensor.CustomName = "Rear Right Sensor";
                    Echo($"Found rear-right sensor: {sensor.CustomName}");
                }
                else if (!isForward && !isRight) // Rear-Left
                {
                    _rearLeftSensor = sensor;
                    sensor.CustomName = "Rear Left Sensor";
                    Echo($"Found rear-left sensor: {sensor.CustomName}");
                }
            }

            // Report findings
            Echo($"Sensor categorization complete:");
            Echo($"  Forward-Right: {(_forwardRightSensor != null ? "Found" : "Missing")}");
            Echo($"  Forward-Left: {(_forwardLeftSensor != null ? "Found" : "Missing")}");
            Echo($"  Rear-Right: {(_rearRightSensor != null ? "Found" : "Missing")}");
            Echo($"  Rear-Left: {(_rearLeftSensor != null ? "Found" : "Missing")}");

            if (_forwardRightSensor == null) Echo("WARNING: No forward-right sensor found!");
            if (_forwardLeftSensor == null) Echo("WARNING: No forward-left sensor found!");
            if (_rearRightSensor == null) Echo("WARNING: No rear-right sensor found!");
            if (_rearLeftSensor == null) Echo("WARNING: No rear-left sensor found!");
        }

        private void InitializePistons()
        {
            var allPistons = this.GetLocalBlocks<IMyPistonBase>();
            Echo($"Initializing pistons - found {allPistons.Count} total pistons");

            if (_remoteControl == null)
            {
                Echo("ERROR: Remote control not found for piston initialization!");
                return;
            }

            // Get remote control's up vector as reference
            var remoteMatrix = _remoteControl.WorldMatrix;
            var remoteUp = remoteMatrix.Up;

            foreach (var piston in allPistons)
            {
                var pistonMatrix = piston.WorldMatrix;
                var pistonUp = pistonMatrix.Up;

                // Calculate dot product to determine if piston points same direction as remote control
                var upDot = Vector3D.Dot(pistonUp, remoteUp);

                Echo($"Piston {piston.CustomName}: upDot={upDot:F2}");

                if (upDot > 0.5) // Piston pointing up (same direction as remote control)
                {
                    _pistonsUp.Add(piston);
                    piston.CustomName = $"Piston Up {_pistonsUp.Count}";
                    Echo($"Found up piston: {piston.CustomName}");
                }
                else if (upDot < -0.5) // Piston pointing down (opposite direction)
                {
                    _pistonsDown.Add(piston);
                    piston.CustomName = $"Piston Down {_pistonsDown.Count}";
                    Echo($"Found down piston: {piston.CustomName}");
                }
            }

            // Report findings
            Echo($"Piston categorization complete:");
            Echo($"  Up pistons: {_pistonsUp.Count}");
            Echo($"  Down pistons: {_pistonsDown.Count}");

            if (_pistonsUp.Count == 0) Echo("WARNING: No up pistons found!");
            if (_pistonsDown.Count == 0) Echo("WARNING: No down pistons found!");
        }

        public bool AreCargoContainersFull()
        {
            if (_cargoContainers.Count == 0) return false;

            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                if (inventory != null)
                {
                    var maxVolume = (double)inventory.MaxVolume;
                    var currentVolume = (double)inventory.CurrentVolume;
                    if (currentVolume < maxVolume * 0.95) // 95% full threshold
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public double GetCargoFullnessPercentage()
        {
            if (_cargoContainers.Count == 0) return 0.0;

            double totalMaxVolume = 0.0;
            double totalCurrentVolume = 0.0;

            foreach (var container in _cargoContainers)
            {
                var inventory = container.GetInventory();
                if (inventory != null)
                {
                    totalMaxVolume += (double)inventory.MaxVolume;
                    totalCurrentVolume += (double)inventory.CurrentVolume;
                }
            }

            if (totalMaxVolume == 0.0) return 0.0;
            return (totalCurrentVolume / totalMaxVolume) * 100.0;
        }

        public void UpdateLcdPanels()
        {
            var cargoPercentage = GetCargoFullnessPercentage();
            var displayText = $"{(int)cargoPercentage}%";

            foreach (var panel in _lcdPanels)
            {
                panel.WriteText(displayText);
            }
        }

        public void TurnOnDrills()
        {
            foreach (var drill in _drills)
            {
                drill.Enabled = true;
            }
        }

        public void TurnOffDrills()
        {
            foreach (var drill in _drills)
            {
                drill.Enabled = false;
            }
        }

        public void SetHandbrake(bool enabled)
        {
            _roverNavigation.SetHandbrake(enabled);
        }

        public void LowerDrills()
        {
            // Compress up pistons (negative velocity)
            foreach (var piston in _pistonsUp)
            {
                piston.Velocity = -(float)PISTON_EXTENSION_SPEED;
            }
            
            // Extend down pistons (positive velocity)
            foreach (var piston in _pistonsDown)
            {
                piston.Velocity = (float)PISTON_EXTENSION_SPEED;
            }
        }

        public void RaiseDrills()
        {
            // Extend up pistons back to original position (positive velocity)
            foreach (var piston in _pistonsUp)
            {
                piston.Velocity = (float)PISTON_RETRACTION_SPEED;
            }
            
            // Compress down pistons back to original position (negative velocity)
            foreach (var piston in _pistonsDown)
            {
                piston.Velocity = -(float)PISTON_RETRACTION_SPEED;
            }
        }

        public bool IsDrillLowered()
        {
            // Check if up pistons are compressed
            foreach (var piston in _pistonsUp)
            {
                if (piston.CurrentPosition > 0.1) // Not fully compressed
                {
                    return false;
                }
            }
            
            // Check if down pistons are extended
            foreach (var piston in _pistonsDown)
            {
                if (piston.CurrentPosition < piston.MaxLimit - 0.1) // Not fully extended
                {
                    return false;
                }
            }
            
            return true;
        }

        public bool IsDrillRaised()
        {
            // Check if up pistons are extended (back to original position)
            foreach (var piston in _pistonsUp)
            {
                if (piston.CurrentPosition < piston.MaxLimit - 0.1) // Not fully extended
                {
                    return false;
                }
            }
            
            // Check if down pistons are compressed (back to original position)
            foreach (var piston in _pistonsDown)
            {
                if (piston.CurrentPosition > 0.1) // Not fully compressed
                {
                    return false;
                }
            }
            
            return true;
        }
    }
}

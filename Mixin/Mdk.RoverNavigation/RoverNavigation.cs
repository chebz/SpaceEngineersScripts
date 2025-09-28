using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public class RoverNavigation
    {
    public enum SuspensionDir
    {
        ForwardLeft,
        ForwardRight,
        BackwardLeft,
        BackwardRight
    }

        #region Fields
        private Program _program;
        private IMyRemoteControl _remoteControl;
        private readonly Dictionary<SuspensionDir, List<IMyMotorSuspension>> _suspensions = new Dictionary<SuspensionDir, List<IMyMotorSuspension>>
        {
            { SuspensionDir.ForwardLeft, new List<IMyMotorSuspension>() },
            { SuspensionDir.ForwardRight, new List<IMyMotorSuspension>() },
            { SuspensionDir.BackwardLeft, new List<IMyMotorSuspension>() },
            { SuspensionDir.BackwardRight, new List<IMyMotorSuspension>() }
        };
        #endregion


        #region Methods
        public bool Initialize(Program program, out string errorMessage)
        {
            _program = program;
            errorMessage = string.Empty;

            // Initialize remote control
            _remoteControl = _program.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                errorMessage = "RoverNavigation: No remote control found!";
                return false;
            }

            return InitializeSuspensions(out errorMessage);
        }

        private bool InitializeSuspensions(out string errorMessage)
        {
            errorMessage = string.Empty;

            // Clear existing suspension lists
            _suspensions[SuspensionDir.ForwardLeft].Clear();
            _suspensions[SuspensionDir.ForwardRight].Clear();
            _suspensions[SuspensionDir.BackwardLeft].Clear();
            _suspensions[SuspensionDir.BackwardRight].Clear();

            // Get all motor suspensions on the grid
            var allSuspensions = _program.GetLocalBlocks<IMyMotorSuspension>();

            // Get grid's center of mass
            var gridCenterOfMass = _remoteControl.CenterOfMass;
            
            // Get remote control's orientation vectors (this determines forward/backward)
            var remoteMatrix = _remoteControl.WorldMatrix;
            var remoteForward = remoteMatrix.Forward;
            var remoteRight = remoteMatrix.Right;

            // Categorize suspensions based on their position relative to grid's center of mass
            foreach (var suspension in allSuspensions)
            {
                var suspensionPosition = suspension.GetPosition();
                var relativePosition = suspensionPosition - gridCenterOfMass;

                // Calculate dot products to determine position relative to remote control orientation
                var forwardDot = Vector3D.Dot(relativePosition, remoteForward);
                var rightDot = Vector3D.Dot(relativePosition, remoteRight);

                // Determine forward/backward and left/right position
                bool isForward = forwardDot > 0;
                bool isRight = rightDot > 0;

                // Add to appropriate list based on position
                if (isForward && !isRight) // Forward-Left
                {
                    _suspensions[SuspensionDir.ForwardLeft].Add(suspension);
                    suspension.CustomName = $"Forward Left Suspension {_suspensions[SuspensionDir.ForwardLeft].Count}";
                }
                else if (isForward && isRight) // Forward-Right
                {
                    _suspensions[SuspensionDir.ForwardRight].Add(suspension);
                    suspension.CustomName = $"Forward Right Suspension {_suspensions[SuspensionDir.ForwardRight].Count}";
                }
                else if (!isForward && !isRight) // Backward-Left
                {
                    _suspensions[SuspensionDir.BackwardLeft].Add(suspension);
                    suspension.CustomName = $"Backward Left Suspension {_suspensions[SuspensionDir.BackwardLeft].Count}";
                }
                else if (!isForward && isRight) // Backward-Right
                {
                    _suspensions[SuspensionDir.BackwardRight].Add(suspension);
                    suspension.CustomName = $"Backward Right Suspension {_suspensions[SuspensionDir.BackwardRight].Count}";
                }
            }

            if (_suspensions[SuspensionDir.ForwardLeft].Count == 0 && 
                _suspensions[SuspensionDir.ForwardRight].Count == 0 &&
                _suspensions[SuspensionDir.BackwardLeft].Count == 0 && 
                _suspensions[SuspensionDir.BackwardRight].Count == 0)
            {
                errorMessage = "RoverNavigation: No suspensions found!";
                return false;
            }

            return true;
        }

        public void NavigateForward(double propulsionOverride)
        {
            // For forward movement, set propulsion and no steering
            SetSuspensionControls(propulsionOverride, 0);
        }

        public void NavigateBackward(double propulsionOverride)
        {
            // For backward movement, set negative propulsion and no steering
            SetSuspensionControls(-propulsionOverride, 0);
        }

        public void NavigateForwardRight(double propulsionOverride)
        {
            SetSuspensionControls(propulsionOverride, 1);
        }

        public void NavigateForwardLeft(double propulsionOverride)
        {
            SetSuspensionControls(propulsionOverride, -1);
        }

        public void NavigateBackwardRight(double propulsionOverride)
        {
            SetSuspensionControls(-propulsionOverride, 1);
        }

        public void NavigateBackwardLeft(double propulsionOverride)
        {
            SetSuspensionControls(-propulsionOverride, -1);
        }

        public void Stop()
        {
            SetSuspensionControls(0, 0);
        }

        public void SetHandbrake(bool enabled)
        {
            foreach (var suspension in _suspensions[SuspensionDir.ForwardLeft])
            {
                suspension.Brake = enabled;
            }
            foreach (var suspension in _suspensions[SuspensionDir.ForwardRight])
            {
                suspension.Brake = enabled;
            }
            foreach (var suspension in _suspensions[SuspensionDir.BackwardLeft])
            {
                suspension.Brake = enabled;
            }
            foreach (var suspension in _suspensions[SuspensionDir.BackwardRight])
            {
                suspension.Brake = enabled;
            }
        }

        private void SetSuspensionControls(double propulsionOverride, double steeringOverride)
        {
            // Forward wheels: normal propulsion direction
            foreach (var suspension in _suspensions[SuspensionDir.ForwardLeft])
            {
                suspension.PropulsionOverride = (float)propulsionOverride;
                suspension.SteeringOverride = (float)steeringOverride;
            }

            foreach (var suspension in _suspensions[SuspensionDir.ForwardRight])
            {
                suspension.PropulsionOverride = (float)-propulsionOverride;
                suspension.SteeringOverride = (float)steeringOverride;
            }

            // Backward wheels: reverse propulsion direction
            foreach (var suspension in _suspensions[SuspensionDir.BackwardLeft])
            {
                suspension.PropulsionOverride = (float)propulsionOverride;
                suspension.SteeringOverride = -(float)steeringOverride;
            }

            foreach (var suspension in _suspensions[SuspensionDir.BackwardRight])
            {
                suspension.PropulsionOverride = (float)-propulsionOverride;
                suspension.SteeringOverride = -(float)steeringOverride;
            }
        }
        #endregion
    }
}
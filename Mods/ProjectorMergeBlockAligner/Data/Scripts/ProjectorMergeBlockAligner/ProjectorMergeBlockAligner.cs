using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRage.ModAPI;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace ProjectorMergeBlockAligner
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation, 1000)]
    class ProjectorMergeBlockAlignerSession : MySessionComponentBase
    {
        static Dictionary<IMyProjector, IMyTerminalControlButton> projectorAlignButtons = new Dictionary<IMyProjector, IMyTerminalControlButton>();

        public override void LoadData()
        {
            base.LoadData();
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomTerminalControlGetter;
            MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Mod loaded! Align buttons will be added to projector terminals");
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomTerminalControlGetter;
            projectorAlignButtons.Clear();
        }

        public static void CustomTerminalControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> list)
        {
            try
            {
                if (!(block is IMyProjector))
                    return;

                var projector = block as IMyProjector;
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Processing projector: {projector.CustomName}");
                
                if (!projectorAlignButtons.ContainsKey(projector))
                {
                    var alignButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("AlignMergeBlocks");
                    alignButton.Title = MyStringId.GetOrCompute("Align Merge Blocks");
                    alignButton.Tooltip = MyStringId.GetOrCompute("Aligns the projector grid with merge blocks for precise connection");
                    alignButton.Action = (projectorBlock) => AlignMergeBlocks(projectorBlock as IMyProjector);
                    alignButton.Enabled = (projectorBlock) => IsProjectorReady(projectorBlock as IMyProjector);
                    alignButton.Visible = (projectorBlock) => true;
                    
                    projectorAlignButtons[projector] = alignButton;
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Created align button for: {projector.CustomName}");
                }

                var alignButtonControl = projectorAlignButtons[projector];
                if (!list.Contains(alignButtonControl))
                {
                    list.Insert(0, alignButtonControl);
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Added align button to terminal for: {projector.CustomName}");
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(e.ToString());
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Exception in terminal control! Check logs.");
            }
        }

        private static bool IsProjectorReady(IMyProjector projector)
        {
            if (projector == null || !projector.IsFunctional)
                return false;

            if (!projector.IsProjecting)
                return false;

            return true;
        }

        private static void AlignMergeBlocks(IMyProjector projector)
        {
            try
            {
                if (!IsProjectorReady(projector))
                {
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Projector must be functional and projecting to align");
                    return;
                }

                var projectorGrid = projector.CubeGrid;
                var projectedGrid = projector.ProjectedGrid;

                if (projectedGrid == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "No projected grid found");
                    return;
                }

                var projectorMergeBlock = FindMergeBlock(projectorGrid);
                var projectedMergeBlock = FindMergeBlock(projectedGrid);

                if (projectorMergeBlock == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "No merge block found on projector grid");
                    return;
                }

                if (projectedMergeBlock == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "No merge block found on projected grid");
                    return;
                }

                var (offset, rotation) = CalculateAlignment(projectorMergeBlock, projectedMergeBlock);
                ApplyAlignment(projector, offset, rotation);
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Alignment applied successfully");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(e.ToString());
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Exception during alignment! Check logs.");
            }
        }

        private static SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock FindMergeBlock(IMyCubeGrid grid)
        {
            var slimBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(slimBlocks, (block) => block.FatBlock is SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock);

            return slimBlocks.Select(block => block.FatBlock as SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock).FirstOrDefault();
        }

        private static (Vector3I offset, Vector3I rotation) CalculateAlignment(SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock mb0, SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock mb1)
        {
            try
            {
                // Step 1: Get world positions of both merge blocks
                var mb0WorldPos = mb0.GetPosition();
                var mb1WorldPos = mb1.GetPosition();
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB0 world pos: {mb0WorldPos.X:F2}, {mb0WorldPos.Y:F2}, {mb0WorldPos.Z:F2}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB1 world pos: {mb1WorldPos.X:F2}, {mb1WorldPos.Y:F2}, {mb1WorldPos.Z:F2}");
                
                // Step 2: Get forward and up directions in world space
                var mb0Forward = mb0.WorldMatrix.Forward;
                var mb0Up = mb0.WorldMatrix.Up;
                var mb1Forward = mb1.WorldMatrix.Forward;
                var mb1Up = mb1.WorldMatrix.Up;
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB0 forward: {mb0Forward.X:F2}, {mb0Forward.Y:F2}, {mb0Forward.Z:F2}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB0 up: {mb0Up.X:F2}, {mb0Up.Y:F2}, {mb0Up.Z:F2}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB1 forward: {mb1Forward.X:F2}, {mb1Forward.Y:F2}, {mb1Forward.Z:F2}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"MB1 up: {mb1Up.X:F2}, {mb1Up.Y:F2}, {mb1Up.Z:F2}");

                // Step 3: Calculate where MB1 should be to align with MB0
                // We want MB1 to be 2.5m in front of MB0
                var targetWorldPos = mb0WorldPos + (mb0Forward * 2.5);
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Target world pos: {targetWorldPos.X:F2}, {targetWorldPos.Y:F2}, {targetWorldPos.Z:F2}");
                
                // Step 4: Convert target world position to projector's local grid coordinates
                var projectorGrid = mb0.CubeGrid;
                var targetLocalPos = projectorGrid.WorldToGridInteger(targetWorldPos);
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Target local pos: {targetLocalPos.X}, {targetLocalPos.Y}, {targetLocalPos.Z}");
                
                // Step 5: Calculate the offset needed in projector's local coordinates
                var mb1LocalPos = mb1.Position;
                var offset = targetLocalPos - mb1LocalPos;
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Calculated offset: {offset.X}, {offset.Y}, {offset.Z}");
                
                // Step 6: Calculate rotation for MB1 to face MB0
                // MB1 should be oriented so its forward faces MB0's forward (opposite direction)
                // and its up faces MB0's up
                var rotation = CalculateRotation(mb0Forward, mb0Up, mb1Forward, mb1Up);
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Target rotation: {rotation.X}, {rotation.Y}, {rotation.Z}");

                return (offset, rotation);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"Error calculating alignment: {e}");
                return (Vector3I.Zero, Vector3I.Zero);
            }
        }

        private static void ApplyAlignment(IMyProjector projector, Vector3I offset, Vector3I rotation)
        {
            try
            {
                var projectedGrid = projector.ProjectedGrid;
                if (projectedGrid == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "No projected grid found!");
                    return;
                }

                var currentOffset = projector.ProjectionOffset;
                var newOffset = currentOffset + offset;

                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Current offset: {currentOffset.X}, {currentOffset.Y}, {currentOffset.Z}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"New offset: {newOffset.X}, {newOffset.Y}, {newOffset.Z}");

                projector.ProjectionOffset = newOffset;
                projector.ProjectionRotation = rotation;
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", "Alignment applied successfully!");
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"Error applying alignment: {e}");
            }
        }

        private static Vector3I GetDirectionVector(Base6Directions.Direction direction)
        {
            switch (direction)
            {
                case Base6Directions.Direction.Forward: return new Vector3I(1, 0, 0);
                case Base6Directions.Direction.Backward: return new Vector3I(-1, 0, 0);
                case Base6Directions.Direction.Left: return new Vector3I(0, 0, 1);
                case Base6Directions.Direction.Right: return new Vector3I(0, 0, -1);
                case Base6Directions.Direction.Up: return new Vector3I(0, 1, 0);
                case Base6Directions.Direction.Down: return new Vector3I(0, -1, 0);
                default: return new Vector3I(1, 0, 0);
            }
        }

        private static Vector3I CalculateRotation(Vector3D mb0Forward, Vector3D mb0Up, Vector3D mb1Forward, Vector3D mb1Up)
        {
            try
            {
                // MB1 should be oriented so its forward faces MB0's forward (opposite direction)
                // and its up faces MB0's up
                var targetForward = -mb0Forward; // MB1 should face opposite of MB0's forward
                var targetUp = mb0Up; // MB1 should have same up as MB0
                
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Target forward: {targetForward.X:F2}, {targetForward.Y:F2}, {targetForward.Z:F2}");
                MyAPIGateway.Utilities.ShowMessage("ProjectorMergeBlockAligner", $"Target up: {targetUp.X:F2}, {targetUp.Y:F2}, {targetUp.Z:F2}");
                
                // Calculate the rotation needed to align MB1 with target orientation
                // This is a simplified approach - in practice you'd need proper quaternion math
                // For now, we'll calculate basic rotation angles
                
                // Convert to ProjectionRotation format (1 = 90 degrees, 2 = 180 degrees)
                var rotationX = CalculateAxisRotation(mb1Up, targetUp, Vector3D.Right);
                var rotationY = CalculateAxisRotation(mb1Forward, targetForward, Vector3D.Up);
                var rotationZ = CalculateAxisRotation(mb1Up, targetUp, Vector3D.Forward);
                
                return new Vector3I(rotationX, rotationY, rotationZ);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"Error calculating rotation: {e}");
                return Vector3I.Zero;
            }
        }
        
        private static int CalculateAxisRotation(Vector3D from, Vector3D to, Vector3D axis)
        {
            // Calculate rotation around a specific axis
            // This is a simplified implementation
            var dot = Vector3D.Dot(from, to);
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, dot)));
            var degrees = angle * 180 / Math.PI;
            
            // Convert to ProjectionRotation format (1 = 90 degrees, 2 = 180 degrees)
            return (int)Math.Round(degrees / 90);
        }
    }
}

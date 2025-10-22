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
using VRage.Game.ModAPI.Ingame;
using VRageRender;
using VRage.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders;
using ProtoBuf;
using Sandbox.Game.EntityComponents;

namespace ToolInfo
{
    [ProtoContract]
    public class ToolInfoComponent : MyGameLogicComponent
    {
        public readonly Guid SETTINGS_GUID = new Guid("88b6e84f-15c6-4df3-8d72-3177fa386d2f");

        // WELDER
        private const double SMALL_WELDER_DISTANCE = 1;
        private const double LARGE_WELDER_DISTANCE = 2.65;
        private const double SMALL_WELDER_RADIUS = 1.6;
        private const double LARGE_WELDER_RADIUS = 2.25;
        
        // GRINDER
        private const double SMALL_GRINDER_DISTANCE = 1;
        private const double LARGE_GRINDER_DISTANCE = 1.5;
        private const double SMALL_GRINDER_RADIUS = 1.6;
        private const double LARGE_GRINDER_RADIUS = 1.45;

        private IMyShipToolBase _tool;
        private double _distance;
        private double _radius;

        private List<VRage.Game.ModAPI.IMySlimBlock> _incompleteBlocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
        private bool _lastCanUseState = false;
        private bool _settingsLoaded = false;
        private bool _settingsNeedSaving = false;

        public readonly ToolInfoSettings Settings = new ToolInfoSettings();

        public bool SettingsNeedSaving
        {
            get 
            {
                return _settingsNeedSaving;
            }
            set 
            {
                _settingsNeedSaving = value;
                if (value)
                {
                    SaveSettings();
                }
            }
        }

        public bool EnableToolInfo 
        { 
            get 
            {
                return Settings.EnableToolInfo; 
            }
            set 
            {
                Settings.EnableToolInfo = value; 
                _settingsNeedSaving = true;
            }
        }

        public bool ShowDebugDraw
        {
            get
            {
                return Settings.ShowDebugDraw;
            }
            set
            {
                Settings.ShowDebugDraw = value;
                _settingsNeedSaving = true;
            }
        }

        public event Action<IMyShipToolBase, bool> CanUseStateChanged;

        static Color colorRed = Color.Red;
        static Color colorGreen = Color.Green;
        static MyStringId particle = MyStringId.GetOrCompute("GizmoDrawLine");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Entity is IMyShipToolBase)
            {
                _tool = (IMyShipToolBase)Entity;
                var isSmallTool = _tool.BlockDefinition.SubtypeName.Contains("Small");
                var isWelder = _tool.BlockDefinition.SubtypeName.Contains("Welder");    
                if (isWelder)
                {
                    _radius = isSmallTool ? SMALL_WELDER_RADIUS : LARGE_WELDER_RADIUS;
                    _distance = isSmallTool ? SMALL_WELDER_DISTANCE : LARGE_WELDER_DISTANCE;
                }
                else
                {
                    _radius = isSmallTool ? SMALL_GRINDER_RADIUS : LARGE_GRINDER_RADIUS;
                    _distance = isSmallTool ? SMALL_GRINDER_DISTANCE : LARGE_GRINDER_DISTANCE;
                }

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            }
        }

        bool LoadSettings()
        {
            if(_tool.Storage == null)
            {
                _tool.Storage = new MyModStorageComponent();
            }

            string rawData;
            if(!_tool.Storage.TryGetValue(SETTINGS_GUID, out rawData))
            {
                return false;
            }

            try
            {
                var loadedSettings = MyAPIGateway.Utilities.SerializeFromBinary<ToolInfoSettings>(Convert.FromBase64String(rawData));

                if(loadedSettings != null)
                {
                    Settings.EnableToolInfo = loadedSettings.EnableToolInfo;
                    Settings.ShowDebugDraw = loadedSettings.ShowDebugDraw;
                    return true;
                }
            }
            catch(Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("ToolInfo", $"Error loading settings: {e.Message}");
            }

            return false;
        }

        void SaveSettings()
        {
            if (_tool == null)
            {
                return;
            }
            if(_tool.Storage == null)
            {
                _tool.Storage = new MyModStorageComponent();
            }
            try
            {
                var data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Settings));
                _tool.Storage.SetValue(SETTINGS_GUID, data);
            }
            catch(Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("ToolInfo", $"Error saving settings: {e.Message}");
            }
        }

        public bool CanUse()
        {
            try
            {
                // Check if ToolInfo is enabled for this tool
                if (!EnableToolInfo)
                {
                    return false;
                }

                _incompleteBlocks.Clear();
                if (!_tool.IsActivated)
                {
                    return false;
                }

                var toolPosition = _tool.GetPosition();
                var toolForward = _tool.WorldMatrix.Forward;
                
                var centerPosition = toolPosition + (toolForward * _distance);
                var boundingSphere = new BoundingSphereD(centerPosition, _radius);
                
                var entitiesInArea = new List<MyEntity>();
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref boundingSphere, entitiesInArea);

                if (entitiesInArea.Count == 0)
                {
                    return false;
                }

                foreach (var entity in entitiesInArea)
                {
                    var cubeGrid = entity as VRage.Game.ModAPI.IMyCubeGrid;
                    if (cubeGrid == null) 
                    {
                        continue;
                    }

                    var slimBlocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
                    cubeGrid.GetBlocks(slimBlocks);

                    foreach (var slimBlock in slimBlocks)
                    {
                        if (slimBlock.FatBlock == _tool) 
                        {
                            continue;
                        }
                        var blockOBB = GetBlockOBB(slimBlock);
                        if (boundingSphere.Contains(blockOBB) != ContainmentType.Disjoint)
                        {
                            if (!slimBlock.IsFullIntegrity)
                            {
                                _incompleteBlocks.Add(slimBlock);
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch
            {
                MyAPIGateway.Utilities.ShowMessage("ToolInfo", $"Error in CanUse for {_tool.Name}");
                return false;
            }
        }
    
        public override void UpdateAfterSimulation()
        {
            // Try to load settings if not loaded yet and storage is available
            if (!_settingsLoaded)
            {
                LoadSettings();
                _settingsLoaded = true;
            }
            
            // Try to save settings if needed and storage is available
            if (_settingsNeedSaving)
            {
                SaveSettings();
                _settingsNeedSaving = false;
            }

            if (!Settings.EnableToolInfo)
            {
                return;
            }
            
            var currentCanUse = CanUse();
            
            if (currentCanUse != _lastCanUseState)
            {
                _lastCanUseState = currentCanUse;
                CanUseStateChanged?.Invoke(_tool, currentCanUse);
            }

            if (ShowDebugDraw)
            {
                var position = _tool.GetPosition() + _tool.WorldMatrix.Forward * _distance;
                var radius = _radius;
                RenderWireframeSphere(new BoundingSphereD(position, radius), colorGreen);
                var toolOBB = GetBlockOBB(_tool.SlimBlock);
                //MyAPIGateway.Utilities.ShowMessage("WelderInfo", $"WelderOBB: {welderOBB.HalfExtent.X:F2}, {welderOBB.HalfExtent.Y:F2}, {welderOBB.HalfExtent.Z:F2}");
                RenderOBB(toolOBB, colorGreen);
                foreach (var block in _incompleteBlocks)
                {
                    var blockOBB = GetBlockOBB(block);                
                    RenderOBB(blockOBB, colorRed);
                }
            }
        }

        private static MyOrientedBoundingBoxD GetBlockOBB(VRage.Game.ModAPI.IMySlimBlock block)
        {
            Vector3D worldCtr;
            block.ComputeWorldCenter(out worldCtr);
            var myGrid = (MyCubeGrid)block.CubeGrid;
            var halfExtents = (block.Max + 1 - block.Min) * myGrid.GridSizeHalf;
            var matrix = block.CubeGrid.PositionComp.WorldMatrixRef;
            matrix.Translation = worldCtr;
            return new MyOrientedBoundingBoxD(new BoundingBoxD(-halfExtents, halfExtents), matrix);
        }

        private static void RenderWireframeSphere(BoundingSphereD sphere, Color color)
        {
            try
            {
                var matrix = MatrixD.CreateTranslation(sphere.Center);
                var radius = sphere.Radius;

                MySimpleObjectDraw.DrawTransparentSphere(
                    ref matrix, 
                    (float)radius, 
                    ref color, 
                    MySimpleObjectRasterizer.Wireframe, 
                    12,
                    null,
                    particle,
                    0.02f,
                    -1,
                    null,
                    MyBillboard.BlendTypeEnum.AdditiveTop,
                    2f
                );
            }
            catch
            {
            }
        }

        private static void RenderOBB(MyOrientedBoundingBoxD obb, Color color)
        {
            try
            {
                var bounds = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);
                var wm = MatrixD.CreateFromTransformScale(obb.Orientation, obb.Center, Vector3D.One);
                
                MySimpleObjectDraw.DrawTransparentBox(
                    ref wm, 
                    ref bounds, 
                    ref color, 
                    MySimpleObjectRasterizer.Wireframe, 
                    1, 
                    0.02f, 
                    null, 
                    particle, 
                    false, 
                    -1, 
                    MyBillboard.BlendTypeEnum.AdditiveTop, 
                    1f
                );
            }
            catch
            {
            }
        }

        private static void RenderAABB(BoundingBoxD aabb, Color color)
        {
            try
            {
                var wm = MatrixD.Identity;
                var bounds = aabb;
                MySimpleObjectDraw.DrawTransparentBox(
                    ref wm, 
                    ref bounds, 
                    ref color, 
                    MySimpleObjectRasterizer.Wireframe, 
                    1, 
                    0.02f, 
                    null, 
                    particle, 
                    false, 
                    -1, 
                    MyBillboard.BlendTypeEnum.AdditiveTop, 
                    1f
                );
            }
            catch
            {
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false)]
    public class WelderInfoComponent : ToolInfoComponent {}

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipGrinder), false)]
    public class GrinderInfoComponent : ToolInfoComponent {}
}

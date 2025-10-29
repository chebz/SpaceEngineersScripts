using VRageMath;
using VRageRender;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace Pathfinder
{
    public static class Utils
    {
        private static readonly MyStringId _gizmoDrawLine = MyStringId.GetOrCompute("GizmoDrawLine");

        public static void DrawAabb(BoundingBoxD aabb, Color color, bool wireframe, float intensity = 1f, float lineWidth = 0.02f)
        {
            var matrix = MatrixD.Identity;
            var rasterizer = wireframe ? MySimpleObjectRasterizer.Wireframe : MySimpleObjectRasterizer.Solid;
            var stringId = wireframe ? _gizmoDrawLine : (MyStringId?)null;
            MySimpleObjectDraw.DrawTransparentBox(
                ref matrix, 
                ref aabb, 
                ref color, 
                rasterizer, 
                1, 
                lineWidth, 
                null, 
                stringId, 
                false, 
                -1, 
                MyBillboard.BlendTypeEnum.AdditiveTop, 
                intensity);
        }

        public static void DrawLine(Vector3D start, Vector3D end, Color color, float width = 1f)
        {
            var lineColor = color.ToVector4();
            MySimpleObjectDraw.DrawLine(
                start, 
                end, 
                null, 
                ref lineColor, 
                width, 
                MyBillboard.BlendTypeEnum.AdditiveTop);
        }

        public static void DrawSphere(Vector3D position, Color color, float radius, bool wireframe, float lineWidth = 0.02f)
        {
            var sphereColor = color;
            var rasterizer = wireframe ? MySimpleObjectRasterizer.Wireframe : MySimpleObjectRasterizer.Solid;
            var stringId = wireframe ? _gizmoDrawLine : (MyStringId?)null;
            var matrix = MatrixD.CreateTranslation(position);
            MySimpleObjectDraw.DrawTransparentSphere(
                ref matrix, 
                radius, 
                ref sphereColor, 
                rasterizer, 
                12, 
                null, 
                stringId, 
                lineWidth,
                -1, 
                null, 
                MyBillboard.BlendTypeEnum.AdditiveTop, 
                1f);
        }

        public static MyOrientedBoundingBoxD GetBlockOBB(IMySlimBlock block)
        {
            Vector3D worldCtr;
            block.ComputeWorldCenter(out worldCtr);
            var myGrid = (MyCubeGrid)block.CubeGrid;
            var halfExtents = (block.Max + 1 - block.Min) * myGrid.GridSizeHalf;
            var matrix = block.CubeGrid.PositionComp.WorldMatrixRef;
            matrix.Translation = worldCtr;
            return new MyOrientedBoundingBoxD(new BoundingBoxD(-halfExtents, halfExtents), matrix);
        }

    }
}
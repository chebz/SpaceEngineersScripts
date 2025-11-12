using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRageRender;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Globalization;
using System.Text;
using Pathfinder.OctreeAStar;
using VRage.Game.Entity;

namespace Pathfinder
{
    public static class Utils
    {
        #region Logging
        private const string LOG_FILE_NAME = "Pathfinder.log";
        private const string APP_VERSION = "1.0.0";

        private static MyLog _log;

        public static void InitializeLog()
        {
            if (_log != null)
            {
                return;
            }

            var log = new MyLog();
            log.Init(LOG_FILE_NAME, new StringBuilder(APP_VERSION));
            _log = log;
        }

        public static void ShutdownLog()
        {
            if (_log == null)
            {
                return;
            }

            _log.Close();
            _log = null;
        }

        public static void Log(MyLogSeverity severity, string format, params object[] args)
        {
            if (_log == null)
            {
                return;
            }

            _log.Log(severity, format, args);
            _log.Flush();
        }
        #endregion

        #region Debug Render
        private static readonly MyStringId _gizmoDrawLine = MyStringId.GetOrCompute("Square");
        public static void DrawAabb(BoundingBoxD aabb, Color color, bool wireframe, float intensity = 1f, float lineWidth = 0.02f)
        {
            var matrix = MatrixD.Identity;
            var rasterizer = wireframe ? MySimpleObjectRasterizer.Wireframe : MySimpleObjectRasterizer.Solid;
            var stringId = wireframe ? _gizmoDrawLine : (MyStringId?)null;
            lineWidth = (float)(1 / aabb.Size.Max()) * lineWidth;
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
                MyBillboard.BlendTypeEnum.Standard, 
                intensity);
        }

        public static void DrawLine(Vector3D start, Vector3D end, Color color, float width = 1f)
        {
            var lineColor = color.ToVector4();
            MySimpleObjectDraw.DrawLine(
                start, 
                end, 
                _gizmoDrawLine, 
                ref lineColor, 
                width, 
                MyBillboard.BlendTypeEnum.Standard);
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
                MyBillboard.BlendTypeEnum.Standard, 
                1f);
        }
        #endregion

        #region Debug Timings
        private class CallFrame
        {
            public DateTime Start;
            public DateTime End;
            public TimeSpan Duration;
            public int CallCount;
        }
        private static readonly Dictionary<string, CallFrame> _callFrames = new Dictionary<string, CallFrame>();
        public static void StartCallFrame(string name)
        {
            CallFrame frame;
            if (!_callFrames.TryGetValue(name, out frame))
            {
                frame = new CallFrame();
                _callFrames.Add(name, frame);
            }
            frame.Start = DateTime.Now;
            frame.CallCount++;
        }
        public static void EndCallFrame(string name)
        {
            CallFrame frame;
            if (!_callFrames.TryGetValue(name, out frame))
            {
                return;
            }
            frame.End = DateTime.Now;
            frame.Duration += frame.End - frame.Start;
        }

        public static void PrintCallFrames()
        {
            foreach (var frame in _callFrames)
            {
                var duration = frame.Value.Duration.TotalSeconds;
                MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"{frame.Key}: {duration:F3}s, {frame.Value.CallCount} calls");
            }
        }
        #endregion
        
        #region Math
        public static readonly List<Vector3D> CardinalDirections = new List<Vector3D>
        {
            new Vector3D(1, 0, 0),
            new Vector3D(0, 1, 0),
            new Vector3D(0, 0, 1),
            new Vector3D(-1, 0, 0),
            new Vector3D(0, -1, 0),
            new Vector3D(0, 0, -1)
        };

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

        public static ContainmentType Contains(this BoundingBoxD bounds, BoundingBoxD other)
        {
            var otherCorners = new Vector3D[8];
            otherCorners[0] = other.Center + new Vector3D(-other.HalfExtents.X, -other.HalfExtents.Y, -other.HalfExtents.Z);
            otherCorners[1] = other.Center + new Vector3D(other.HalfExtents.X, -other.HalfExtents.Y, -other.HalfExtents.Z);
            otherCorners[2] = other.Center + new Vector3D(-other.HalfExtents.X, other.HalfExtents.Y, -other.HalfExtents.Z);
            otherCorners[3] = other.Center + new Vector3D(other.HalfExtents.X, other.HalfExtents.Y, -other.HalfExtents.Z);
            otherCorners[4] = other.Center + new Vector3D(-other.HalfExtents.X, -other.HalfExtents.Y, other.HalfExtents.Z);
            otherCorners[5] = other.Center + new Vector3D(other.HalfExtents.X, -other.HalfExtents.Y, other.HalfExtents.Z);
            otherCorners[6] = other.Center + new Vector3D(-other.HalfExtents.X, other.HalfExtents.Y, other.HalfExtents.Z);
            otherCorners[7] = other.Center + new Vector3D(other.HalfExtents.X, other.HalfExtents.Y, other.HalfExtents.Z);

            var containedCount = 0;
            foreach (var corner in otherCorners)
            {
                if (bounds.Contains(corner) == ContainmentType.Contains)
                {
                    containedCount++;
                }
            }

            if (containedCount == 8)
            {
                return ContainmentType.Contains;
            }
            if (containedCount > 0)
            {
                return ContainmentType.Intersects;
            }

            var boundsCorners = new Vector3D[8];
            boundsCorners[0] = bounds.Center + new Vector3D(-bounds.HalfExtents.X, -bounds.HalfExtents.Y, -bounds.HalfExtents.Z);
            boundsCorners[1] = bounds.Center + new Vector3D(bounds.HalfExtents.X, -bounds.HalfExtents.Y, -bounds.HalfExtents.Z);
            boundsCorners[2] = bounds.Center + new Vector3D(-bounds.HalfExtents.X, bounds.HalfExtents.Y, -bounds.HalfExtents.Z);
            boundsCorners[3] = bounds.Center + new Vector3D(bounds.HalfExtents.X, bounds.HalfExtents.Y, -bounds.HalfExtents.Z);
            boundsCorners[4] = bounds.Center + new Vector3D(-bounds.HalfExtents.X, -bounds.HalfExtents.Y, bounds.HalfExtents.Z);
            boundsCorners[5] = bounds.Center + new Vector3D(bounds.HalfExtents.X, -bounds.HalfExtents.Y, bounds.HalfExtents.Z);
            boundsCorners[6] = bounds.Center + new Vector3D(-bounds.HalfExtents.X, bounds.HalfExtents.Y, bounds.HalfExtents.Z);
            boundsCorners[7] = bounds.Center + new Vector3D(bounds.HalfExtents.X, bounds.HalfExtents.Y, bounds.HalfExtents.Z);

            foreach (var corner in boundsCorners)
            {
                if (other.Contains(corner) == ContainmentType.Contains)
                {
                    return ContainmentType.Intersects;
                }
            }

            if (bounds.Intersects(other))
            {
                return ContainmentType.Intersects;
            }

            return ContainmentType.Disjoint;
        }

        public static double ManhattanDistance(Vector3D a, Vector3D b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
        }

        public static Vector3D FirstOptimalCardinalDirection(Vector3D from, Vector3D to)
        {
            var dir = (to - from);
            dir.Normalize();
            return CardinalDirections.OrderByDescending(d => Vector3D.Dot(d, dir)).First();
        }

        public static Vector3D SecondOptimalCardinalDirection(Vector3D from, Vector3D to)
        {
            var dir = (to - from);
            dir.Normalize();
            return CardinalDirections.OrderByDescending(d => Vector3D.Dot(d, dir)).Skip(1).First();
        }

        public static Vector3D[] GetBoundingBoxCorners(BoundingBoxD box)
        {
            var min = box.Min;
            var max = box.Max;

            return new[]
            {
                new Vector3D(min.X, min.Y, min.Z),
                new Vector3D(max.X, min.Y, min.Z),
                new Vector3D(min.X, max.Y, min.Z),
                new Vector3D(max.X, max.Y, min.Z),
                new Vector3D(min.X, min.Y, max.Z),
                new Vector3D(max.X, min.Y, max.Z),
                new Vector3D(min.X, max.Y, max.Z),
                new Vector3D(max.X, max.Y, max.Z)
            };
        }

        public static Octant.OctantOccupancy GetOccupancy(BoundingBoxD bounds, IMyCubeGrid grid)
        {
            var entitiesInNode = new List<MyEntity>();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref bounds, entitiesInNode);

            foreach (var entity in entitiesInNode)
            {
                if (entity == grid)
                {
                    continue;
                }
                if (entity is MyPlanet)
                {
                    var planet = (MyPlanet)entity;
                    var corners = GetBoundingBoxCorners(bounds);

                    var totalCorners = corners.Length;
                    var undergroundCorners = 0;

                    for (int i = 0; i < totalCorners; i++)
                    {
                        if (planet.IsUnderGround(corners[i]))
                        {
                            undergroundCorners++;
                        }
                    }

                    if (undergroundCorners == 0)
                    {
                        continue;
                    }
                    else if (undergroundCorners == totalCorners)
                    {
                        return Octant.OctantOccupancy.Full;
                    }
                    else
                    {
                        return Octant.OctantOccupancy.PartialInTerrain;
                    }
                }
                
                var voxelMap = entity as MyVoxelMap;
                if (voxelMap != null)
                {
                    if (bounds.Size.Max() > 100)
                    {
                        return Octant.OctantOccupancy.Partial;
                    }
                    var vol = voxelMap.GetVoxelContentInBoundingBox_Fast(bounds, MatrixD.Identity);
                    if (vol.Item2 > 0)
                    {
                        return Octant.OctantOccupancy.Partial;
                    }
                }

                var cubeGrid = entity as IMyCubeGrid;
                if (cubeGrid != null)
                {
                    var slimBlocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(slimBlocks);

                    foreach (var slimBlock in slimBlocks)
                    {
                        var blockOBB = GetBlockOBB(slimBlock);
                        var contains = blockOBB.Contains(ref bounds);
                        if (contains != ContainmentType.Disjoint)
                        {
                            return contains == ContainmentType.Contains ? Octant.OctantOccupancy.Full : Octant.OctantOccupancy.Partial;
                        }
                    }
                }
            }
            return Octant.OctantOccupancy.Empty;
        }
        #endregion

        #region GPS
        public static List<IMyGps> GetGpsList() 
        {
            var gpsList = new List<IMyGps>();
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null || MyAPIGateway.Session.GPS == null)
            {
                return gpsList;
            }
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, gpsList);
            return gpsList;
        }

        public static Vector3D? GetGpsFromKey(long key)
        {
            return GetGpsList().FirstOrDefault(gps => gps.Hash == key)?.Coords;
        }

        public static long ComputeGpsKey(Vector3D? coords)
        {
            if (coords == null)
            {
                return -1;
            }
            var gpsList = GetGpsList();
            foreach (var gps in gpsList)
            {
                if (Vector3D.DistanceSquared(gps.Coords, coords.Value) < 0.1)
                {
                    return gps.Hash;
                }
            }
            return -1;
        }

        public static Vector3D? ParseCoordsFromString(string value)
        {
            var parts = value.Split(',');
            if (parts.Length != 3)
            {
                return null;
            }
            double x, y, z;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return null;
            }
            return new Vector3D(x, y, z);
        }

        public static Vector3D? ParseGpsString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (!trimmed.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = trimmed.Split(':');
            if (parts.Length < 5)
            {
                return null;
            }

            double x;
            double y;
            double z;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                return null;
            }

            return new Vector3D(x, y, z);
        }

        public static string GetStringFromCoords(Vector3D? coords)
        {
            if (coords == null)
            {
                return string.Empty;
            }
            return $"{coords.Value.X:F2},{coords.Value.Y:F2},{coords.Value.Z:F2}";
        }
        #endregion
    }
}
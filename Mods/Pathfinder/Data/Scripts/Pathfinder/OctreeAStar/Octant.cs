using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.Game.GameSystems;
using System;
using VRage.Utils;

namespace Pathfinder.OctreeAStar
{
    public class Octant
    {
        #region OctantOccupancy enum
        public enum OctantOccupancy
        {
            Unknown,
            Empty,
            Partial,
            PartialInTerrain,
            Full
        }
        #endregion

        #region OctantState enum
        public enum OctantState
        {
            Unexplored,
            Open,
            Closed
        }
        #endregion

        #region Fields
        public static int octantCount;

        private readonly Graph _graph;
        public readonly int id;
        public BoundingBoxD bounds;
        public readonly double size;
        public OctantState state = OctantState.Unexplored;
        public OctantOccupancy occupancy = OctantOccupancy.Unknown;
        public double altitude = 0.0;
        public double g;
        public double h = -1;
        public Octant parent;
        public Octant from;
        public bool isReverse;
        public bool isMeet;
        public bool isPartialInTerrain = false;
        public readonly List<Octant> children = new List<Octant>();
        #endregion

        #region Properties
        
        public double ExploreCostInTerrain
        {
            get
            {
                if (!isPartialInTerrain)
                {
                    return 1;
                }
                return _graph.exploreCostFactorInTerrain;
            }
        }
        
        public double ExploreCost => Math.Max(0, _graph.maxRootSize - size) * _graph.exploreCostFactor * ExploreCostInTerrain;
        public double AltitudeCost 
        {
            get
            {
                var min = _graph.MinAltitude;
                var max = _graph.MaxAltitude;
                var useMin = min > 0;
                var useMax = max > 0 && (min <= 0 || max > min);
                if (!useMin && !useMax)
                {
                    return 0;
                }
                if (altitude == 0)
                {
                    CalculateAltitude();
                }
                if (altitude == double.PositiveInfinity)
                {
                    return 0;
                }
                double deviation = 0;
                if (useMin && altitude < min)
                {
                    deviation += min - altitude;
                }
                if (useMax && altitude > max)
                {
                    deviation += altitude - max;
                }
                if (deviation <= 0)
                {
                    return 0;
                }
                var cost = deviation * _graph.altitudeCostFactor;
                // Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"AltitudeCost: {cost}");
                return cost;
            }
        }

        public double F => g * _graph.gFactor + h + ExploreCost + AltitudeCost;

        public bool IsRoot => parent == null;
        public bool IsLeaf => children.Count == 0;
        public Octant Root => IsRoot ? this : parent.Root;
        
        #endregion

        #region Constructors
        public Octant(BoundingBoxD bounds, Graph graph, Octant parent)
        {
            id = octantCount++;
            this.bounds = bounds;
            size = bounds.Size.X;
            _graph = graph;
            this.parent = parent;
        }
        #endregion

        #region Methods
        public void Explore()
        {
            if (occupancy == OctantOccupancy.Full || occupancy == OctantOccupancy.Empty)
            {
                return;
            }
            if (!IsLeaf)
            {
                return;
            }
            if (size <= _graph.AgentSize &&
                (bounds.Contains(_graph.Start) == ContainmentType.Contains))
            {
                occupancy = OctantOccupancy.Empty;
                return;
            }

            occupancy = OctantOccupancy.Empty;
            _graph.IncrementExploreCount();

            if (size > _graph.maxRootSize)
            {
                occupancy = OctantOccupancy.Partial;
            }
            else
            {
                occupancy = Utils.GetOccupancy(bounds, _graph.OwnGrid, _graph.UseDynamicObstacles, _graph.DynamicObstacles);
            }

            if (occupancy == OctantOccupancy.Partial || occupancy == OctantOccupancy.PartialInTerrain)
            {
                if (Subdivide(occupancy == OctantOccupancy.PartialInTerrain))
                {
                    occupancy = OctantOccupancy.Partial;
                    return;
                }
                occupancy = OctantOccupancy.Full;
                return;
            }

        }

        void CalculateAltitude()
        {
            MyPlanet closestPlanet = MyGamePruningStructure.GetClosestPlanet(ref bounds);
            if (closestPlanet == null)
            {
                altitude = double.PositiveInfinity;
                return;
            }
            altitude = (bounds.Center - closestPlanet.PositionComp.GetPosition()).Length() - (double)closestPlanet.AverageRadius;
        }

        public void SubdivideToMaxSize()
        {
            if (size <= _graph.maxRootSize)
            {
                return;
            }
            if (IsLeaf)
            {
                Subdivide(false);
            }
            foreach (var child in children)
            {
                child.SubdivideToMaxSize();
            }
        }

        public bool Subdivide(bool isPartialInTerrain = false)
        {
            if (size <= _graph.AgentSize)
            {
                return false;
            }
            if (!IsLeaf)
            {
                return false;
            }

            var newSize = bounds.Size * 0.5;
            var centerOffset = bounds.Size * 0.25;
            var parentCenter = bounds.Center;

            for (var i = 0; i < 8; i++)
            {
                var childCenter = parentCenter;
                childCenter.X += centerOffset.X * ((i & 1) == 0 ? -1 : 1);
                childCenter.Y += centerOffset.Y * ((i & 2) == 0 ? -1 : 1);
                childCenter.Z += centerOffset.Z * ((i & 4) == 0 ? -1 : 1);
                var childBounds = new BoundingBoxD(childCenter - newSize * 0.5, childCenter + newSize * 0.5);
                var childOctant = new Octant(childBounds, _graph, this)
                {
                    isReverse = isReverse,
                    isPartialInTerrain = isPartialInTerrain
                };
                children.Add(childOctant);
            }
            foreach (var child in children)
            {
                var neighbors = child.GetNeighbors();
                foreach (var neighbor in neighbors)
                {
                    if (neighbor.state == OctantState.Closed &&
                        neighbor.occupancy == OctantOccupancy.Empty &&
                        child.state == OctantState.Unexplored)
                    {
                        child.g = neighbor.g + Vector3D.Distance(child.bounds.Center, neighbor.bounds.Center);
                        child.from = neighbor;
                        child.state = OctantState.Open;
                        child.isReverse = neighbor.isReverse;
                        var open = neighbor.isReverse ? _graph.OpenReverse : _graph.Open;
                        open.Add(child);
                    }
                    else if (child.state == OctantState.Closed &&
                             child.occupancy == OctantOccupancy.Empty &&
                             neighbor.state == OctantState.Unexplored)
                    {
                        neighbor.g = child.g + Vector3D.Distance(child.bounds.Center, neighbor.bounds.Center);
                        neighbor.from = child;
                        neighbor.state = OctantState.Open;
                        neighbor.isReverse = child.isReverse;
                        var open = child.isReverse ? _graph.OpenReverse : _graph.Open;
                        open.Add(neighbor);
                    }
                }
            }
            return true;
        }

        public bool MergeIfAllUnknown()
        {
            if (IsLeaf)
            {
                return occupancy == OctantOccupancy.Unknown || occupancy == OctantOccupancy.Empty;
            }

            foreach (var child in children)
            {
                if (!child.MergeIfAllUnknown())
                {
                    return false;
                }
            }

            children.Clear();
            return true;
        }

        public Octant GetClosestLeaf(Vector3D pos)
        {
            if (bounds.Contains(pos) != ContainmentType.Contains)
            {
                return null;
            }
            if (children.Count == 0)
            {
                return this;
            }

            var closestDistance = double.MaxValue;
            Octant closestChild = null;
            foreach (var child in children)
            {
                var distance = (child.bounds.Center - pos).LengthSquared();
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestChild = child;
                }
            }
            return closestChild?.GetClosestLeaf(pos);
        }

        public void GetLeafs(BoundingBoxD bounds, ref List<Octant> leafs)
        {
            if (bounds.Contains(this.bounds.Center) == ContainmentType.Disjoint && 
                this.bounds.Contains(bounds.Center) == ContainmentType.Disjoint)
            {
                return;
            }
            if (IsLeaf)
            {
                leafs.Add(this);
                return;
            }
            foreach (var child in children)
            {
                child.GetLeafs(bounds, ref leafs);
            }
        }

        public void GetIntersectingOctants(Vector3D p0, Vector3D p1, ref List<Octant> octants)
        {
            var lineBounds = new BoundingBoxD();
            lineBounds.Min = new Vector3D(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Min(p0.Z, p1.Z));
            lineBounds.Max = new Vector3D(Math.Max(p0.X, p1.X), Math.Max(p0.Y, p1.Y), Math.Max(p0.Z, p1.Z));

            if (!bounds.Intersects(lineBounds))
            {
                return;
            }

            if (IsLeaf)
            {
                octants.Add(this);
                return;
            }

            foreach (var child in children)
            {
                child.GetIntersectingOctants(p0, p1, ref octants);
            }
        }

        public void GetAllOctants(ref List<Octant> octants)
        {
            octants.Add(this);
            foreach (var child in children)
            {
                child.GetAllOctants(ref octants);
            }
        }

        public List<Octant> GetNeighbors()
        {
            var neighbors = new List<Octant>();
            foreach (var direction in Utils.CardinalDirections)
            {
                var sibling = GetSibling(direction, this);
                if (sibling != null)
                {
                    var oppositeDirection = -direction;
                    sibling.GetEdgeLeafs(oppositeDirection, neighbors);
                }
            }
            return neighbors;
        }

        public Octant GetSibling(Vector3D direction, Octant source)
        {
            var center = source.bounds.Center + direction * source.bounds.Extents.X;
            if (Root.bounds.Contains(center) != ContainmentType.Contains)
            {
                return null;
            }

            var current = Root;
            while (current != null)
            {
                if (current.occupancy == OctantOccupancy.Full)
                {
                    break;
                }
                if (current.IsLeaf)
                {
                    return current;
                }
                //approximately same size
                if (Math.Abs(current.size - source.size) < 0.01)
                { 
                    return current;
                }
                current = current.children.FirstOrDefault(c => c.bounds.Contains(center) == ContainmentType.Contains);
                if (current == null)
                {
                    break;
                }
            }
            return null;
        }

        public void GetEdgeLeafs(Vector3D direction, List<Octant> leafs)
        {
            if (IsLeaf)
            {
                leafs.Add(this);
                return;
            }
            foreach (var child in children)
            {
                if (child.occupancy == OctantOccupancy.Full)
                {
                    continue;
                }
                var dirToChild = child.bounds.Center - bounds.Center;
                if (dirToChild.Dot(direction) < 0)
                {
                    continue;
                }
                child.GetEdgeLeafs(direction, leafs);
            }
        }

        public void Render()
        {
            var allOctants = new List<Octant>();
            GetAllOctants(ref allOctants);

            bool showNonLeaf = PathfinderSettings.Instance.NonLeafSetting.ShouldShow;

            foreach (var octant in allOctants)
            {
                if (!octant.IsLeaf && !showNonLeaf)
                {
                    continue;
                }

                if (octant.occupancy == OctantOccupancy.Full)
                {
                    var setting = PathfinderSettings.Instance.OccupiedSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.state == OctantState.Open && !octant.isReverse)
                {
                    var setting = PathfinderSettings.Instance.OpenSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.state == OctantState.Closed && !octant.isReverse)
                {
                    var setting = PathfinderSettings.Instance.ClosedSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.state == OctantState.Open && octant.isReverse)
                {
                    var setting = PathfinderSettings.Instance.OpenReverseSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.state == OctantState.Closed && octant.isReverse)
                {
                    var setting = PathfinderSettings.Instance.ClosedReverseSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.state == OctantState.Unexplored)
                {
                    var setting = PathfinderSettings.Instance.UnexploredSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }

                if (octant.isMeet)
                {
                    var setting = PathfinderSettings.Instance.MeetSetting;
                    if (setting.ShouldShow)
                    {
                        Utils.DrawAabb(octant.bounds, setting.Color, setting.Wireframe, 1f, setting.LineThickness);
                    }
                }
            }
        }
        #endregion
    }
}
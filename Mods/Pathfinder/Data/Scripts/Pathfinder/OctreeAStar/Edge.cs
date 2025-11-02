using VRageMath;
using System;

namespace Pathfinder.OctreeAStar
{
    public class Edge
    {
        #region Fields
        public Octant start;
        public Octant end;
        public Vector3D center;
        public double size;
        public double g;
        #endregion

        #region Constructors
        public Edge(Octant start, Octant end)
        {
            this.start = start;
            this.end = end;
            var smallest = start.size < end.size ? start : end;
            var largest = GetOther(smallest);
            var halfSize = smallest.size / 2;
            var optimalCardinalDirection = Utils.FirstOptimalCardinalDirection(smallest.bounds.Center, largest.bounds.Center);
            var optimalCardinalDirectionVector = optimalCardinalDirection * halfSize;
            center = smallest.bounds.Center + optimalCardinalDirectionVector;
            size = smallest.size;
        }
        #endregion

        #region Methods
        public Octant GetOther(Octant octant)
        {
            return start == octant ? end : start;
        }
        #endregion
    }
}
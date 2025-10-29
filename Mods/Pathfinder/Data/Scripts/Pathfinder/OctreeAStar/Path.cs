using System.Collections.Generic;
using VRageMath;

namespace Pathfinder.OctreeAStar
{
    public class Path
    {
        public enum State
        {
            NotStarted,
            Calculating,
            Full,
            Partial,
            NoPath
        }
        public State state = State.NotStarted;
        public List<Vector3D> points = new List<Vector3D>();

        public void Render()
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                Utils.DrawLine(
                    start, 
                    end, 
                    Color.Green, 
                    0.02f
                );
            }

            foreach (var point in points)
            {
                Utils.DrawSphere(point, Color.Red, 0.5f, false);
            }
        }
    }
}
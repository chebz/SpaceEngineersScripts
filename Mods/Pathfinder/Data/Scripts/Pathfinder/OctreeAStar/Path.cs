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
            Ready,
            NoPath
        }
        public State state = State.NotStarted;
        public List<Vector3D> points = new List<Vector3D>();

        public void Render()
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                Utils.DrawLine(start, end, Color.Cyan, 0.2f);
            }

            foreach (var point in points)
            {
                Utils.DrawSphere(point, Color.Red, 0.5f, false);
            }
        }

        public override string ToString()
        {
            if (points == null || points.Count == 0 || state != State.Ready)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", p.X, p.Y, p.Z);
            }
            return sb.ToString();
        }
    }
}
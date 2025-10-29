namespace Pathfinder.OctreeAStar
{
    public class Edge
    {
        public Octant Start { get; set; }
        public Octant End { get; set; }
        public double GCost { get; set; }
    }
}
using VRageMath;
using VRage.Game.ModAPI;
using System.Collections.Generic;

namespace Pathfinder.OctreeAStar
{
    public class Graph
    {
        public const int MAX_NODES_PER_FRAME = 1;
        private List<Octant> _open = new List<Octant>();
        private List<Octant> _closed = new List<Octant>();
        private double _rootSize;
        private Vector3D _start;
        private Vector3D _end;
        private IMyCubeGrid _ownGrid;
        private double _agentSize;
        private Path _path;
        private int _nodesProcessed;

        public Vector3D Start => _start;
        public Vector3D End => _end;
        public IMyCubeGrid OwnGrid => _ownGrid;
        public double AgentSize => _agentSize;

        public void BeginFindPath(IMyCubeGrid ownGrid, Vector3D start, Vector3D end)
        {
            _ownGrid = ownGrid;
            _agentSize = ownGrid.WorldVolume.Radius;
            _start = start;
            _end = end;
            _path = new Path();
            _rootSize = Vector3D.Distance(start, end);
            _open.Clear();
            _closed.Clear();
            var center = (start + end) / 2;
            var bb = new BoundingBoxD(
                center - _rootSize, 
                center + _rootSize);
            var octant = new Octant(bb, this, null);
            _open.Add(octant);
            _path.state = Path.State.Calculating;
            _nodesProcessed = 0;
        }

        public void Update()
        {
            if (_path.state != Path.State.Calculating)
            {
                return;
            }

            while (_open.Count > 0 && _nodesProcessed < MAX_NODES_PER_FRAME)
            {
                double lowestFCost = double.MaxValue;
                Octant current = null;
                foreach (var octant in _open)
                {
                    if (octant.F < lowestFCost)
                    {
                        lowestFCost = octant.F;
                        current = octant;
                    }
                }
                _open.Remove(current);
                if (current.State == Octant.OctantState.Unexplored)
                {
                    current.Explore();
                    switch (current.State)
                    {
                        case Octant.OctantState.FullObstacle:
                            continue;
                        case Octant.OctantState.PartialObstacle:
                            var bestChild = current.GetBestChild(_start);
                            if (bestChild != null)
                            {
                                _open.Add(bestChild);
                            }
                            continue;
                        case Octant.OctantState.Empty:
                            break;
                    }
                }
                _closed.Add(current);
                _nodesProcessed++;
            }
        }

        public void Render()
        {
            foreach (var octant in _open)
            {
                octant.Render();
            }
        }
    }
}
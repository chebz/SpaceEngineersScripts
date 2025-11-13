using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Pathfinder;

namespace Pathfinder.OctreeAStar
{
    public class Graph
    {
        #region Fields
        public double maxRootSize = 2000;
        public int maxNodesPerFrame = 1;
        public double gFactor = 0.1;
        public double exploreCostFactor = 0.75;
        public double exploreCostFactorInTerrain = 10.0;
        public double altitudeCostFactor = 1.0;
        public int maxPathOptimizationSteps = 100;
        private bool _endFound;
        private bool _isReverse;
        private int _currentExplorationCount;
        private readonly Random _random = new Random();
        private double _minClosedGForward;
        private double _minClosedGReverse;
        private readonly List<Vector3D> _collinearBuffer = new List<Vector3D>();
        private readonly List<Vector3D> _offsetBuffer = new List<Vector3D>();
        private readonly List<SegmentPair> _segmentBuffer = new List<SegmentPair>();
        private readonly List<Octant> _octantBuffer = new List<Octant>();
        private bool _useDynamicObstacles;
        private List<MyOrientedBoundingBoxD> _dynamicObstacles;

        #region Debug
        private int _exploreCount;
        #endregion

        private static readonly Vector3D[] StartOffsetDirections = new[]
        {
            Vector3D.Zero,
            Vector3D.UnitX,
            -Vector3D.UnitX,
            Vector3D.UnitY,
            -Vector3D.UnitY,
            Vector3D.UnitZ,
            -Vector3D.UnitZ
        };

        private const double StartOffsetFactor = 0.25;
        private int _currentStartOffsetIndex;
        private Vector3D _originalStart;
        private Vector3D _currentStart;

        private struct PathCandidate
        {
            public int Index;
            public Octant Octant;

            public PathCandidate(int index, Octant octant)
            {
                Index = index;
                Octant = octant;
            }
        }

        private struct SegmentPair
        {
            public Vector3D Start;
            public Vector3D End;

            public SegmentPair(Vector3D start, Vector3D end)
            {
                Start = start;
                End = end;
            }
        }
        #endregion

        #region Properties
        public Octant Root { get; private set; }

        public Vector3D Start { get; private set; }

        public Vector3D End { get; private set; }

        public IMyRemoteControl RemoteControl { get; private set; }

        public IMyCubeGrid OwnGrid { get; private set; }

        public double AgentSize { get; private set; }

        public double MinAltitude { get; private set; }

        public double MaxAltitude { get; private set; }

        public Path Path { get; private set; }

        public List<Octant> Open { get; } = new List<Octant>();

        public List<Octant> OpenReverse { get; } = new List<Octant>();

        public HashSet<Octant> Closed { get; } = new HashSet<Octant>();

        public HashSet<Octant> ClosedReverse { get; } = new HashSet<Octant>();

        public HashSet<Octant> MeetPoints { get; } = new HashSet<Octant>();

        public int NodesProcessed { get; private set; }

        public int StepCount { get; private set; }

        public int ExploreCount { get; private set; }
        #endregion

        #region Methods
        public void IncrementExploreCount()
        {
            ExploreCount++;
        }

        public bool UseDynamicObstacles => _useDynamicObstacles && _dynamicObstacles != null && _dynamicObstacles.Count > 0;

        public List<MyOrientedBoundingBoxD> DynamicObstacles => _dynamicObstacles;

        public void BeginFindPath(IMyRemoteControl remoteControl, Vector3D start, Vector3D end, double minAltitude, double maxAltitude, bool enableDynamicPathRefinement = false, List<MyOrientedBoundingBoxD> dynamicObstacles = null)
        {
            RemoteControl = remoteControl;
            OwnGrid = remoteControl.CubeGrid;
            AgentSize = OwnGrid.WorldVolume.Radius * 2;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            _useDynamicObstacles = enableDynamicPathRefinement;
            _dynamicObstacles = enableDynamicPathRefinement && dynamicObstacles != null
                ? new List<MyOrientedBoundingBoxD>(dynamicObstacles)
                : null;

            var settings = OctreeAStarSettings.Instance;
            var configuredMaxRootSize = enableDynamicPathRefinement ? settings.MaxDPRRootSize : settings.MaxRootSize;
            var configuredMinRootSize = enableDynamicPathRefinement ? settings.MinDPRRootSize : settings.MinRootSize;
            _originalStart = start;
            Start = start;
            End = end;
            _currentStartOffsetIndex = 0;
            _endFound = false;

            var distance = Vector3D.Distance(start, end);
            var scaledDistance = distance / 10.0;
            var effectiveMinRootSize = configuredMinRootSize > 0 ? configuredMinRootSize : 0.0;
            var effectiveMaxRootSize = configuredMaxRootSize > 0 ? configuredMaxRootSize : double.MaxValue;
            var desiredRootSize = Math.Max(effectiveMinRootSize, scaledDistance);
            desiredRootSize = Math.Max(desiredRootSize, AgentSize);
            maxRootSize = Math.Min(effectiveMaxRootSize, desiredRootSize);

            

            BeginFindPathInternal();
        }

        public void Reset()
        {
            Root = null;
            Path = null;
            Open.Clear();
            OpenReverse.Clear();
            Closed.Clear();
            ClosedReverse.Clear();
            MeetPoints.Clear();
            NodesProcessed = 0;
            StepCount = 0;
            ExploreCount = 0;
            _currentExplorationCount = 0;
            _isReverse = false;
            _useDynamicObstacles = false;
            _dynamicObstacles = null;
        }

        private void BeginFindPathInternal()
        {
            Octant.octantCount = 0;

            NodesProcessed = 0;
            StepCount = 0;
            ExploreCount = 0;
            _currentExplorationCount = 0;
            _isReverse = false;
            _minClosedGForward = double.MaxValue;
            _minClosedGReverse = double.MaxValue;
            _exploreCount = 0;

            Open.Clear();
            OpenReverse.Clear();
            Closed.Clear();
            ClosedReverse.Clear();
            MeetPoints.Clear();

            Path = new Path
            {
                state = Path.State.Calculating
            };

            var offsetDirection = StartOffsetDirections[_currentStartOffsetIndex];
            var offsetDistance = AgentSize * StartOffsetFactor;
            var offset = offsetDirection * offsetDistance;

            _currentStart = _originalStart + offset;
            Start = _currentStart;

            var halfSize = AgentSize / 2.0;
            var min = _currentStart - new Vector3D(halfSize, halfSize, halfSize);
            var max = _currentStart + new Vector3D(halfSize, halfSize, halfSize);
            var bounds = new BoundingBoxD(min, max);

            Root = new Octant(bounds, this, null)
            {
                state = Octant.OctantState.Open,
                occupancy = Octant.OctantOccupancy.Empty
            };

            Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"Max Root size: {maxRootSize}");

            // try to find the closest bounds to End that is Empty
            if (!TryFindEnd())
            {
                Utils.ShowHudMessage("No path found, end point is not reachable");
                Path.state = Path.State.NoPath;
                return;
            }

            var minimumDistance = AgentSize > 0 ? AgentSize * 2.0 : 0.0;
            if (minimumDistance > 0.0 && Vector3D.Distance(Start, End) < minimumDistance)
            {
                NodesProcessed = 0;
                StepCount = 0;
                ExploreCount = 0;
                _currentExplorationCount = 0;
                _isReverse = false;

                Open.Clear();
                OpenReverse.Clear();
                Closed.Clear();
                ClosedReverse.Clear();
                MeetPoints.Clear();
                Root = null;

                Path = new Path
                {
                    points = new List<Vector3D> { Start, End },
                    state = Path.State.Ready
                };

                return;
            }

            // find the closest large octant center behind Start
            {
                var dir = (Start - End).Normalized();
                var largeOctantCenter = Start + dir * maxRootSize / 2;
                ExpandToEnvelopPoint(largeOctantCenter);
            }
            if (Root.bounds.Contains(End) != ContainmentType.Contains)
            {
                // find the closest large octant center behind End
                var dir = (End - Start).Normalized();
                var largeOctantCenter = End + dir * maxRootSize / 2;
                ExpandToEnvelopPoint(largeOctantCenter);
            }

            SubdividePoint(Start);
            SubdividePoint(End);

            var startOctant = Root.GetClosestLeaf(Start);
            if (startOctant != null)
            {
                startOctant.state = Octant.OctantState.Open;
                startOctant.g = 0;
                startOctant.h = -1;
                startOctant.from = null;
                startOctant.isReverse = false;
                Open.Add(startOctant);
            }

            var endOctant = Root.GetClosestLeaf(End);
            if (endOctant != null)
            {
                endOctant.state = Octant.OctantState.Open;
                endOctant.g = 0;
                endOctant.h = -1;
                endOctant.from = null;
                endOctant.isReverse = true;
                OpenReverse.Add(endOctant);
            }
        }

        private bool TryAdvanceStartOffset()
        {
            if (_currentStartOffsetIndex + 1 >= StartOffsetDirections.Length)
            {
                return false;
            }

            _currentStartOffsetIndex++;
            var offsetDirection = StartOffsetDirections[_currentStartOffsetIndex];
            var offsetDistance = AgentSize * StartOffsetFactor;
            var offset = offsetDirection * offsetDistance;
                Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"Retrying path with start offset {_currentStartOffsetIndex}: ({offset.X:F2}, {offset.Y:F2}, {offset.Z:F2})");
            BeginFindPathInternal();
            return true;
        }

        public void Update()
        {
            if (Path == null)
            {
                Utils.ShowHudMessage("No path to find");
                return;
            }
            if (Path.state != Path.State.Calculating)
            {
                Utils.ShowHudMessage("Path is not calculating");
                return;
            }

            var settings = OctreeAStarSettings.Instance;
            if (settings != null)
            {
                maxNodesPerFrame = settings.MaxNodesPerFrame;
                exploreCostFactor = settings.ExploreCostFactor;
                exploreCostFactorInTerrain = settings.ExploreCostFactorInTerrain;
                altitudeCostFactor = settings.AltitudeCostFactor;
                maxPathOptimizationSteps = settings.MaxPathOptimizationSteps;
            }

            StepCount++;
            NodesProcessed = 0;

            var current = MeetPoints.FirstOrDefault();
            if (current != null)
            {
                _isReverse = current.isReverse;
            }
            else
            {
                if (Open.Count > 0 && OpenReverse.Count > 0)
                {
                    double weightForward = OpenReverse.Count;
                    double weightReverse = Open.Count;
                    var totalWeight = weightForward + weightReverse;
                    var randomValue = _random.NextDouble() * totalWeight;
                    _isReverse = randomValue >= weightForward;
                }
                else
                {
                    if (TryAdvanceStartOffset())
                    {
                        return;
                    }

                    Path.state = Path.State.NoPath;
                    Utils.ShowHudMessage("No path found");
                    return;
                }
            }

            var open = _isReverse ? OpenReverse : Open;
            var goal = _isReverse ? Start : End;

            var nodesPerFrame = maxNodesPerFrame;
            if (nodesPerFrame <= 0) 
            {
                nodesPerFrame = 1;
            }

            while (NodesProcessed < nodesPerFrame)
            {
                if (open.Count == 0)
                {
                    if (TryAdvanceStartOffset())
                    {
                        return;
                    }

                    Path.state = Path.State.NoPath;
                    Utils.ShowHudMessage("No path found");
                    return;
                }

                var lowestOpenFCost = double.MaxValue;

                if (current == null)
                {
                    foreach (var octant in open)
                    {
                        if (octant.h == -1)
                        {
                            octant.h = Vector3D.Distance(octant.bounds.Center, goal);
                        }
                        if (octant.F < lowestOpenFCost)
                        {
                            lowestOpenFCost = octant.F;
                            current = octant;
                        }
                    }

                    var expandFCost = CalculateExpandFCost();

                    if (expandFCost < lowestOpenFCost && Root.size / 2 < maxRootSize)
                    {
                        ExpandTowards(goal);
                        continue;
                    }
                }

                Open.Remove(current);
                OpenReverse.Remove(current);
                MeetPoints.Remove(current);
                current.state = Octant.OctantState.Closed;

                var closed = _isReverse ? ClosedReverse : Closed;
                closed.Add(current);
                if (_isReverse)
                {
                    if (current.g < _minClosedGReverse)
                    {
                        _minClosedGReverse = current.g;
                    }
                }
                else
                {
                    if (current.g < _minClosedGForward)
                    {
                        _minClosedGForward = current.g;
                    }
                }

                if (current.occupancy == Octant.OctantOccupancy.Unknown)
                {
                    current.Explore();
                    _exploreCount++;
                }

                if (current.occupancy != Octant.OctantOccupancy.Empty)
                {
                    NodesProcessed++;
                    continue;
                }

                if (!_isReverse && current.bounds.Contains(End) == ContainmentType.Contains)
                {
                    Path.state = Path.State.Ready;
                    ReconstructPath(current);
                    return;
                }

                var neighbors = current.GetNeighbors();
                foreach (var neighbor in neighbors)
                {
                    neighbor.SubdivideToMaxSize();
                }
                neighbors = current.GetNeighbors();


                foreach (var neighbor in neighbors)
                {
                    var other = neighbor;
                    if (other.state == Octant.OctantState.Unexplored)
                    {
                        other.g = current.g + Vector3D.Distance(other.bounds.Center, current.bounds.Center);
                        other.from = current;
                        other.isReverse = _isReverse;
                        open.Add(other);
                        other.state = Octant.OctantState.Open;
                    }
                    else if (other.isReverse != _isReverse)
                    {
                        if (other.occupancy == Octant.OctantOccupancy.Empty)
                        {
                            Path.state = Path.State.Ready;
                            if (_isReverse)
                            {
                                ReconstructBidirectionalPath(other, current);
                            }
                            else
                            {
                                ReconstructBidirectionalPath(current, other);
                            }
                            return;
                        }
                        if (other.occupancy == Octant.OctantOccupancy.Unknown || other.occupancy == Octant.OctantOccupancy.PartialInTerrain)
                        {
                            other.isMeet = true;
                            MeetPoints.Add(other);
                        }
                    }
                }
                NodesProcessed++;
            }
            // Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"NodesProcessed: {NodesProcessed}");
            // Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"Closed: {Closed.Count}");
            // Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"ClosedReverse: {ClosedReverse.Count}");
        }

        private double CalculateExpandFCost()
        {
            var direction = Utils.FirstOptimalCardinalDirection(Root.bounds.Center, End);
            var virtualCenter = Root.bounds.Center + direction * Root.size;

            var minGToRoot = _isReverse ? _minClosedGReverse : _minClosedGForward;
            if (minGToRoot == double.MaxValue)
            {
                minGToRoot = 0;
            }

            var virtualG = minGToRoot + Root.size;
            var virtualH = Vector3D.Distance(virtualCenter, End);
            return virtualG * gFactor + virtualH;
        }

        private void ExpandToEnvelopPoint(Vector3D point)
        {
            while (Root.bounds.Contains(point) != ContainmentType.Contains)
            {
                ExpandTowards(point);
            }
        }

        private void ExpandTowards(Vector3D target)
        {
            var firstOptimalCardinalDirection = Utils.FirstOptimalCardinalDirection(Root.bounds.Center, target);
            var secondOptimalCardinalDirection = Utils.SecondOptimalCardinalDirection(Root.bounds.Center, target);
            var thirdOptimalCardinalDirection = Vector3D.Cross(firstOptimalCardinalDirection, secondOptimalCardinalDirection);
            var oldSize = Root.size;
            var halfOldSize = Root.size / 2;
            var center = Root.bounds.Center;
            var newCenter = center + (firstOptimalCardinalDirection + secondOptimalCardinalDirection + thirdOptimalCardinalDirection) * halfOldSize;
            var newSize = oldSize * 2;
            var newBounds = new BoundingBoxD(newCenter - new Vector3D(newSize, newSize, newSize) / 2, 
                                           newCenter + new Vector3D(newSize, newSize, newSize) / 2);
            var newOctant = new Octant(newBounds, this, null);
            newOctant.children.Add(Root);
            Root.parent = newOctant;
            Root = newOctant;

            var size = Root.size / 4;
            var center1 = center + firstOptimalCardinalDirection * oldSize;
            var bounds1 = new BoundingBoxD(center1 - new Vector3D(size, size, size), 
                                          center1 + new Vector3D(size, size, size));
            var octant1 = new Octant(bounds1, this, Root);
            Root.children.Add(octant1);

            var center2 = center + secondOptimalCardinalDirection * oldSize;
            var bounds2 = new BoundingBoxD(center2 - new Vector3D(size, size, size), 
                                          center2 + new Vector3D(size, size, size));
            var octant2 = new Octant(bounds2, this, Root);
            Root.children.Add(octant2);

            var center3 = center + thirdOptimalCardinalDirection * oldSize;
            var bounds3 = new BoundingBoxD(center3 - new Vector3D(size, size, size), 
                                          center3 + new Vector3D(size, size, size));
            var octant3 = new Octant(bounds3, this, Root);
            Root.children.Add(octant3);

            var center4 = center + (firstOptimalCardinalDirection + secondOptimalCardinalDirection) * oldSize;
            var bounds4 = new BoundingBoxD(center4 - new Vector3D(size, size, size), 
                                          center4 + new Vector3D(size, size, size));
            var octant4 = new Octant(bounds4, this, Root);
            Root.children.Add(octant4);

            var center5 = center + (firstOptimalCardinalDirection + thirdOptimalCardinalDirection) * oldSize;
            var bounds5 = new BoundingBoxD(center5 - new Vector3D(size, size, size), 
                                          center5 + new Vector3D(size, size, size));
            var octant5 = new Octant(bounds5, this, Root);
            Root.children.Add(octant5);

            var center6 = center + (secondOptimalCardinalDirection + thirdOptimalCardinalDirection) * oldSize;
            var bounds6 = new BoundingBoxD(center6 - new Vector3D(size, size, size), 
                                          center6 + new Vector3D(size, size, size));
            var octant6 = new Octant(bounds6, this, Root);
            Root.children.Add(octant6);

            var center7 = center + (firstOptimalCardinalDirection + secondOptimalCardinalDirection + thirdOptimalCardinalDirection) * oldSize;
            var bounds7 = new BoundingBoxD(center7 - new Vector3D(size, size, size), 
                                          center7 + new Vector3D(size, size, size));
            var octant7 = new Octant(bounds7, this, Root);
            Root.children.Add(octant7);
        }

        public bool TryFindEnd()
        {
            // find the closest bounds to End that is Empty
            // center
            var fullSize = new Vector3D(AgentSize, AgentSize, AgentSize);
            var halfSize = fullSize * 0.5;

            var offsets = new Vector3D[]
            {
                Vector3D.Zero,
                new Vector3D(-AgentSize, 0, 0),
                new Vector3D(AgentSize, 0, 0),
                new Vector3D(0, -AgentSize, 0),
                new Vector3D(0, AgentSize, 0),
                new Vector3D(-AgentSize, -AgentSize, 0),
                new Vector3D(-AgentSize, AgentSize, 0),
                new Vector3D(AgentSize, -AgentSize, 0),
                new Vector3D(AgentSize, AgentSize, 0)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                var center = End + offsets[i];
                var bounds = new BoundingBoxD(center - halfSize, center + halfSize);
                var occupancy = Utils.GetOccupancy(bounds, OwnGrid, UseDynamicObstacles, _dynamicObstacles);
                if (occupancy == Octant.OctantOccupancy.Empty)
                {
                    End = center;
                    return true;
                }
            }

            return false;
        }

        private void SubdividePoint(Vector3D point)
        {
            var octant = Root.GetClosestLeaf(point);
            while (octant != null && octant.Subdivide())
            {
                octant = octant.GetClosestLeaf(point);
            }
        }

        private void ReconstructPath(Octant goal)
        {
            Path.points.Clear();
            var current = goal;
            while (current != null)
            {
                Path.points.Add(current.bounds.Center);
                current = current.from;
            }
            Path.points.Reverse();
            ReducePath();
        }

        private void ReconstructBidirectionalPath(Octant meetFromStart, Octant meetFromEnd)
        {
            Path.points.Clear();

            var pathFromStart = new List<Vector3D>();
            var current = meetFromStart;
            while (current != null && !current.isReverse)
            {
                pathFromStart.Add(current.bounds.Center);
                current = current.from;
            }
            pathFromStart.Reverse();
            Path.points.AddRange(pathFromStart);

            var pathFromEnd = new List<Vector3D>();
            current = meetFromEnd;
            while (current != null && current.isReverse)
            {
                pathFromEnd.Add(current.bounds.Center);
                current = current.from;
            }
            Path.points.AddRange(pathFromEnd);
            ReducePath();

            // debug
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"explore count: {_exploreCount}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Open count: {Open.Count}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"OpenReverse count: {OpenReverse.Count}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Closed count: {Closed.Count}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"ClosedReverse count: {ClosedReverse.Count}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"MeetPoints count: {MeetPoints.Count}");
            // MyAPIGateway.Utilities.ShowMessage("Pathfinder", $"Root size: {Root.size}");
            // Utils.PrintCallFrames();
        }

        private void ReducePath()
        {
            RemoveCollinearPoints();
            RemoveIntermediatePoints();
        }

        public void RemoveCollinearPoints()
        {
            if (Path == null || Path.points == null || Path.points.Count < 3)
            {
                return;
            }

            var points = Path.points;
            var reducedPath = _collinearBuffer;
            reducedPath.Clear();
            reducedPath.Add(points[0]);

            const double tolerance = 1e-4;

            for (var i = 1; i < points.Count - 1; i++)
            {
                var prev = points[i - 1];
                var current = points[i];
                var next = points[i + 1];

                var dir1 = current - prev;
                var dir2 = next - current;

                var cross = Vector3D.Cross(dir1, dir2);
                var crossLenSq = cross.LengthSquared();
                var lengthProductSq = dir1.LengthSquared() * dir2.LengthSquared();

                if (lengthProductSq <= double.Epsilon || crossLenSq > tolerance * lengthProductSq)
                {
                    reducedPath.Add(current);
                }
            }

            reducedPath.Add(points[points.Count - 1]);

            points.Clear();
            points.AddRange(reducedPath);
            reducedPath.Clear();
        }

        public void RemoveIntermediatePoints()
        {
            if (Path == null || Path.points == null || Path.points.Count < 3)
            {
                return;
            }

            var settings = OctreeAStarSettings.Instance;
            var maxOptimizationSteps = maxPathOptimizationSteps;
            if (settings != null)
            {
                maxOptimizationSteps = settings.MaxPathOptimizationSteps;
            }

            _currentExplorationCount = 0;
            var workingPoints = new List<Vector3D>(Path.points);
            var originalCount = workingPoints.Count;

            while (_currentExplorationCount < maxOptimizationSteps)
            {
                if (workingPoints.Count < 3)
                {
                    break;
                }

                var candidates = new List<PathCandidate>(workingPoints.Count);
                for (var i = 1; i < workingPoints.Count - 1; i++)
                {
                    var octant = Root != null ? Root.GetClosestLeaf(workingPoints[i]) : null;
                    candidates.Add(new PathCandidate(i, octant));
                }

                if (candidates.Count == 0)
                {
                    break;
                }

                candidates.Sort((a, b) =>
                {
                    var sizeA = a.Octant != null ? a.Octant.size : 0.0;
                    var sizeB = b.Octant != null ? b.Octant.size : 0.0;
                    var sizeComparison = sizeB.CompareTo(sizeA);
                    return sizeComparison != 0 ? sizeComparison : a.Index.CompareTo(b.Index);
                });

                var removedInPass = false;

                for (var i = 0; i < candidates.Count && _currentExplorationCount < maxOptimizationSteps; i++)
                {
                    var candidate = candidates[i];
                    var index = candidate.Index;

                    if (index <= 0 || index >= workingPoints.Count - 1)
                    {
                        continue;
                    }

                    var previous = workingPoints[index - 1];
                    var next = workingPoints[index + 1];

                    if (!HasClearPath(previous, next, maxOptimizationSteps))
                    {
                        continue;
                    }

                    Path.points.RemoveAt(index);
                    workingPoints.RemoveAt(index);
                    removedInPass = true;
                    break;
                }

                if (!removedInPass)
                {
                    Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"No intermediate points removed");
                    break;
                }
            }
            Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"Optimization steps performed: {_currentExplorationCount}");
            Utils.Log(MyLogSeverity.Info, "Pathfinder: {0}", $"Intermediate points removed: {originalCount - Path.points.Count}");
        }

        private bool HasClearPath(Vector3D p0, Vector3D p1, int maxOptimizationSteps)
        {
            var segment = p1 - p0;
            var segmentLength = segment.Length();
            if (segmentLength <= double.Epsilon)
            {
                return true;
            }

            var direction = segment / segmentLength;
            var reference = Math.Abs(Vector3D.Dot(direction, Vector3D.Up)) > 0.99 ? Vector3D.Forward : Vector3D.Up;
            var right = Vector3D.Cross(reference, direction);
            if (right.LengthSquared() > 0)
            {
                right.Normalize();
            }
            var up = Vector3D.Cross(direction, right);
            if (up.LengthSquared() > 0)
            {
                up.Normalize();
            }

            var offsets = _offsetBuffer;
            offsets.Clear();
            offsets.Add(Vector3D.Zero);
            if (AgentSize > double.Epsilon)
            {
                var halfExtent = AgentSize * 0.5;
                var rightOffset = right * halfExtent;
                var upOffset = up * halfExtent;

                offsets.Add(rightOffset + upOffset);
                offsets.Add(rightOffset - upOffset);
                offsets.Add(-rightOffset + upOffset);
                offsets.Add(-rightOffset - upOffset);
            }

            var segments = _segmentBuffer;
            segments.Clear();
            foreach (var offset in offsets)
            {
                segments.Add(new SegmentPair(p0 + offset, p1 + offset));
            }

            while (true)
            {
                var octants = _octantBuffer;
                octants.Clear();
                Root.GetIntersectingOctants(p0, p1, ref octants);

                var needsExploration = false;
                foreach (var octant in octants)
                {
                    if (!SegmentsIntersectBounds(segments, octant.bounds))
                    {
                        continue;
                    }

                    if (octant.occupancy == Octant.OctantOccupancy.Full)
                    {
                        octants.Clear();
                        segments.Clear();
                        offsets.Clear();
                        return false;
                    }

                    if (octant.occupancy == Octant.OctantOccupancy.Partial ||
                        octant.occupancy == Octant.OctantOccupancy.Unknown)
                    {
                        if (_currentExplorationCount >= maxOptimizationSteps)
                        {
                            octants.Clear();
                            segments.Clear();
                            offsets.Clear();
                            return false;
                        }

                        octant.Explore();
                        _exploreCount++;
                        needsExploration = true;
                        _currentExplorationCount++;
                        break;
                    }
                }

                if (!needsExploration)
                {
                    octants.Clear();
                    break;
                }
            }

            segments.Clear();
            offsets.Clear();
            return true;
        }

        private static bool SegmentsIntersectBounds(List<SegmentPair> segments, BoundingBoxD bounds)
        {
            foreach (var segment in segments)
            {
                if (SegmentIntersectsBounds(segment.Start, segment.End, bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsBounds(Vector3D start, Vector3D end, BoundingBoxD bounds)
        {
            if (bounds.Contains(start) != ContainmentType.Disjoint || bounds.Contains(end) != ContainmentType.Disjoint)
            {
                return true;
            }

            var direction = end - start;
            var length = direction.Length();
            if (length <= double.Epsilon)
            {
                return false;
            }
            direction /= length;

            var ray = new RayD(start, direction);
            var distance = bounds.Intersects(ray);
            if (distance.HasValue && distance.Value <= length)
            {
                return true;
            }

            var reverseRay = new RayD(end, -direction);
            distance = bounds.Intersects(reverseRay);
            if (distance.HasValue && distance.Value <= length)
            {
                return true;
            }

            return false;
        }

        public void Render()
        {
            if (Root != null && OctreeAStarSettings.Instance.RenderOctants)
            {
                Root.Render();
            }
            if (Path != null && OctreeAStarSettings.Instance.RenderPath)
            {
                Path.Render(Color.Cyan, true);
            }
            if (DynamicObstacles != null && DynamicObstacles.Count > 0)
            {
                foreach (var obstacle in DynamicObstacles)
                {
                    Utils.DrawOBB(obstacle, Color.Red, true);
                }
            }
        }
        #endregion
    }
}
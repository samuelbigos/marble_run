using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using UnityEngine;

namespace AStar
{
    // https://www.redblobgames.com/pathfinding/a-star/implementation.html
    // A* needs only a WeightedGraph and a location type L, and does *not*
    // have to be a grid. However, in the example code I am using a grid.
    public interface WeightedGraph<L>
    {
        double Cost(Location a, Location b);
        IEnumerable<Location> Neighbors(Location id);
    }

    public struct Location
    {
        public bool Equals(Location other)
        {
            return pos.Equals(other.pos);
        }

        public override bool Equals(object obj)
        {
            return obj is Location other && Equals(other);
        }

        public override int GetHashCode()
        {
            return pos.GetHashCode();
        }

        private readonly Vector3Int pos;
        public Vector3Int Pos => pos;
        
        public Location(Vector3Int p)
        {
            pos = p;
        }
    }

    public class Grid3D : WeightedGraph<Location>
    {
        private static readonly Location[] DIRS = new[]
        {
            new Location(new Vector3Int(1, 0, 0)),
            //new Location(new Vector3Int(0, 1, 0)), // don't allow paths to ascend
            new Location(new Vector3Int(0, 0, 1)),
            new Location(new Vector3Int(-1, 0, 0)),
            new Location(new Vector3Int(0, -1, 0)),
            new Location(new Vector3Int(0, 0, -1))
        };

        public HashSet<Location> Closed => _closed;

        private Vector3Int _size;
        private HashSet<Location> _closed = new HashSet<Location>();
        
        public Grid3D(Vector3Int size)
        {
            _size = size;
        }

        public void Ban(Location loc)
        {
            _closed.Add(loc);
        }

        public void Unban(Location loc)
        {
            _closed.Remove(loc);
        }

        private bool InBounds(Location id)
        {
            return 0 <= id.Pos.x && id.Pos.x < _size.x &&
                   0 <= id.Pos.y && id.Pos.y < _size.y && 
                   0 <= id.Pos.z && id.Pos.z < _size.z;
        }

        private bool Passable(Location id)
        {
            return !_closed.Contains(id);
        }

        public double Cost(Location a, Location b)
        {
            return 1.0;
        }

        public IEnumerable<Location> Neighbors(Location id)
        {
            foreach (Location dir in DIRS)
            {
                Location next = new Location(id.Pos + dir.Pos);
                if (InBounds(next) && Passable(next))
                {
                    yield return next;
                }
            }
        }
    }


    public class PriorityQueue<T>
    {
        // I'm using an unsorted array for this example, but ideally this
        // would be a binary heap. There's an open issue for adding a binary
        // heap to the standard C# library: https://github.com/dotnet/corefx/issues/574
        //
        // Until then, find a binary or d-ary heap class:
        // * https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2?view=net-6.0
        // * https://github.com/BlueRaja/High-Speed-Priority-Queue-for-C-Sharp
        // * http://visualstudiomagazine.com/articles/2012/11/01/priority-queues-with-c.aspx
        // * http://xfleury.github.io/graphsearch.html
        // * http://stackoverflow.com/questions/102398/priority-queue-in-net

        private List<Tuple<T, double>> elements = new List<Tuple<T, double>>();

        public int Count
        {
            get { return elements.Count; }
        }

        public void Enqueue(T item, double priority)
        {
            elements.Add(Tuple.Create(item, priority));
        }

        public T Dequeue()
        {
            int bestIndex = 0;

            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i].Item2 < elements[bestIndex].Item2)
                {
                    bestIndex = i;
                }
            }

            T bestItem = elements[bestIndex].Item1;
            elements.RemoveAt(bestIndex);
            return bestItem;
        }
    }

    public class AStarSearch
    {
        // Note: a generic version of A* would abstract over Location and
        // also Heuristic
        private static double Heuristic(Location a, Location b)
        {
            return (a.Pos - b.Pos).magnitude;
        }

        public List<Location> FindPath(WeightedGraph<Location> graph, Location start, Location goal)
        {
            List<Location> path = new List<Location>();
            Dictionary<Location, Location> cameFrom = new Dictionary<Location, Location>();
            Dictionary<Location, double> costSoFar = new Dictionary<Location, double>();
            PriorityQueue<Location> frontier = new PriorityQueue<Location>();
            
            frontier.Enqueue(start, 0);

            cameFrom[start] = start;
            costSoFar[start] = 0;

            while (frontier.Count > 0)
            {
                Location current = frontier.Dequeue();

                if (current.Equals(goal))
                {
                    break;
                }

                foreach (Location next in graph.Neighbors(current))
                {
                    // ignore neighbors that would lead to a steep gradient in the path
                    // (remember we are drawing a path from end to start, hence -2.
                    Location prev = cameFrom[current];
                    if (next.Pos.y - prev.Pos.y == -2)
                        continue;

                    double newCost = costSoFar[current] + graph.Cost(current, next);
                    
                    // if (cameFrom.ContainsKey(prev))
                    // {
                    //     Location pp = cameFrom[prev];
                    //     if (current.Pos.y - prev.Pos.y == -1)
                    //     {
                    //         Vector3Int dir = prev.Pos - pp.Pos;
                    //         if (next.Pos - current.Pos != dir)
                    //         {
                    //             newCost = costSoFar[current] + graph.Cost(current, next) * 100.0f;
                    //         }
                    //     }
                    // }
                    //
                    // if (cameFrom.ContainsKey(prev))
                    // {
                    //     Location pp = cameFrom[prev];
                    //     if (prev.Pos.y - pp.Pos.y == -1)
                    //     {
                    //         Vector3Int dir = current.Pos - prev.Pos;
                    //         if (next.Pos - current.Pos != dir)
                    //         {
                    //             newCost = costSoFar[current] + graph.Cost(current, next) * 100.0f;
                    //         }
                    //     }
                    // }

                    if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                    {
                        costSoFar[next] = newCost;
                        double priority = newCost + Heuristic(next, goal);
                        frontier.Enqueue(next, priority);
                        cameFrom[next] = current;
                    }
                }
            }

            if (!cameFrom.ContainsKey(goal))
                return null;

            Location loc = goal;
            while (true)
            {
                path.Add(loc);
                loc = cameFrom[loc];
                if (loc.Equals(start))
                {
                    path.Add(loc);
                    break;
                }
            }

            return path;
        }
    }
}
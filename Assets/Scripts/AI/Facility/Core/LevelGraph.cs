using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility
{
    /// <summary>
    /// A walkable location in the level: the centre of one 4 m floor tile.
    /// </summary>
    public sealed class GraphNode
    {
        public GraphNode(int id, float x, float z, string area)
        {
            Id = id;
            X = x;
            Z = z;
            Area = area ?? string.Empty;
        }

        public int Id { get; }
        public float X { get; }
        public float Z { get; }

        /// <summary>The room or corridor this tile belongs to, e.g. "MaintenanceBay".</summary>
        public string Area { get; }

        public override string ToString() => $"{Area} ({X}, {Z})";
    }

    /// <summary>
    /// An undirected connection between two neighbouring nodes.
    /// A door edge becomes impassable while its door is locked.
    /// </summary>
    public sealed class GraphEdge
    {
        public GraphEdge(int a, int b, float cost)
        {
            A = a;
            B = b;
            Cost = cost;
        }

        public int A { get; }
        public int B { get; }
        public float Cost { get; }

        /// <summary>Id of the door on this edge (e.g. "Door_LabSouth"), or null if there is no door.</summary>
        public string DoorId { get; internal set; }

        public bool IsDoor => DoorId != null;

        /// <summary>Returns the node at the other end of this edge.</summary>
        public int Other(int nodeId)
        {
            if (nodeId == A) return B;
            if (nodeId == B) return A;
            throw new ArgumentException($"Node {nodeId} is not part of this edge.", nameof(nodeId));
        }
    }

    /// <summary>
    /// Graph abstraction of the level used by the Facility AI.
    /// Pure C# (no UnityEngine dependency) so it can be unit tested in isolation.
    /// </summary>
    public sealed class LevelGraph
    {
        private readonly List<GraphNode> nodes = new List<GraphNode>();
        private readonly List<GraphEdge> edges = new List<GraphEdge>();
        private readonly List<List<GraphEdge>> adjacency = new List<List<GraphEdge>>();

        public IReadOnlyList<GraphNode> Nodes => nodes;
        public IReadOnlyList<GraphEdge> Edges => edges;
        public int NodeCount => nodes.Count;

        public GraphNode AddNode(float x, float z, string area = null)
        {
            var node = new GraphNode(nodes.Count, x, z, area);
            nodes.Add(node);
            adjacency.Add(new List<GraphEdge>());
            return node;
        }

        public GraphEdge AddEdge(int a, int b, float cost)
        {
            ValidateNode(a);
            ValidateNode(b);
            if (a == b) throw new ArgumentException("An edge must connect two different nodes.");
            if (cost <= 0f) throw new ArgumentOutOfRangeException(nameof(cost), "Edge cost must be positive.");

            var edge = new GraphEdge(a, b, cost);
            edges.Add(edge);
            adjacency[a].Add(edge);
            adjacency[b].Add(edge);
            return edge;
        }

        public IReadOnlyList<GraphEdge> EdgesOf(int nodeId)
        {
            ValidateNode(nodeId);
            return adjacency[nodeId];
        }

        /// <summary>All door ids that have been tagged on edges.</summary>
        public IEnumerable<string> DoorIds
        {
            get
            {
                foreach (var edge in edges)
                    if (edge.IsDoor) yield return edge.DoorId;
            }
        }

        /// <summary>
        /// Marks the edge whose midpoint is closest to (x, z) as a door, if it lies within the tolerance.
        /// Door markers sit exactly between two tiles, so the midpoint identifies the edge.
        /// </summary>
        public bool TagDoor(string doorId, float x, float z, float tolerance)
        {
            if (string.IsNullOrEmpty(doorId)) throw new ArgumentException("A door id is required.", nameof(doorId));

            GraphEdge best = null;
            float bestDistance = tolerance;
            foreach (var edge in edges)
            {
                var a = nodes[edge.A];
                var b = nodes[edge.B];
                float distance = Distance((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f, x, z);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = edge;
                }
            }

            if (best == null) return false;
            best.DoorId = doorId;
            return true;
        }

        /// <summary>The edge carrying the given door, or null if no edge has that door.</summary>
        public GraphEdge FindDoorEdge(string doorId)
        {
            foreach (var edge in edges)
                if (edge.DoorId == doorId) return edge;
            return null;
        }

        public static bool IsPassable(GraphEdge edge, ICollection<string> lockedDoors)
            => !edge.IsDoor || lockedDoors == null || !lockedDoors.Contains(edge.DoorId);

        /// <summary>
        /// Breadth-first search: can <paramref name="goal"/> be reached from <paramref name="start"/>
        /// with the given doors locked? Used by the "route to exit" and "robots can reach the player" constraints,
        /// where only reachability matters, so BFS is cheaper than a full shortest-path search.
        /// </summary>
        public bool IsReachable(int start, int goal, ICollection<string> lockedDoors = null)
        {
            ValidateNode(start);
            ValidateNode(goal);
            if (start == goal) return true;

            var visited = new bool[nodes.Count];
            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var edge in adjacency[current])
                {
                    if (!IsPassable(edge, lockedDoors)) continue;
                    int next = edge.Other(current);
                    if (visited[next]) continue;
                    if (next == goal) return true;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        /// <summary>Number of nodes reachable from <paramref name="start"/> (including itself) with the given doors locked.</summary>
        public int CountReachable(int start, ICollection<string> lockedDoors = null)
        {
            var distances = ShortestDistances(start, lockedDoors);
            int count = 0;
            foreach (float d in distances)
                if (!float.IsPositiveInfinity(d)) count++;
            return count;
        }

        /// <summary>
        /// Dijkstra's algorithm from <paramref name="start"/>. Unreachable nodes get float.PositiveInfinity.
        /// Uses the O(V²) array version: the level has 45 nodes, so it is fast enough, simple to verify,
        /// and needs no priority queue (not available in Unity's .NET Standard 2.1 profile).
        /// Used for the spawn "distance window" constraint, which needs real walking distance, not straight-line distance.
        /// </summary>
        public float[] ShortestDistances(int start, ICollection<string> lockedDoors = null)
        {
            ValidateNode(start);

            int count = nodes.Count;
            var distance = new float[count];
            var settled = new bool[count];
            for (int i = 0; i < count; i++) distance[i] = float.PositiveInfinity;
            distance[start] = 0f;

            for (int step = 0; step < count; step++)
            {
                int current = -1;
                float smallest = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!settled[i] && distance[i] < smallest)
                    {
                        smallest = distance[i];
                        current = i;
                    }
                }

                if (current == -1) break; // every remaining node is unreachable
                settled[current] = true;

                foreach (var edge in adjacency[current])
                {
                    if (!IsPassable(edge, lockedDoors)) continue;
                    int next = edge.Other(current);
                    float candidate = distance[current] + edge.Cost;
                    if (candidate < distance[next]) distance[next] = candidate;
                }
            }

            return distance;
        }

        /// <summary>Returns the id of the node closest to (x, z), or -1 if the graph is empty.</summary>
        public int FindNearestNode(float x, float z)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            foreach (var node in nodes)
            {
                float distance = Distance(node.X, node.Z, x, z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node.Id;
                }
            }

            return best;
        }

        private static float Distance(float x1, float z1, float x2, float z2)
        {
            float dx = x1 - x2;
            float dz = z1 - z2;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private void ValidateNode(int id)
        {
            if (id < 0 || id >= nodes.Count)
                throw new ArgumentOutOfRangeException(nameof(id), $"No node with id {id}.");
        }
    }
}

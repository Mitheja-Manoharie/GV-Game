using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility;
using NUnit.Framework;

namespace LabLockdown.AI.Flanker.Tests
{
    /// <summary>
    /// The Lab Lockdown level for tests, with a stand-in for Unity's line-of-sight raycasts: a straight line
    /// between two points sees through as long as it stays on floor tiles and only steps between neighbouring
    /// tiles. Walls in this layout are exactly the tile boundaries without a neighbour, so this matches the
    /// level closely enough for planning tests. Open doors can be seen through; locked (closed) doors cannot.
    /// </summary>
    internal static class TestLevel
    {
        public static LevelGraph Graph() => LabLockdownLayout.CreateGraph();

        /// <summary>The node whose tile centre is exactly (x, z).</summary>
        public static int Node(LevelGraph graph, float x, float z)
        {
            int node = graph.FindNearestNode(x, z);
            Assert.AreEqual(x, graph.Nodes[node].X, 0.01f, $"No tile centre at ({x}, {z})");
            Assert.AreEqual(z, graph.Nodes[node].Z, 0.01f, $"No tile centre at ({x}, {z})");
            return node;
        }

        public static HashSet<int> VisibleFrom(LevelGraph graph, float x, float z, ICollection<string> lockedDoors = null)
        {
            var tiles = new HashSet<(int, int)>();
            foreach (var node in graph.Nodes) tiles.Add(Cell(node.X, node.Z));

            // A locked door is a closed wall between its two tiles.
            var closed = new HashSet<((int, int), (int, int))>();
            foreach (var edge in graph.Edges)
            {
                if (!edge.IsDoor || lockedDoors == null || !lockedDoors.Contains(edge.DoorId)) continue;
                var a = Cell(graph.Nodes[edge.A].X, graph.Nodes[edge.A].Z);
                var b = Cell(graph.Nodes[edge.B].X, graph.Nodes[edge.B].Z);
                closed.Add((a, b));
                closed.Add((b, a));
            }

            var visible = new HashSet<int>();
            foreach (var node in graph.Nodes)
                if (LineOfSight(tiles, closed, x, z, node.X, node.Z)) visible.Add(node.Id);
            return visible;
        }

        /// <summary>Percepts for a flanker at <paramref name="agent"/> and a player at <paramref name="player"/> looking along <paramref name="facing"/>.</summary>
        public static FlankerPercepts Percepts(LevelGraph graph, (float x, float z) agent, (float x, float z) player,
            (float x, float z) facing, float health01 = 1f, params string[] lockedDoors)
        {
            return new FlankerPercepts
            {
                AgentNode = Node(graph, agent.x, agent.z),
                AgentHealth01 = health01,
                PlayerNode = Node(graph, player.x, player.z),
                PlayerX = player.x,
                PlayerZ = player.z,
                PlayerForwardX = facing.x,
                PlayerForwardZ = facing.z,
                VisibleNodes = VisibleFrom(graph, player.x, player.z, lockedDoors),
                LockedDoors = new HashSet<string>(lockedDoors),
            };
        }

        public static string Area(LevelGraph graph, int node) => graph.Nodes[node].Area;

        private static (int, int) Cell(float x, float z) =>
            ((int)MathF.Floor(x / LabLockdownLayout.TileSize), (int)MathF.Floor(z / LabLockdownLayout.TileSize));

        private static bool LineOfSight(HashSet<(int, int)> tiles, HashSet<((int, int), (int, int))> closed,
            float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            int steps = Math.Max(1, (int)(MathF.Sqrt(dx * dx + dz * dz) / 0.1f));
            var previous = Cell(x0, z0);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                var cell = Cell(x0 + dx * t, z0 + dz * t);
                if (cell == previous) continue;
                if (!tiles.Contains(cell) || closed.Contains((previous, cell))) return false;

                // Crossing a corner diagonally needs an open tile beside it, or the line squeezes through a wall corner.
                bool diagonal = cell.Item1 != previous.Item1 && cell.Item2 != previous.Item2;
                if (diagonal && !tiles.Contains((cell.Item1, previous.Item2)) && !tiles.Contains((previous.Item1, cell.Item2)))
                    return false;

                previous = cell;
            }

            return true;
        }
    }
}

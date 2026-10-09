using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility
{
    /// <summary>A floor tile centre and the area (room or corridor) it belongs to.</summary>
    public readonly struct TileInfo
    {
        public TileInfo(float x, float z, string area)
        {
            X = x;
            Z = z;
            Area = area;
        }

        public float X { get; }
        public float Z { get; }
        public string Area { get; }
    }

    /// <summary>A door position: the midpoint between the two tiles it separates.</summary>
    public readonly struct DoorInfo
    {
        public DoorInfo(string id, float x, float z)
        {
            Id = id;
            X = x;
            Z = z;
        }

        public string Id { get; }
        public float X { get; }
        public float Z { get; }
    }

    /// <summary>A candidate enemy spawn location.</summary>
    public readonly struct SpawnInfo
    {
        public SpawnInfo(string id, float x, float z)
        {
            Id = id;
            X = x;
            Z = z;
        }

        public string Id { get; }
        public float X { get; }
        public float Z { get; }
    }

    /// <summary>
    /// Builds a <see cref="LevelGraph"/> from a tile grid: every tile becomes a node, and tiles that are
    /// exactly one tile apart (north/south/east/west) are connected. On the Lab Lockdown layout every such
    /// neighbour pair is joined by an opening or a door, so no wall information is needed.
    /// </summary>
    public static class LevelGraphBuilder
    {
        public static LevelGraph Build(IEnumerable<TileInfo> tiles, IEnumerable<DoorInfo> doors, float tileSize)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            if (tileSize <= 0f) throw new ArgumentOutOfRangeException(nameof(tileSize));

            var graph = new LevelGraph();
            var byCell = new Dictionary<(int, int), int>();

            foreach (var tile in tiles)
            {
                var cell = ToCell(tile.X, tile.Z, tileSize);
                if (byCell.ContainsKey(cell))
                    throw new ArgumentException($"Duplicate tile at ({tile.X}, {tile.Z}).");

                byCell[cell] = graph.AddNode(tile.X, tile.Z, tile.Area).Id;
            }

            // Connect each tile to its east and north neighbour; the other two directions are
            // covered when the neighbour itself is visited, so every pair is added exactly once.
            foreach (var entry in byCell)
            {
                var (cx, cz) = entry.Key;
                if (byCell.TryGetValue((cx + 1, cz), out int east)) graph.AddEdge(entry.Value, east, tileSize);
                if (byCell.TryGetValue((cx, cz + 1), out int north)) graph.AddEdge(entry.Value, north, tileSize);
            }

            if (doors != null)
            {
                foreach (var door in doors)
                {
                    if (!graph.TagDoor(door.Id, door.X, door.Z, tileSize * 0.25f))
                        throw new ArgumentException($"Door {door.Id} at ({door.X}, {door.Z}) does not sit between two tiles.");
                }
            }

            return graph;
        }

        // Tile centres sit half a tile off the grid lines (…, -2, 2, 6, …), so x / tileSize is always
        // a half value (-0.5, 0.5, 1.5, …). Floor maps each to its own cell; Round would send both
        // -0.5 and 0.5 to cell 0 and merge neighbouring tiles.
        private static (int, int) ToCell(float x, float z, float tileSize)
            => ((int)MathF.Floor(x / tileSize), (int)MathF.Floor(z / tileSize));
    }
}

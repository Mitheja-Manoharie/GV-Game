using System.Collections.Generic;

namespace LabLockdown.AI.Facility
{
    /// <summary>
    /// The Lab Lockdown level as data: floor tile centres on the 4 m modular grid, and the five doors.
    /// Coordinates match the greybox and art pass in Assets/Scenes/Dev/Mitheja.unity.
    ///
    /// Why a hand-authored tile graph instead of Unity's NavMesh:
    /// the Facility AI only needs room-level structure (where can things go, through which doors),
    /// and its constraint checks run BFS/Dijkstra many times per decision. A 45-node graph keeps
    /// each check to microseconds, and lets the logic run outside Unity in unit tests.
    /// </summary>
    public static class LabLockdownLayout
    {
        public const float TileSize = 4f;

        public const string DoorArmouryNorth = "Door_ArmouryNorth";
        public const string DoorInfirmaryNorth = "Door_InfirmaryNorth";
        public const string DoorLabSouth = "Door_LabSouth";
        public const string DoorControlWest = "Door_ControlWest";
        public const string DoorControlEast = "Door_ControlEast";

        /// <summary>Default player start: south-west tile of the quarantine cell.</summary>
        public const float PlayerStartX = -2f, PlayerStartZ = -2f;

        /// <summary>Default exit: the control room tile in front of the window (ExitPoint is at 2, 30.5).</summary>
        public const float ExitX = 2f, ExitZ = 30f;

        public static IReadOnlyList<TileInfo> Tiles { get; } = CreateTiles();

        public static IReadOnlyList<DoorInfo> Doors { get; } = new[]
        {
            new DoorInfo(DoorArmouryNorth, 14f, 4f),
            new DoorInfo(DoorInfirmaryNorth, -14f, 20f),
            new DoorInfo(DoorLabSouth, 14f, 24f),
            new DoorInfo(DoorControlWest, -4f, 30f),
            new DoorInfo(DoorControlEast, 8f, 30f),
        };

        /// <summary>
        /// Areas where enemies must never spawn: the start area and the armoury safe room.
        /// The control room is also excluded from the defaults so the exit is never camped at spawn time.
        /// </summary>
        private static readonly HashSet<string> safeAreas = new HashSet<string>
        {
            "QuarantineCell", "QuarantineCorridor", "Armoury",
        };

        public static bool IsSafeArea(string area) => safeAreas.Contains(area);

        /// <summary>
        /// Default spawn candidates, spread over both routes to the exit so the Facility AI can
        /// flank the player from either side. SP_MaintenanceBay reuses the existing EnemySpawnPoint.
        /// </summary>
        public static IReadOnlyList<SpawnInfo> DefaultSpawnPoints { get; } = new[]
        {
            new SpawnInfo("SP_MaintenanceBay", 2f, 17f),
            new SpawnInfo("SP_Crossroads", 10f, 14f),
            new SpawnInfo("SP_ServiceCorridor", 14f, 10f),
            new SpawnInfo("SP_LabCorridor", 14f, 22f),
            new SpawnInfo("SP_ResearchLab", 18f, 30f),
            new SpawnInfo("SP_Infirmary", -14f, 14f),
            new SpawnInfo("SP_InfirmaryNorth", -14f, 26f),
            new SpawnInfo("SP_WestCorridor", -10f, 30f),
        };

        public static LevelGraph CreateGraph() => LevelGraphBuilder.Build(Tiles, Doors, TileSize);

        private static List<TileInfo> CreateTiles()
        {
            var tiles = new List<TileInfo>();

            void Area(string name, float[] xs, float[] zs)
            {
                foreach (float x in xs)
                    foreach (float z in zs)
                        tiles.Add(new TileInfo(x, z, name));
            }

            Area("QuarantineCell", new[] { -2f, 2f }, new[] { -2f, 2f });
            Area("QuarantineCorridor", new[] { 6f }, new[] { 2f });
            Area("Armoury", new[] { 10f, 14f }, new[] { -2f, 2f });
            Area("ServiceCorridor", new[] { 14f }, new[] { 6f, 10f });
            Area("Crossroads", new[] { 10f, 14f }, new[] { 14f });
            Area("LabCorridor", new[] { 14f }, new[] { 18f, 22f });
            Area("MaintenanceBay", new[] { -2f, 2f, 6f }, new[] { 10f, 14f, 18f });
            Area("InfirmaryCorridor", new[] { -6f }, new[] { 14f });
            Area("Infirmary", new[] { -14f, -10f }, new[] { 14f, 18f });
            Area("InfirmaryNorthCorridor", new[] { -14f }, new[] { 22f, 26f });
            Area("WestCorridor", new[] { -14f, -10f, -6f }, new[] { 30f });
            Area("ControlRoom", new[] { -2f, 2f, 6f }, new[] { 26f, 30f });
            Area("LabLink", new[] { 10f }, new[] { 30f });
            Area("ResearchLab", new[] { 14f, 18f }, new[] { 26f, 30f });

            return tiles;
        }
    }
}

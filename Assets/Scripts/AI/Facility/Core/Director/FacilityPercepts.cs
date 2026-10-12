using System.Collections.Generic;

namespace LabLockdown.AI.Facility.Director
{
    /// <summary>
    /// What the Facility AI senses at a decision (the "S" in PEAS). Filled in by the Unity runner from the
    /// scene; built by hand in unit tests.
    /// </summary>
    public sealed class FacilityPercepts
    {
        /// <summary>Game time in seconds.</summary>
        public double Time;

        /// <summary>Graph node the player is standing on.</summary>
        public int PlayerNode;

        /// <summary>Player health, 0 (dead) to 1 (full).</summary>
        public float Health01 = 1f;

        /// <summary>Health lost recently (e.g. over the last 5 seconds), 0 to 1.</summary>
        public float RecentDamage01;

        /// <summary>Seconds since the player last took damage or last had an enemy alive.</summary>
        public float CalmSeconds;

        public int EnemiesAlive;

        /// <summary>Spawn candidate nodes the player currently has line of sight to.</summary>
        public ICollection<int> VisibleNodes = new HashSet<int>();

        /// <summary>Doors that are locked right now (read from the door system, the source of truth).</summary>
        public ICollection<string> LockedDoors = new HashSet<string>();
    }
}

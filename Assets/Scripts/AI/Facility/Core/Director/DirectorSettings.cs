using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility.Director
{
    /// <summary>
    /// Every tunable number of the Facility AI in one place. [Serializable] with public fields so Unity shows
    /// it in the Inspector, while the class itself stays free of UnityEngine (pure C#, unit testable).
    /// </summary>
    [Serializable]
    public sealed class DirectorSettings
    {
        // ---------- Enemy budget ----------

        /// <summary>Hard cap on enemies alive at once (performance and fairness).</summary>
        public int MaxEnemiesAlive = 6;

        /// <summary>Most enemies spawned by one decision.</summary>
        public int MaxSpawnPerWave = 3;

        // ---------- Spawn distance window (walking distance in metres along the level graph) ----------

        public float MinSpawnDistance = 12f;
        public float MaxSpawnDistance = 40f;

        // ---------- Doors ----------

        /// <summary>Doors the Facility AI may lock. The armoury door is left to the safe-room logic.</summary>
        public List<string> LockableDoors = new List<string>
        {
            LabLockdownLayout.DoorInfirmaryNorth,
            LabLockdownLayout.DoorLabSouth,
            LabLockdownLayout.DoorControlWest,
            LabLockdownLayout.DoorControlEast,
        };

        /// <summary>Most doors locked at once during a peak (build-up allows one).</summary>
        public int MaxLockedDoors = 2;

        /// <summary>Intensity needed before any door may be locked.</summary>
        public float LockThreshold = 0.55f;

        /// <summary>A door that just changed keeps its state this long (stops doors flickering).</summary>
        public float DoorCooldownSeconds = 10f;

        /// <summary>A locked door is always reopened after this long, so the player is never held back for good.</summary>
        public float MaxLockSeconds = 25f;

        // ---------- Intensity (utility) model ----------

        public float BaseIntensity = 0.15f;
        public float HealthWeight = 0.35f;
        public float ProgressWeight = 0.25f;
        public float CalmWeight = 0.3f;

        /// <summary>Calm time at which the "player is bored" term reaches its maximum.</summary>
        public float CalmSaturationSeconds = 20f;

        public float DamageWeight = 0.5f;
        public float CrowdWeight = 0.4f;

        // ---------- Pacing ----------

        /// <summary>Intensity at or above which a decision is a "peak".</summary>
        public float PeakThreshold = 0.8f;

        /// <summary>Below this health the Director stops spawning and opens doors (relief).</summary>
        public float ReliefHealth = 0.3f;

        /// <summary>Relief period after a peak wave, so pressure rises and falls instead of staying maxed out.</summary>
        public float ReliefAfterPeakSeconds = 15f;

        public float DecisionIntervalCalm = 8f;
        public float DecisionIntervalTense = 4f;

        // ---------- Spawn and door utility (value ordering and branch-and-bound objective) ----------

        public float DistanceUtilityWeight = 1f;
        public float FlankUtilityWeight = 1f;
        public float VarietyUtilityWeight = 0.5f;
        public float SpreadUtilityWeight = 0.5f;

        /// <summary>Walking distance between two spawns that counts as fully spread out.</summary>
        public float SpreadNormalisationMetres = 16f;

        /// <summary>How many recent spawn points are remembered and penalised (variety).</summary>
        public int RecentSpawnMemory = 4;

        public float DoorUtilityWeight = 1f;

        // ---------- Solver ----------

        /// <summary>Node budget per solve, so a decision can never stall a frame.</summary>
        public int SolverNodeBudget = 20000;

        /// <summary>Clamps every value into a sensible range. Called by the Director before each decision.</summary>
        public void Validate()
        {
            MaxEnemiesAlive = Math.Max(0, MaxEnemiesAlive);
            MaxSpawnPerWave = Math.Max(0, MaxSpawnPerWave);
            MinSpawnDistance = Math.Max(0f, MinSpawnDistance);
            MaxSpawnDistance = Math.Max(MinSpawnDistance, MaxSpawnDistance);
            MaxLockedDoors = Math.Max(0, MaxLockedDoors);
            DoorCooldownSeconds = Math.Max(0f, DoorCooldownSeconds);
            MaxLockSeconds = Math.Max(DoorCooldownSeconds, MaxLockSeconds);
            CalmSaturationSeconds = Math.Max(0.01f, CalmSaturationSeconds);
            DecisionIntervalCalm = Math.Max(0.5f, DecisionIntervalCalm);
            DecisionIntervalTense = Math.Max(0.5f, DecisionIntervalTense);
            SpreadNormalisationMetres = Math.Max(0.01f, SpreadNormalisationMetres);
            RecentSpawnMemory = Math.Max(0, RecentSpawnMemory);
            SolverNodeBudget = Math.Max(100, SolverNodeBudget);
            if (LockableDoors == null) LockableDoors = new List<string>();
        }
    }
}

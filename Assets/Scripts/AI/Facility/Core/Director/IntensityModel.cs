using System;

namespace LabLockdown.AI.Facility.Director
{
    public enum DirectorPhase
    {
        /// <summary>No new enemies, doors opened: the player recovers.</summary>
        Relief,

        /// <summary>Pressure rises: some enemies, at most one locked door.</summary>
        BuildUp,

        /// <summary>Maximum pressure: full waves and up to MaxLockedDoors locked; followed by a relief period.</summary>
        Peak,
    }

    /// <summary>The utility layer's verdict for one decision: how much pressure, and what it may use.</summary>
    public sealed class IntensityEvaluation
    {
        public float Intensity;
        public DirectorPhase Phase;

        /// <summary>Enemies to spawn this decision (k), before the CSP checks whether k placements exist.</summary>
        public int SpawnBudget;

        public bool LocksAllowed;
        public int MaxLocks;

        /// <summary>Seconds until the next decision.</summary>
        public float DecisionDelay;

        public string Reason;
    }

    /// <summary>
    /// Utility-based layer of the Facility AI. Turns the player's state into one "desired pressure" value
    /// (intensity) and maps it to a plan. A weighted sum keeps every factor's effect visible and tunable:
    ///
    ///   intensity = clamp01( base + wH·health + wP·progress + wC·calm − wD·recentDamage − wN·enemiesAlive/max )
    ///
    /// Healthy, progressing, bored players get more pressure; hurt or swarmed players get less.
    /// </summary>
    public static class IntensityModel
    {
        public static float ComputeIntensity(FacilityPercepts percepts, float progress01, DirectorSettings settings)
        {
            float calm = Clamp01(percepts.CalmSeconds / settings.CalmSaturationSeconds);
            float crowd = settings.MaxEnemiesAlive > 0 ? Clamp01((float)percepts.EnemiesAlive / settings.MaxEnemiesAlive) : 1f;

            float raw = settings.BaseIntensity
                        + settings.HealthWeight * Clamp01(percepts.Health01)
                        + settings.ProgressWeight * Clamp01(progress01)
                        + settings.CalmWeight * calm
                        - settings.DamageWeight * Clamp01(percepts.RecentDamage01)
                        - settings.CrowdWeight * crowd;

            return Clamp01(raw);
        }

        public static IntensityEvaluation Evaluate(FacilityPercepts percepts, float progress01, DirectorSettings settings,
            bool inReliefWindow, bool playerInSafeArea = false)
        {
            float intensity = ComputeIntensity(percepts, progress01, settings);
            var evaluation = new IntensityEvaluation { Intensity = intensity };

            string relief = null;
            if (percepts.Health01 < settings.ReliefHealth)
                relief = $"Relief: player health {percepts.Health01:P0} is below {settings.ReliefHealth:P0}";
            else if (playerInSafeArea)
                relief = "Relief: player is in a safe area (no pressure while choosing gear)";
            else if (inReliefWindow)
                relief = "Relief: cooling down after a peak";

            if (relief != null)
            {
                evaluation.Phase = DirectorPhase.Relief;
                evaluation.SpawnBudget = 0;
                evaluation.LocksAllowed = false;
                evaluation.MaxLocks = 0;
                evaluation.DecisionDelay = settings.DecisionIntervalCalm;
                evaluation.Reason = relief;
                return evaluation;
            }

            evaluation.Phase = intensity >= settings.PeakThreshold ? DirectorPhase.Peak : DirectorPhase.BuildUp;

            // How many enemies should be alive, and how big one wave may be: both grow with intensity,
            // so build-up sends small groups and only a peak sends a full wave.
            int desiredAlive = (int)Math.Round(intensity * settings.MaxEnemiesAlive, MidpointRounding.AwayFromZero);
            int waveCap = (int)Math.Ceiling(intensity * settings.MaxSpawnPerWave - 1e-6);
            evaluation.SpawnBudget = Math.Max(0, Math.Min(waveCap, desiredAlive - percepts.EnemiesAlive));

            evaluation.LocksAllowed = intensity >= settings.LockThreshold && settings.MaxLockedDoors > 0;
            evaluation.MaxLocks = !evaluation.LocksAllowed
                ? 0
                : evaluation.Phase == DirectorPhase.Peak ? settings.MaxLockedDoors : Math.Min(1, settings.MaxLockedDoors);

            evaluation.DecisionDelay = Lerp(settings.DecisionIntervalCalm, settings.DecisionIntervalTense, intensity);
            evaluation.Reason = $"{evaluation.Phase}: intensity {intensity:F2} → want {desiredAlive} enemies alive " +
                                $"({percepts.EnemiesAlive} now), spawn {evaluation.SpawnBudget}, " +
                                $"{(evaluation.LocksAllowed ? $"lock up to {evaluation.MaxLocks}" : "no locking")}";
            return evaluation;
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
}

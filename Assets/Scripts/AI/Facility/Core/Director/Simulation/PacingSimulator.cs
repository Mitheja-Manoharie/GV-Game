using System;
using System.Collections.Generic;

namespace LabLockdown.AI.Facility.Director.Simulation
{
    /// <summary>
    /// A deliberately simple model of a player, used to test pacing without running the game.
    /// The player walks the shortest open route to the exit, stops to fight while enemies are engaging,
    /// loses health per engaging enemy, defeats enemies at a skill-dependent rate, regenerates when calm,
    /// and heals at medkit tiles.
    /// </summary>
    public sealed class SimulatedPlayer
    {
        public string Name = "Average";

        /// <summary>Walking speed in m/s when no enemy is engaging.</summary>
        public float WalkSpeed = 2.5f;

        /// <summary>Enemies defeated per second while fighting (skill).</summary>
        public float KillsPerSecond = 0.25f;

        /// <summary>Health lost per second for each engaging enemy.</summary>
        public float DamagePerEnemyPerSecond = 0.012f;

        public float RegenPerSecond = 0.01f;
        public float RegenDelaySeconds = 6f;
        public float MedkitHeal = 0.4f;

        /// <summary>Enemy movement speed in m/s (time to reach the player after spawning).</summary>
        public float EnemySpeed = 3f;

        /// <summary>Spawn points within this walking distance count as visible to the player.</summary>
        public float SightRange = 10f;

        /// <summary>Random variation of skill and damage between runs, e.g. 0.2 = ±20 %.</summary>
        public float Variation = 0.2f;

        public static SimulatedPlayer Strong() => new SimulatedPlayer { Name = "Strong", KillsPerSecond = 0.4f, DamagePerEnemyPerSecond = 0.008f };
        public static SimulatedPlayer Average() => new SimulatedPlayer();
        public static SimulatedPlayer Weak() => new SimulatedPlayer { Name = "Weak", KillsPerSecond = 0.15f, DamagePerEnemyPerSecond = 0.018f };
    }

    public sealed class PacingSample
    {
        public double Time;
        public float Intensity;
        public DirectorPhase Phase;
        public float Health;
        public int EnemiesAlive;
        public int LockedDoors;
        public float Progress;
    }

    public sealed class PacingResult
    {
        public readonly List<PacingSample> Samples = new List<PacingSample>();
        public readonly List<DirectorDecision> Decisions = new List<DirectorDecision>();
        public bool ReachedExit;
        public bool Died;
        public double Duration;
        public int TotalSpawned;
        public int MaxAlive;
        public float MinHealth = 1f;

        /// <summary>False if at any moment the player could not reach the exit (must never happen).</summary>
        public bool ExitAlwaysReachable = true;

        public int Peaks;
        public double SecondsInRelief, SecondsInBuildUp, SecondsInPeak;

        public double SpawnsPerMinute => Duration > 0 ? TotalSpawned / (Duration / 60.0) : 0;
    }

    /// <summary>
    /// Runs the real <see cref="FacilityDirector"/> against a <see cref="SimulatedPlayer"/> on the real level
    /// graph, in fixed time steps. Deterministic for a given seed.
    /// Used for tuning (comparing settings) and for pacing tests.
    /// </summary>
    public static class PacingSimulator
    {
        public static PacingResult Run(FacilityLevel level, DirectorSettings settings, SimulatedPlayer player, int seed,
            double maxSeconds = 900, double step = 0.5)
        {
            var random = new Random(seed);
            float skill = Vary(player.KillsPerSecond, player.Variation, random);
            float damageRate = Vary(player.DamagePerEnemyPerSecond, player.Variation, random);

            var graph = level.Graph;
            var director = new FacilityDirector(level, settings);
            var result = new PacingResult();

            int playerNode = level.StartNode;
            double travelLeft = 0;           // seconds until the player reaches nextNode
            int nextNode = -1;
            float health = 1f;
            double lastDamage = 0, lastEnemy = 0;
            double killProgress = 0;
            var enemies = new List<double>(); // arrival time of each living enemy
            var locked = new HashSet<string>();
            var medkitsUsed = new HashSet<int>();
            var medkits = MedkitNodes(graph);
            var healthHistory = new Queue<(double time, float health)>();
            DirectorPhase phase = DirectorPhase.Relief;
            double nextSample = 0;

            for (double t = 0; t <= maxSeconds; t += step)
            {
                // ---- enemies: engaged ones hurt the player and get defeated over time ----
                int engaged = 0;
                foreach (double arrival in enemies)
                    if (arrival <= t) engaged++;

                if (engaged > 0)
                {
                    health -= engaged * damageRate * (float)step;
                    lastDamage = t;
                    killProgress += skill * step;
                    while (killProgress >= 1 && engaged > 0)
                    {
                        killProgress -= 1;
                        RemoveEarliestArrived(enemies, t);
                        engaged--;
                    }
                }
                else
                {
                    killProgress = 0;
                    if (t - lastDamage >= player.RegenDelaySeconds) health = Math.Min(1f, health + player.RegenPerSecond * (float)step);
                }

                if (enemies.Count > 0) lastEnemy = t;
                result.MaxAlive = Math.Max(result.MaxAlive, enemies.Count);
                result.MinHealth = Math.Min(result.MinHealth, Math.Max(0f, health));

                if (health <= 0f)
                {
                    result.Died = true;
                    result.Duration = t;
                    break;
                }

                // ---- movement: walk the shortest open route while not fighting ----
                if (!graph.IsReachable(playerNode, level.ExitNode, locked)) result.ExitAlwaysReachable = false;

                if (engaged == 0)
                {
                    if (nextNode < 0) nextNode = NextStepToExit(graph, playerNode, level.ExitNode, locked, out travelLeft, player.WalkSpeed);
                    if (nextNode >= 0)
                    {
                        travelLeft -= step;
                        if (travelLeft <= 0)
                        {
                            playerNode = nextNode;
                            nextNode = -1;
                            if (medkits.Contains(playerNode) && medkitsUsed.Add(playerNode))
                                health = Math.Min(1f, health + player.MedkitHeal);
                        }
                    }
                }

                if (playerNode == level.ExitNode)
                {
                    result.ReachedExit = true;
                    result.Duration = t;
                    break;
                }

                // ---- percepts ----
                healthHistory.Enqueue((t, health));
                while (healthHistory.Count > 0 && healthHistory.Peek().time < t - 5) healthHistory.Dequeue();
                float highest = health;
                foreach (var sample in healthHistory) highest = Math.Max(highest, sample.health);

                var percepts = new FacilityPercepts
                {
                    Time = t,
                    PlayerNode = playerNode,
                    Health01 = Math.Max(0f, health),
                    RecentDamage01 = Math.Max(0f, highest - health),
                    CalmSeconds = (float)(t - Math.Max(lastDamage, lastEnemy)),
                    EnemiesAlive = enemies.Count,
                    LockedDoors = new HashSet<string>(locked),
                };

                // ---- the Director ----
                if (director.IsDue(t))
                {
                    float[] playerDistance = graph.ShortestDistances(playerNode, locked);
                    foreach (int node in level.SpawnCandidates)
                        if (playerDistance[node] <= player.SightRange) percepts.VisibleNodes.Add(node);

                    var decision = director.Decide(percepts);
                    result.Decisions.Add(decision);
                    phase = decision.Evaluation.Phase;
                    if (phase == DirectorPhase.Peak) result.Peaks++;

                    locked.Clear();
                    foreach (var door in decision.DoorLocks)
                        if (door.Value) locked.Add(door.Key);

                    float[] fromPlayer = graph.ShortestDistances(playerNode, locked);
                    foreach (int node in decision.SpawnNodes)
                    {
                        enemies.Add(t + fromPlayer[node] / player.EnemySpeed);
                        result.TotalSpawned++;
                    }
                }

                switch (phase)
                {
                    case DirectorPhase.Relief: result.SecondsInRelief += step; break;
                    case DirectorPhase.BuildUp: result.SecondsInBuildUp += step; break;
                    default: result.SecondsInPeak += step; break;
                }

                if (t >= nextSample)
                {
                    nextSample = t + 1.0;
                    result.Samples.Add(new PacingSample
                    {
                        Time = t,
                        Intensity = IntensityModel.ComputeIntensity(percepts, level.Progress(playerNode), settings),
                        Phase = phase,
                        Health = Math.Max(0f, health),
                        EnemiesAlive = enemies.Count,
                        LockedDoors = locked.Count,
                        Progress = level.Progress(playerNode),
                    });
                }

                result.Duration = t;
            }

            return result;
        }

        private static int NextStepToExit(LevelGraph graph, int from, int exit, ICollection<string> locked, out double travelSeconds, float speed)
        {
            travelSeconds = 0;
            float[] toExit = graph.ShortestDistances(exit, locked);
            if (float.IsPositiveInfinity(toExit[from])) return -1;

            int best = -1;
            float bestDistance = toExit[from];
            foreach (var edge in graph.EdgesOf(from))
            {
                if (!LevelGraph.IsPassable(edge, locked)) continue;
                int other = edge.Other(from);
                if (toExit[other] < bestDistance)
                {
                    bestDistance = toExit[other];
                    best = other;
                    travelSeconds = edge.Cost / speed;
                }
            }

            return best;
        }

        private static void RemoveEarliestArrived(List<double> enemies, double now)
        {
            int index = -1;
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] <= now && (index < 0 || enemies[i] < enemies[index])) index = i;
            if (index >= 0) enemies.RemoveAt(index);
        }

        private static HashSet<int> MedkitNodes(LevelGraph graph) => new HashSet<int>
        {
            graph.FindNearestNode(-2.5f, 18.5f),   // maintenance bay
            graph.FindNearestNode(-10.5f, 18.5f),  // infirmary
            graph.FindNearestNode(18.5f, 25.5f),   // research lab
        };

        private static float Vary(float value, float variation, Random random) =>
            value * (float)(1.0 + (random.NextDouble() * 2.0 - 1.0) * variation);
    }
}

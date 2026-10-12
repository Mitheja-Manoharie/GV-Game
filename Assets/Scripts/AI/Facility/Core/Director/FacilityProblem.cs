using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Csp;

namespace LabLockdown.AI.Facility.Director
{
    /// <summary>Level data the Facility AI needs on every decision, computed once.</summary>
    public sealed class FacilityLevel
    {
        private readonly Dictionary<int, float[]> distancesFromCandidate = new Dictionary<int, float[]>();

        public FacilityLevel(LevelGraph graph, IEnumerable<int> spawnCandidates, int exitNode, int startNode)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            if (exitNode < 0 || exitNode >= graph.NodeCount) throw new ArgumentOutOfRangeException(nameof(exitNode));
            if (startNode < 0 || startNode >= graph.NodeCount) throw new ArgumentOutOfRangeException(nameof(startNode));

            ExitNode = exitNode;
            StartNode = startNode;

            // Keep valid, distinct candidates outside the safe areas (quarantine cell, armoury).
            var candidates = new List<int>();
            if (spawnCandidates != null)
            {
                foreach (int node in spawnCandidates)
                {
                    if (node < 0 || node >= graph.NodeCount || candidates.Contains(node)) continue;
                    if (LabLockdownLayout.IsSafeArea(graph.Nodes[node].Area)) continue;
                    candidates.Add(node);
                }
            }

            SpawnCandidates = candidates;
            foreach (int node in candidates) distancesFromCandidate[node] = graph.ShortestDistances(node);

            ExitDistances = graph.ShortestDistances(exitNode);
            StartToExitDistance = ExitDistances[startNode];
        }

        public LevelGraph Graph { get; }
        public IReadOnlyList<int> SpawnCandidates { get; }
        public int ExitNode { get; }
        public int StartNode { get; }

        /// <summary>Walking distance from every node to the exit with all doors open.</summary>
        public float[] ExitDistances { get; }

        public float StartToExitDistance { get; }

        /// <summary>Walking distance between two spawn candidates with all doors open.</summary>
        public float CandidateDistance(int a, int b) => distancesFromCandidate[a][b];

        /// <summary>0 at the start, 1 at the exit, based on remaining walking distance.</summary>
        public float Progress(int playerNode)
        {
            if (StartToExitDistance <= 0f || float.IsPositiveInfinity(StartToExitDistance)) return 0f;
            float progress = 1f - ExitDistances[playerNode] / StartToExitDistance;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }
    }

    /// <summary>What the Facility AI remembers about one door between decisions.</summary>
    public sealed class DoorMemory
    {
        public double LastChanged = double.NegativeInfinity;
        public double LockedSince = double.NaN;
    }

    /// <summary>
    /// One decision expressed as a CSP.
    ///
    /// Variables
    ///   Spawn1..Spawnk – domain: spawn candidate nodes (outside the safe areas)
    ///   one per lockable door – domain: {Open = 0, Locked = 1}
    ///
    /// Constraints
    ///   unary   Spawn hidden from the player, not in the player's area, within the walking-distance window
    ///   unary   door rules: open when the player is at it / locked too long / player hurt;
    ///           otherwise keep its state during the cooldown; otherwise open if locking is not allowed
    ///   binary  Spawn_i &lt; Spawn_i+1 (all different + symmetry breaking: spawn slots are interchangeable)
    ///   global  the exit stays reachable from the player with the chosen doors locked
    ///   global  at most L doors locked
    ///   global  every spawn can reach the player with the chosen doors locked
    /// </summary>
    public sealed class FacilityProblem
    {
        public const int Open = 0;
        public const int Locked = 1;

        private FacilityProblem()
        {
        }

        public CspProblem Problem { get; private set; }
        public int[] SpawnVariables { get; private set; }
        public string[] DoorIds { get; private set; }
        public int[] DoorVariables { get; private set; }
        public FacilityObjective Objective { get; private set; }

        /// <summary>Door rules applied this decision, in plain words (for the debug overlay).</summary>
        public IReadOnlyList<string> Rules { get; private set; }

        public static FacilityProblem Build(
            FacilityLevel level,
            FacilityPercepts percepts,
            IntensityEvaluation evaluation,
            DirectorSettings settings,
            int spawnCount,
            IReadOnlyDictionary<string, DoorMemory> doorMemory,
            ICollection<int> recentSpawns)
        {
            var graph = level.Graph;
            int player = percepts.PlayerNode;
            var problem = new CspProblem();
            var rules = new List<string>();

            // Doors the Director does not control stay as they are (e.g. the armoury safe-room door).
            var lockableDoors = new List<string>();
            foreach (string id in settings.LockableDoors)
                if (!lockableDoors.Contains(id) && graph.FindDoorEdge(id) != null) lockableDoors.Add(id);

            var fixedLocked = new HashSet<string>();
            foreach (string id in percepts.LockedDoors)
                if (!lockableDoors.Contains(id)) fixedLocked.Add(id);

            float[] playerDistance = graph.ShortestDistances(player, fixedLocked);
            string playerArea = graph.Nodes[player].Area;

            // ---------- Spawn variables ----------
            var spawnVariables = new int[spawnCount];
            for (int i = 0; i < spawnCount; i++)
            {
                string name = $"Spawn{i + 1}";
                int v = problem.AddVariable(name, level.SpawnCandidates);
                spawnVariables[i] = v;

                problem.AddUnary($"{name} hidden from the player", v, node => !percepts.VisibleNodes.Contains(node));
                problem.AddUnary($"{name} not in the player's area ({playerArea})", v, node => graph.Nodes[node].Area != playerArea);
                problem.AddUnary($"{name} {settings.MinSpawnDistance:0}–{settings.MaxSpawnDistance:0} m from the player", v,
                    node => playerDistance[node] >= settings.MinSpawnDistance && playerDistance[node] <= settings.MaxSpawnDistance);
            }

            for (int i = 0; i + 1 < spawnCount; i++)
                problem.AddBinary($"Spawn{i + 1} < Spawn{i + 2} (all different, symmetry breaking)",
                    spawnVariables[i], spawnVariables[i + 1], (a, b) => a < b);

            // ---------- Door variables ----------
            var doorVariables = new int[lockableDoors.Count];
            int forcedLocked = 0;
            bool playerHurt = percepts.Health01 < settings.ReliefHealth;

            for (int j = 0; j < lockableDoors.Count; j++)
            {
                string id = lockableDoors[j];
                int v = problem.AddVariable(id, new[] { Open, Locked });
                doorVariables[j] = v;

                int current = percepts.LockedDoors.Contains(id) ? Locked : Open;
                doorMemory.TryGetValue(id, out var memory);
                memory = memory ?? new DoorMemory();

                var edge = graph.FindDoorEdge(id);
                bool playerAtDoor = Math.Min(playerDistance[edge.A], playerDistance[edge.B]) <= edge.Cost;
                double lockedFor = current == Locked && !double.IsNaN(memory.LockedSince) ? percepts.Time - memory.LockedSince : 0.0;
                double sinceChange = percepts.Time - memory.LastChanged;

                // Rules in priority order: safety first, then stability, then pacing.
                string rule;
                int[] allowed;
                if (playerAtDoor)
                {
                    rule = $"{id} must be open: the player is at the door";
                    allowed = new[] { Open };
                }
                else if (lockedFor >= settings.MaxLockSeconds)
                {
                    rule = $"{id} must be open: locked for {lockedFor:0} s (max {settings.MaxLockSeconds:0} s)";
                    allowed = new[] { Open };
                }
                else if (playerHurt)
                {
                    rule = $"{id} must be open: player health is low";
                    allowed = new[] { Open };
                }
                else if (sinceChange < settings.DoorCooldownSeconds)
                {
                    rule = $"{id} keeps {(current == Locked ? "locked" : "open")}: changed {sinceChange:0} s ago (cooldown)";
                    allowed = new[] { current };
                    if (current == Locked) forcedLocked++;
                }
                else if (!evaluation.LocksAllowed)
                {
                    rule = $"{id} must be open: locking not allowed in this phase";
                    allowed = new[] { Open };
                }
                else
                {
                    rule = null;
                    allowed = null;
                }

                if (allowed != null)
                {
                    rules.Add(rule);
                    var allowedSet = new HashSet<int>(allowed);
                    problem.AddUnary(rule, v, value => allowedSet.Contains(value));
                }
            }

            // Doors locked by the chosen assignment, plus doors the Director does not control.
            var lockedBuffer = new HashSet<string>();
            HashSet<string> LockedSet(IAssignment assignment)
            {
                lockedBuffer.Clear();
                foreach (string id in fixedLocked) lockedBuffer.Add(id);
                for (int j = 0; j < doorVariables.Length; j++)
                    if (assignment[doorVariables[j]] == Locked) lockedBuffer.Add(lockableDoors[j]);
                return lockedBuffer;
            }

            if (doorVariables.Length > 0)
            {
                problem.AddGlobal("Exit reachable from the player", doorVariables,
                    a => graph.IsReachable(player, level.ExitNode, LockedSet(a)));

                int maxLocked = Math.Max(evaluation.MaxLocks, forcedLocked);
                problem.AddGlobal($"At most {maxLocked} doors locked", doorVariables, a =>
                {
                    int count = 0;
                    foreach (int v in doorVariables)
                        if (a[v] == Locked) count++;
                    return count <= maxLocked;
                });
            }

            for (int i = 0; i < spawnCount; i++)
            {
                int spawnVariable = spawnVariables[i];
                var scope = new int[1 + doorVariables.Length];
                scope[0] = spawnVariable;
                Array.Copy(doorVariables, 0, scope, 1, doorVariables.Length);

                problem.AddGlobal($"Spawn{i + 1} can reach the player", scope,
                    a => graph.IsReachable(a[spawnVariable], player, LockedSet(a)));
            }

            var objective = new FacilityObjective(level, percepts, evaluation, settings, spawnVariables,
                lockableDoors, doorVariables, playerDistance, fixedLocked, recentSpawns);

            return new FacilityProblem
            {
                Problem = problem,
                SpawnVariables = spawnVariables,
                DoorIds = lockableDoors.ToArray(),
                DoorVariables = doorVariables,
                Objective = objective,
                Rules = rules,
            };
        }
    }

    /// <summary>
    /// The utility side of the CSP: how good a valid configuration is for gameplay.
    /// Used twice: as value ordering (try the best values first) and as the branch-and-bound objective.
    ///
    ///   spawn score = weighted average of
    ///       distance – close to the middle of the distance window (not too close, not too far)
    ///       flank    – at high intensity prefer spawns BEHIND the player (further from the exit);
    ///                  at low intensity prefer spawns AHEAD (the player meets them head-on, which is fairer)
    ///       variety  – avoid spawn points used recently
    ///     + spread   – walking distance to the other chosen spawns (avoid clumping)
    ///
    ///   door score   Locked = intensity × detour (how much longer the player's route to the exit becomes)
    ///                Open   = (1 − intensity) × 0.5
    ///   so a door is only locked when it actually changes the player's route and pressure is high.
    /// </summary>
    public sealed class FacilityObjective : ISolutionObjective
    {
        private readonly DirectorSettings settings;
        private readonly FacilityLevel level;
        private readonly int[] spawnVariables;
        private readonly Dictionary<int, int> doorIndexByVariable = new Dictionary<int, int>();
        private readonly Dictionary<int, double> spawnBase = new Dictionary<int, double>();
        private readonly double maxSpawnBase;
        private readonly double[] doorLockedScore;
        private readonly double[] doorOpenScore;
        private readonly int variableCount;

        internal FacilityObjective(
            FacilityLevel level,
            FacilityPercepts percepts,
            IntensityEvaluation evaluation,
            DirectorSettings settings,
            int[] spawnVariables,
            IReadOnlyList<string> doorIds,
            int[] doorVariables,
            float[] playerDistance,
            ICollection<string> fixedLocked,
            ICollection<int> recentSpawns)
        {
            this.level = level;
            this.settings = settings;
            this.spawnVariables = spawnVariables;
            variableCount = spawnVariables.Length + doorVariables.Length;
            float intensity = evaluation.Intensity;

            maxSpawnBase = 0.0;
            foreach (int node in level.SpawnCandidates)
            {
                double score = SpawnBaseScore(node, percepts.PlayerNode, playerDistance, intensity, recentSpawns);
                spawnBase[node] = score;
                if (score > maxSpawnBase) maxSpawnBase = score;
            }

            // Door detour: how much longer the route to the exit gets if only this door is locked.
            var graph = level.Graph;
            float baseRoute = graph.ShortestDistances(percepts.PlayerNode, fixedLocked)[level.ExitNode];
            doorLockedScore = new double[doorVariables.Length];
            doorOpenScore = new double[doorVariables.Length];

            for (int j = 0; j < doorVariables.Length; j++)
            {
                doorIndexByVariable[doorVariables[j]] = j;

                var locked = new HashSet<string>(fixedLocked) { doorIds[j] };
                float lockedRoute = graph.ShortestDistances(percepts.PlayerNode, locked)[level.ExitNode];

                double detour = 0.0;
                if (!float.IsPositiveInfinity(baseRoute) && !float.IsPositiveInfinity(lockedRoute))
                    detour = Clamp01((lockedRoute - baseRoute) / Math.Max(baseRoute, LabLockdownLayout.TileSize));

                doorLockedScore[j] = settings.DoorUtilityWeight * intensity * detour;
                doorOpenScore[j] = settings.DoorUtilityWeight * (1.0 - intensity) * 0.5;
            }
        }

        /// <summary>Spawn score without the spread term, in [0, 1].</summary>
        public double SpawnBaseScore(int node, int playerNode, float[] playerDistance, float intensity, ICollection<int> recentSpawns)
        {
            double min = settings.MinSpawnDistance;
            double max = settings.MaxSpawnDistance;
            double ideal = (min + max) * 0.5;
            double half = (max - min) * 0.5;
            double distance = half <= 0.0 ? 1.0 : Clamp01(1.0 - Math.Abs(playerDistance[node] - ideal) / half);

            bool behind = level.ExitDistances[node] > level.ExitDistances[playerNode] + 0.01f;
            double flank = behind ? intensity : 1.0 - intensity;

            double variety = recentSpawns != null && recentSpawns.Contains(node) ? 0.0 : 1.0;

            double weights = settings.DistanceUtilityWeight + settings.FlankUtilityWeight + settings.VarietyUtilityWeight;
            if (weights <= 0.0) return 0.0;
            return (settings.DistanceUtilityWeight * distance + settings.FlankUtilityWeight * flank +
                    settings.VarietyUtilityWeight * variety) / weights;
        }

        /// <summary>Score of one variable's value given the other assigned variables. Also used as value ordering.</summary>
        public double VariableScore(int variable, int value, IAssignment assignment)
        {
            if (doorIndexByVariable.TryGetValue(variable, out int j))
                return value == FacilityProblem.Locked ? doorLockedScore[j] : doorOpenScore[j];

            spawnBase.TryGetValue(value, out double baseScore);
            return baseScore + settings.SpreadUtilityWeight * Spread(variable, value, assignment);
        }

        public double Score(IAssignment complete)
        {
            double total = 0.0;
            for (int v = 0; v < variableCount; v++) total += VariableScore(v, complete[v], complete);
            return total;
        }

        /// <summary>
        /// Assigned variables score what they score now; unassigned ones their best possible value.
        /// A spawn's spread can only fall as more spawns are placed (it is a minimum over more distances),
        /// so the current value is never an underestimate and the bound stays valid.
        /// </summary>
        public double UpperBound(IAssignment partial)
        {
            double bound = 0.0;
            for (int v = 0; v < variableCount; v++)
            {
                if (partial.IsAssigned(v))
                {
                    bound += VariableScore(v, partial[v], partial);
                }
                else if (doorIndexByVariable.TryGetValue(v, out int j))
                {
                    bound += Math.Max(doorLockedScore[j], doorOpenScore[j]);
                }
                else
                {
                    bound += maxSpawnBase + settings.SpreadUtilityWeight;
                }
            }

            return bound;
        }

        /// <summary>1 when no other spawn is placed yet; otherwise the nearest other spawn's distance, normalised.</summary>
        private double Spread(int variable, int node, IAssignment assignment)
        {
            double nearest = double.PositiveInfinity;
            foreach (int other in spawnVariables)
            {
                if (other == variable || !assignment.IsAssigned(other)) continue;
                double distance = level.CandidateDistance(node, assignment[other]);
                if (distance < nearest) nearest = distance;
            }

            return double.IsPositiveInfinity(nearest) ? 1.0 : Clamp01(nearest / settings.SpreadNormalisationMetres);
        }

        private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
    }
}

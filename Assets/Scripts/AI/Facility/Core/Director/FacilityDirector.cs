using System;
using System.Collections.Generic;
using System.Text;
using LabLockdown.AI.Facility.Csp;

namespace LabLockdown.AI.Facility.Director
{
    /// <summary>What the Facility AI decided, and why. Applied by the Unity runner; shown in the debug overlay.</summary>
    public sealed class DirectorDecision
    {
        public double Time { get; internal set; }
        public IntensityEvaluation Evaluation { get; internal set; }
        public float Progress { get; internal set; }

        /// <summary>Spawns the utility layer asked for (k) before the CSP checked what is possible.</summary>
        public int RequestedSpawns { get; internal set; }

        /// <summary>Graph nodes to spawn enemies at (may be fewer than requested).</summary>
        public IReadOnlyList<int> SpawnNodes { get; internal set; } = Array.Empty<int>();

        /// <summary>Target state of every lockable door: true = locked.</summary>
        public IReadOnlyDictionary<string, bool> DoorLocks { get; internal set; } = new Dictionary<string, bool>();

        public IReadOnlyList<string> DoorsToLock { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> DoorsToUnlock { get; internal set; } = Array.Empty<string>();

        public SolverStats Stats { get; internal set; }
        public double Score { get; internal set; } = double.NaN;
        public bool IsProvenOptimal { get; internal set; }

        /// <summary>True when no valid configuration existed and the safety fallback was used.</summary>
        public bool UsedFallback { get; internal set; }

        public IReadOnlyList<string> Rules { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> Notes { get; internal set; } = Array.Empty<string>();

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append($"t={Time:0.0} {Evaluation?.Phase} intensity {Evaluation?.Intensity:F2}, ");
            text.Append($"spawn {SpawnNodes.Count}/{RequestedSpawns}");
            if (DoorsToLock.Count > 0) text.Append($", lock {string.Join(", ", DoorsToLock)}");
            if (DoorsToUnlock.Count > 0) text.Append($", unlock {string.Join(", ", DoorsToUnlock)}");
            if (UsedFallback) text.Append(" [fallback]");
            return text.ToString();
        }
    }

    /// <summary>
    /// The Facility AI ("Lockdown Director"). Each decision:
    ///   1. sense       – percepts from the runner (player node, health, damage, calm time, visibility, doors)
    ///   2. utility     – IntensityModel decides HOW MUCH pressure (spawn count k, whether doors may lock)
    ///   3. CSP         – FacilityProblem + branch and bound decide WHERE, under the fairness constraints
    ///   4. degrade     – if k spawns cannot be placed, try k−1 … 0; if nothing is valid, open every door
    ///   5. remember    – door change times, recent spawn points, relief window after a peak
    /// Pure C#: no Unity types, so the whole decision process is unit tested.
    /// </summary>
    public sealed class FacilityDirector
    {
        private readonly BacktrackingSolver solver = new BacktrackingSolver();
        private readonly Dictionary<string, DoorMemory> doors = new Dictionary<string, DoorMemory>();
        private readonly List<int> recentSpawns = new List<int>();
        private double reliefUntil = double.NegativeInfinity;
        private double lastSeenInSafeArea = double.NegativeInfinity;

        public FacilityDirector(FacilityLevel level, DirectorSettings settings)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Settings = settings ?? new DirectorSettings();
            Reset(0.0);
        }

        public FacilityLevel Level { get; }
        public DirectorSettings Settings { get; }
        public double NextDecisionTime { get; private set; }
        public DirectorDecision LastDecision { get; private set; }

        public bool IsDue(double time) => time >= NextDecisionTime;

        /// <summary>Decides if a decision is due, otherwise returns null.</summary>
        public DirectorDecision Tick(FacilityPercepts percepts) => IsDue(percepts.Time) ? Decide(percepts) : null;

        /// <summary>Clears all memory, e.g. when the player respawns. The first decision waits one calm interval.</summary>
        public void Reset(double now)
        {
            doors.Clear();
            recentSpawns.Clear();
            reliefUntil = double.NegativeInfinity;
            lastSeenInSafeArea = double.NegativeInfinity;
            LastDecision = null;
            NextDecisionTime = now + Settings.DecisionIntervalCalm;
        }

        public DirectorDecision Decide(FacilityPercepts percepts)
        {
            if (percepts == null) throw new ArgumentNullException(nameof(percepts));
            if (percepts.PlayerNode < 0 || percepts.PlayerNode >= Level.Graph.NodeCount)
                throw new ArgumentOutOfRangeException(nameof(percepts), $"Player node {percepts.PlayerNode} is not in the level graph.");

            Settings.Validate();
            percepts.VisibleNodes = percepts.VisibleNodes ?? new HashSet<int>();
            percepts.LockedDoors = percepts.LockedDoors ?? new HashSet<string>();
            percepts.OccupiedSpawnNodes = percepts.OccupiedSpawnNodes ?? new HashSet<int>();
            percepts.ObstructedDoors = percepts.ObstructedDoors ?? new HashSet<string>();
            SyncDoorMemory(percepts);

            float progress = Level.Progress(percepts.PlayerNode);
            bool inSafeArea = LabLockdownLayout.IsSafeArea(Level.Graph.Nodes[percepts.PlayerNode].Area);
            var notes = new List<string>();

            if (inSafeArea) lastSeenInSafeArea = percepts.Time;
            percepts.CalmSeconds = EffectiveCalmSeconds(percepts, inSafeArea);

            var evaluation = IntensityModel.Evaluate(percepts, progress, Settings, percepts.Time < reliefUntil, inSafeArea);

            // Graceful degradation: ask for k spawns; if the constraints cannot be met, ask for fewer.
            FacilityProblem facility = null;
            SolveResult result = null;
            for (int k = evaluation.SpawnBudget; k >= 0; k--)
            {
                facility = FacilityProblem.Build(Level, percepts, evaluation, Settings, k, doors, recentSpawns);
                var options = new SolverOptions
                {
                    MaxNodes = Settings.SolverNodeBudget,
                    ValueScore = facility.Objective.VariableScore,
                };

                result = solver.SolveBest(facility.Problem, facility.Objective, options);
                if (result.IsSolved)
                {
                    if (k < evaluation.SpawnBudget)
                        notes.Add($"Only {k} of {evaluation.SpawnBudget} spawns could be placed fairly");
                    if (!result.IsProvenOptimal)
                        notes.Add("Node budget reached: using the best configuration found so far");
                    break;
                }

                notes.Add(result.Status == SolveStatus.NodeLimitReached
                    ? $"{k} spawns: node budget reached without a valid placement"
                    : $"{k} spawns: no valid placement");
            }

            var decision = new DirectorDecision
            {
                Time = percepts.Time,
                Evaluation = evaluation,
                Progress = progress,
                RequestedSpawns = evaluation.SpawnBudget,
                Stats = result?.Stats,
                Rules = facility?.Rules ?? (IReadOnlyList<string>)Array.Empty<string>(),
            };

            var doorLocks = new Dictionary<string, bool>();
            var spawnNodes = new List<int>();

            if (result != null && result.IsSolved)
            {
                foreach (int v in facility.SpawnVariables) spawnNodes.Add(result.Values[v]);
                for (int j = 0; j < facility.DoorIds.Length; j++)
                    doorLocks[facility.DoorIds[j]] = result.Values[facility.DoorVariables[j]] == FacilityProblem.Locked;

                decision.Score = result.Score;
                decision.IsProvenOptimal = result.IsProvenOptimal;
            }
            else
            {
                // Safety fallback: never leave the player in an unfair state.
                foreach (string id in Settings.LockableDoors)
                    if (Level.Graph.FindDoorEdge(id) != null) doorLocks[id] = false;

                decision.UsedFallback = true;
                notes.Add("No valid configuration: spawning nothing and opening every door");
            }

            ApplyMemory(percepts, decision, doorLocks, spawnNodes);

            if (evaluation.Phase == DirectorPhase.Peak && spawnNodes.Count > 0)
            {
                reliefUntil = percepts.Time + Settings.ReliefAfterPeakSeconds;
                notes.Add($"Peak wave: no new spawns for {Settings.ReliefAfterPeakSeconds:0} s");
            }

            decision.Notes = notes;
            NextDecisionTime = percepts.Time + evaluation.DecisionDelay;
            LastDecision = decision;
            return decision;
        }

        /// <summary>
        /// The intensity the Director would compute right now, without deciding or changing its memory.
        /// Used by the live intensity graph in the debug overlay.
        /// </summary>
        public float PreviewIntensity(FacilityPercepts percepts)
        {
            if (percepts == null) throw new ArgumentNullException(nameof(percepts));
            if (percepts.PlayerNode < 0 || percepts.PlayerNode >= Level.Graph.NodeCount) return 0f;

            bool inSafeArea = LabLockdownLayout.IsSafeArea(Level.Graph.Nodes[percepts.PlayerNode].Area);
            float calm = percepts.CalmSeconds;
            percepts.CalmSeconds = EffectiveCalmSeconds(percepts, inSafeArea);
            float intensity = IntensityModel.ComputeIntensity(percepts, Level.Progress(percepts.PlayerNode), Settings);
            percepts.CalmSeconds = calm;
            return intensity;
        }

        /// <summary>
        /// Time spent choosing gear in a safe area is preparation, not boredom: outside a safe area, calm time
        /// only counts from when the player was last seen in one. Otherwise the first encounter after the
        /// armoury would always be a peak.
        /// </summary>
        private float EffectiveCalmSeconds(FacilityPercepts percepts, bool inSafeArea)
        {
            if (inSafeArea) return percepts.CalmSeconds;
            return (float)Math.Min(percepts.CalmSeconds, percepts.Time - lastSeenInSafeArea);
        }

        /// <summary>Keeps lock times in step with the real doors (they may be changed by other code).</summary>
        private void SyncDoorMemory(FacilityPercepts percepts)
        {
            foreach (string id in Settings.LockableDoors)
            {
                if (!doors.TryGetValue(id, out var memory))
                {
                    memory = new DoorMemory();
                    doors[id] = memory;
                }

                bool locked = percepts.LockedDoors.Contains(id);
                if (locked && double.IsNaN(memory.LockedSince)) memory.LockedSince = percepts.Time;
                if (!locked) memory.LockedSince = double.NaN;
            }
        }

        private void ApplyMemory(FacilityPercepts percepts, DirectorDecision decision, Dictionary<string, bool> doorLocks, List<int> spawnNodes)
        {
            var toLock = new List<string>();
            var toUnlock = new List<string>();

            foreach (var entry in doorLocks)
            {
                bool wasLocked = percepts.LockedDoors.Contains(entry.Key);
                if (entry.Value == wasLocked) continue;

                if (!doors.TryGetValue(entry.Key, out var memory))
                {
                    memory = new DoorMemory();
                    doors[entry.Key] = memory;
                }

                memory.LastChanged = percepts.Time;
                memory.LockedSince = entry.Value ? percepts.Time : double.NaN;
                (entry.Value ? toLock : toUnlock).Add(entry.Key);
            }

            foreach (int node in spawnNodes) recentSpawns.Add(node);
            while (recentSpawns.Count > Settings.RecentSpawnMemory) recentSpawns.RemoveAt(0);

            decision.SpawnNodes = spawnNodes;
            decision.DoorLocks = doorLocks;
            decision.DoorsToLock = toLock;
            decision.DoorsToUnlock = toUnlock;
        }
    }
}

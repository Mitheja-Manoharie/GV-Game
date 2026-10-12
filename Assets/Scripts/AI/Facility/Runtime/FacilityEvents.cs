using System;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Director;
using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// What the Facility AI tells the rest of the game. Teammates' agents subscribe here instead of
    /// referencing the runner, e.g. a Flanker re-plans when a door locks, a Specimen hides during a peak.
    ///
    /// <code>
    /// void OnEnable()  => FacilityEvents.DoorLockChanged += OnDoorLockChanged;
    /// void OnDisable() => FacilityEvents.DoorLockChanged -= OnDoorLockChanged;
    /// void OnDoorLockChanged(string doorId, bool locked) { if (locked) agent.ResetPath(); }
    /// </code>
    ///
    /// All events are raised on the main thread, after the decision has been applied to the scene.
    /// Subscribers are cleared when Play mode starts, so stale handlers from a previous session never fire.
    /// </summary>
    public static class FacilityEvents
    {
        private static readonly HashSet<string> lockedDoors = new HashSet<string>();

        /// <summary>Every decision, including ones that change nothing.</summary>
        public static event Action<DirectorDecision> DecisionMade;

        /// <summary>The pacing phase changed (previous, current).</summary>
        public static event Action<DirectorPhase, DirectorPhase> PhaseChanged;

        /// <summary>A door was locked (true) or unlocked (false) by the Facility AI. Door id = Door_* marker name.</summary>
        public static event Action<string, bool> DoorLockChanged;

        /// <summary>The Facility AI spawned a wave: spawn positions and the phase it was spawned in.</summary>
        public static event Action<IReadOnlyList<Vector3>, DirectorPhase> WaveSpawned;

        public static DirectorPhase Phase { get; private set; } = DirectorPhase.Relief;
        public static float Intensity { get; private set; }

        /// <summary>Doors the Facility AI currently holds locked.</summary>
        public static IReadOnlyCollection<string> LockedDoors => lockedDoors;

        public static bool IsDoorLocked(string doorId) => lockedDoors.Contains(doorId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DecisionMade = null;
            PhaseChanged = null;
            DoorLockChanged = null;
            WaveSpawned = null;
            lockedDoors.Clear();
            Phase = DirectorPhase.Relief;
            Intensity = 0f;
        }

        // ---------- Raised by FacilityDirectorRunner ----------

        internal static void RaiseDecision(DirectorDecision decision, IReadOnlyList<Vector3> spawnPositions)
        {
            var previous = Phase;
            Phase = decision.Evaluation.Phase;
            Intensity = decision.Evaluation.Intensity;

            foreach (string door in decision.DoorsToLock)
                if (lockedDoors.Add(door)) Invoke(DoorLockChanged, door, true);
            foreach (string door in decision.DoorsToUnlock)
                if (lockedDoors.Remove(door)) Invoke(DoorLockChanged, door, false);

            if (previous != Phase) Invoke(PhaseChanged, previous, Phase);
            if (spawnPositions.Count > 0) Invoke(WaveSpawned, spawnPositions, Phase);
            Invoke(DecisionMade, decision);
        }

        internal static void RaiseAllUnlocked()
        {
            var doors = new List<string>(lockedDoors);
            lockedDoors.Clear();
            foreach (string door in doors) Invoke(DoorLockChanged, door, false);
        }

        // One faulty subscriber must not stop the others (or the Director).
        private static void Invoke<T>(Action<T> handler, T arg)
        {
            if (handler == null) return;
            foreach (Action<T> h in handler.GetInvocationList())
            {
                try { h(arg); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void Invoke<T1, T2>(Action<T1, T2> handler, T1 a, T2 b)
        {
            if (handler == null) return;
            foreach (Action<T1, T2> h in handler.GetInvocationList())
            {
                try { h(a, b); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}

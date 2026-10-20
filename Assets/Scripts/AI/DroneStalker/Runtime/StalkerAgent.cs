using System;
using System.Collections.Generic;
using UnityEngine;

namespace LabLockdown.AI.DroneStalker.Runtime
{
    /// <summary>How fast the body should move for the current action (the body maps these to its own speeds).</summary>
    public enum StalkerMoveSpeed
    {
        /// <summary>Stay where you are (waiting in ambush, or no route).</summary>
        Stop,
        /// <summary>Slow wandering while no player is in sight.</summary>
        Patrol,
        /// <summary>Careful movement: stalking, hiding, moving to an ambush spot.</summary>
        Investigate,
        /// <summary>Fast: closing in for a strike, or getting away afterwards.</summary>
        Chase,
    }

    /// <summary>
    /// The Drone Stalker's AI component. Every frame it senses the scene (<see cref="StalkerSensor"/>), lets the
    /// <see cref="StalkerBrain"/> decide, and publishes the result for the body to carry out:
    ///   - <see cref="CurrentPath"/>: the route as world positions (the "path array" the Agent Controller turns into movement),
    ///   - <see cref="CurrentSpeed"/> and <see cref="CurrentAction"/>,
    ///   - <see cref="LookTarget"/>: where to face while the player is known,
    ///   - <see cref="StrikeRequested"/>: raised once when the drone should deliver its strike.
    /// It never moves, rotates or animates anything itself; that is the body's job.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StalkerSensor))]
    public sealed class StalkerAgent : MonoBehaviour
    {
        [Tooltip("Every tunable weight of the Drone Stalker. See Docs/DroneStalker.md for what each one does.")]
        [SerializeField] private StalkerSettings settings = new StalkerSettings();
        [Tooltip("Give each drone in the scene its own patrol randomness, so several drones spread out.")]
        [SerializeField] private bool uniquePatrolPerDrone = true;

        StalkerSensor sensor;
        StalkerBrain brain;
        readonly StalkerPercepts percepts = new StalkerPercepts();
        readonly List<Vector3> path = new List<Vector3>();
        StalkerDecision publishedDecision;
        int[] publishedNodes;
        StalkerDecision struckDecision;

        /// <summary>Raised when the route or the speed changes: (new route as world positions, speed).</summary>
        public event Action<IReadOnlyList<Vector3>, StalkerMoveSpeed> PathChanged;

        /// <summary>Raised when the action changes: (previous action, new action).</summary>
        public event Action<StalkerAction, StalkerAction> ActionChanged;

        /// <summary>Raised once per strike decision, with the player as the target. The body plays the attack and deals damage.</summary>
        public event Action<Transform> StrikeRequested;

        /// <summary>The brain (for the debug view and tests). Null until Start.</summary>
        public StalkerBrain Brain => brain;

        /// <summary>The sensor feeding the brain.</summary>
        public StalkerSensor Sensor => sensor;

        /// <summary>The latest percepts (what the drone sensed this frame).</summary>
        public StalkerPercepts Percepts => percepts;

        /// <summary>The latest decision, or null before the first one.</summary>
        public StalkerDecision Decision => brain?.Current;

        public StalkerAction CurrentAction => brain != null ? brain.Current.Action : StalkerAction.Patrol;

        /// <summary>The route still to travel, as world positions on the floor (the current tile is not included).</summary>
        public IReadOnlyList<Vector3> CurrentPath => path;

        public StalkerMoveSpeed CurrentSpeed { get; private set; } = StalkerMoveSpeed.Stop;

        /// <summary>Where the body should face: the player while the drone knows where they are, otherwise null (face along the route).</summary>
        public Vector3? LookTarget { get; private set; }

        /// <summary>True while the drone has a route to follow.</summary>
        public bool IsMoving => path.Count > 0 && CurrentSpeed != StalkerMoveSpeed.Stop;

        public StalkerSettings Settings => settings;

        void Awake()
        {
            sensor = GetComponent<StalkerSensor>();
        }

        void Start()
        {
            sensor.Initialise();
            if (!sensor.IsReady)
            {
                Debug.LogWarning("[DroneStalker] Sensor is not ready (no level graph); the drone stays idle.", this);
                enabled = false;
                return;
            }
            if (uniquePatrolPerDrone) settings.PatrolSeed = unchecked(settings.PatrolSeed * 31 + GetInstanceID());
            brain = new StalkerBrain(sensor.GraphProvider.Graph, settings);
        }

        void Update()
        {
            if (brain == null) return;

            sensor.Sense(percepts, settings, Time.timeAsDouble);
            StalkerAction previous = brain.Current.Action;
            StalkerDecision d = brain.Think(percepts);

            LookTarget = brain.IsAwareOfPlayer && sensor.Player != null ? sensor.Player.position : (Vector3?)null;

            if (d.Action != previous) ActionChanged?.Invoke(previous, d.Action);
            Publish(d);

            if (d.StrikeNow && !ReferenceEquals(d, struckDecision))
            {
                struckDecision = d;
                if (sensor.Player != null) StrikeRequested?.Invoke(sensor.Player);
            }
        }

        /// <summary>Turns the decision's node route into world positions and announces it when it changes.</summary>
        void Publish(StalkerDecision d)
        {
            StalkerMoveSpeed speed = SpeedFor(d);
            bool routeChanged = !ReferenceEquals(d, publishedDecision) || !ReferenceEquals(d.Path, publishedNodes);
            if (!routeChanged && speed == CurrentSpeed) return;

            publishedDecision = d;
            publishedNodes = d.Path;
            CurrentSpeed = speed;

            path.Clear();
            if (d.Path != null)
                for (int k = 1; k < d.Path.Length; k++)   // index 0 is the tile the drone is on
                    path.Add(sensor.GraphProvider.NodePosition(d.Path[k]));

            PathChanged?.Invoke(path, CurrentSpeed);
        }

        static StalkerMoveSpeed SpeedFor(StalkerDecision d)
        {
            if (!d.HasRoute) return StalkerMoveSpeed.Stop;
            switch (d.Action)
            {
                case StalkerAction.Patrol: return StalkerMoveSpeed.Patrol;
                case StalkerAction.Strike:
                case StalkerAction.Retreat: return StalkerMoveSpeed.Chase;
                default: return StalkerMoveSpeed.Investigate;   // Stalk, Hide, Ambush: move carefully
            }
        }

        /// <summary>Forgets the player and starts patrolling again (for example after the player respawns).</summary>
        public void ResetAgent()
        {
            if (!sensor.IsReady) return;
            brain = new StalkerBrain(sensor.GraphProvider.Graph, settings);
            publishedDecision = null;
            publishedNodes = null;
            struckDecision = null;
            path.Clear();
            CurrentSpeed = StalkerMoveSpeed.Stop;
            LookTarget = null;
        }
    }
}

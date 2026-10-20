using System.Collections.Generic;
using System.Diagnostics;
using LabLockdown.AI.Agents;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LabLockdown.AI.Flanker.Runtime
{
    /// <summary>
    /// Unity "body" around the Tactical Flanker's planner. Every <see cref="senseInterval"/> seconds it
    /// <b>senses</b> (player tile, position and facing, what the player can see, locked doors, own health),
    /// checks whether the current plan still fits, and <b>re-plans</b> with <see cref="FlankerPlanner"/> if not.
    /// Every frame it <b>acts</b> on the current step: walks the step's tile route through the robot's
    /// <see cref="IPathFollower"/>, or faces the player and fires through its <see cref="IRobotWeapon"/>.
    /// The brain never moves the robot itself, so the same body serves the Security Hunter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlankerAgent : MonoBehaviour
    {
        [Header("Thinking")]
        [Tooltip("Seconds between sensing the world and checking the plan.")]
        [SerializeField] private float senseInterval = 0.25f;
        [Tooltip("Re-plan if an attack has had no line of fire for this long (e.g. the player stepped behind a pillar).")]
        [SerializeField] private float attackPatience = 2f;
        [Tooltip("Player more than this far (m) from every tile centre, or below y = -2, is treated as off the level.")]
        [SerializeField] private float maxOffGraphDistance = 3f;

        [Header("Tuning (see Docs/TacticalFlanker.md)")]
        [SerializeField] private float viewHalfAngleDegrees = 55f;
        [SerializeField] private float attackRange = 12f;
        [SerializeField] private float exposurePenalty = 2f;
        [SerializeField] private float headOnPenalty = 30f;
        [SerializeField, Range(0f, 1f)] private float lowHealth01 = 0.3f;

        [Header("Debug")]
        [Tooltip("Show every flanker's plan in the top-right corner.")]
        [SerializeField] private bool showOverlay = true;
        [Tooltip("Draw the planned routes, and for the selected flanker the tiles the player holds.")]
        [SerializeField] private bool drawGizmos = true;

        private static readonly List<FlankerAgent> agents = new List<FlankerAgent>();

        // Statics survive between Play sessions when domain reload is off (Enter Play Mode options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => agents.Clear();

        private LevelGraphProvider graph;
        private PlayerVisibilitySensor sensor;
        private FlankerPlanner planner;
        private IPathFollower follower;
        private IRobotWeapon weapon;
        private IRobotHealth health;
        private Transform player;

        private readonly HashSet<string> lockedDoors = new HashSet<string>();
        private readonly List<Vector3> waypoints = new List<Vector3>();
        private float nextSense;
        private int stepIndex;
        private bool stepStarted;
        private bool forceReplan;
        private float noLineOfFire;
        private bool dead;

        public FlankerDecision Decision { get; private set; }
        public FlankerStep CurrentStep => Decision != null && stepIndex < Decision.Steps.Count ? Decision.Steps[stepIndex] : null;
        public string Status { get; private set; } = "Starting";
        public int Decisions { get; private set; }
        public double LastDecisionMs { get; private set; }
        public double MaxDecisionMs { get; private set; }

        private void Start()
        {
            graph = FindAnyObjectByType<LevelGraphProvider>();
            follower = GetComponent<IPathFollower>();
            weapon = GetComponent<IRobotWeapon>();
            health = GetComponent<IRobotHealth>();

            if (graph == null || follower == null)
            {
                Debug.LogWarning($"[Flanker] {name} needs a LevelGraphProvider in the scene and an IPathFollower on the robot; disabled.", this);
                enabled = false;
                return;
            }

            planner = new FlankerPlanner(graph.Graph, new FlankerSettings
            {
                ViewHalfAngleDegrees = viewHalfAngleDegrees,
                AttackRange = weapon != null ? Mathf.Min(attackRange, weapon.Range) : attackRange,
                ExposurePenalty = exposurePenalty,
                HeadOnPenalty = headOnPenalty,
                LowHealth01 = lowHealth01,
            });

            sensor = PlayerVisibilitySensor.Get(graph);
            follower.PathEnded += OnPathEnded;
            if (health != null) health.Died += OnDied;
        }

        private void OnEnable()
        {
            agents.Add(this);
            FacilityEvents.DoorLockChanged += OnDoorLockChanged;
        }

        private void OnDisable()
        {
            agents.Remove(this);
            FacilityEvents.DoorLockChanged -= OnDoorLockChanged;
        }

        private void OnDestroy()
        {
            if (follower != null) follower.PathEnded -= OnPathEnded;
            if (health != null) health.Died -= OnDied;
        }

        private void OnDoorLockChanged(string doorId, bool locked) => nextSense = 0f; // sense now, not at the next tick

        private void OnDied()
        {
            dead = true;
            Status = "Dead";
            follower.FaceTarget(null);
            enabled = false;
        }

        private void Update()
        {
            if (dead) return;

            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged == null)
                {
                    Status = "Waiting: no object tagged Player";
                    return;
                }

                player = tagged.transform;
            }

            if (Time.time >= nextSense)
            {
                nextSense = Time.time + senseInterval;
                Think();
            }

            Act();
        }

        // ---------- Sense and decide ----------

        private void Think()
        {
            var percepts = Sense(out string offGraph);
            if (percepts == null)
            {
                Status = offGraph;
                return;
            }

            bool replan = forceReplan || Decision == null || !planner.IsStillValid(Decision, percepts);
            if (!replan) return;

            var clock = Stopwatch.StartNew();
            Decision = planner.Decide(percepts);
            clock.Stop();

            LastDecisionMs = clock.Elapsed.TotalMilliseconds;
            if (LastDecisionMs > MaxDecisionMs) MaxDecisionMs = LastDecisionMs;
            Decisions++;
            forceReplan = false;
            stepIndex = 0;
            stepStarted = false;
            noLineOfFire = 0f;
            Status = Decision.Summary;
        }

        private FlankerPercepts Sense(out string waitingReason)
        {
            waitingReason = null;
            Vector3 p = player.position;
            int playerNode = graph.NearestNode(p);
            Vector3 tile = graph.NodePosition(playerNode);
            if (p.y < -2f || new Vector2(p.x - tile.x, p.z - tile.z).magnitude > maxOffGraphDistance)
            {
                waitingReason = "Waiting: player is off the level graph";
                return null;
            }

            lockedDoors.Clear();
            foreach (var door in FacilityEvents.LockedDoors) lockedDoors.Add(door);

            Vector3 forward = player.forward;
            return new FlankerPercepts
            {
                AgentNode = graph.NearestNode(transform.position),
                AgentHealth01 = health != null ? health.Health01 : 1f,
                PlayerNode = playerNode,
                PlayerX = p.x,
                PlayerZ = p.z,
                PlayerForwardX = forward.x,
                PlayerForwardZ = forward.z,
                VisibleNodes = sensor.VisibleNodes,
                LockedDoors = lockedDoors,
            };
        }

        // ---------- Act ----------

        private void Act()
        {
            var step = CurrentStep;
            if (step == null)
            {
                // Plan finished or empty (holding in cover): stand still and wait for a re-plan.
                follower.FaceTarget(null);
                return;
            }

            if (step.IsMove)
            {
                if (stepStarted) return;
                stepStarted = true;

                waypoints.Clear();
                foreach (int node in step.Path) waypoints.Add(graph.NodePosition(node));
                follower.FaceTarget(null);
                follower.SetPath(waypoints, MoveSpeed.Chase);
                return;
            }

            // Attack: stand, face the player, fire whenever the gun is ready.
            if (!stepStarted)
            {
                stepStarted = true;
                follower.Stop();
            }

            follower.FaceTarget(player);
            if (weapon == null) return;

            if (weapon.CanHit(player))
            {
                noLineOfFire = 0f;
                weapon.TryFire(player);
            }
            else
            {
                noLineOfFire += Time.deltaTime;
                if (noLineOfFire >= attackPatience)
                {
                    forceReplan = true;
                    nextSense = 0f;
                }
            }
        }

        private void OnPathEnded(PathEndReason reason)
        {
            var step = CurrentStep;
            if (step == null || !step.IsMove || !stepStarted) return;

            switch (reason)
            {
                case PathEndReason.Arrived:
                    stepIndex++;
                    stepStarted = false;
                    nextSense = 0f; // check the plan from the new position straight away
                    break;
                case PathEndReason.Stuck:
                    forceReplan = true;
                    nextSense = 0f;
                    break;
                // Replaced and Stopped are caused by this script.
            }
        }

        // ---------- Debug ----------

        private void OnGUI()
        {
            if (!showOverlay || agents.Count == 0 || agents[0] != this) return; // the first flanker draws for all

            const float width = 460f;
            float height = 30f + 38f * agents.Count;
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, height), GUI.skin.box);
            GUILayout.Label($"<b>Tactical Flankers</b> ({agents.Count})", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var agent in agents)
            {
                var step = agent.CurrentStep;
                string doing = step == null ? "—" : step.Kind.ToString();
                GUILayout.Label($"{agent.name}: <b>{doing}</b>  ·  {agent.Decisions} plans, last {agent.LastDecisionMs:0.00} ms, max {agent.MaxDecisionMs:0.00} ms\n   {agent.Status}",
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 11 });
            }

            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || Decision == null || graph == null) return;

            for (int s = stepIndex; s < Decision.Steps.Count; s++)
            {
                var step = Decision.Steps[s];
                if (!step.IsMove)
                {
                    if (player != null)
                    {
                        Gizmos.color = step.Kind == FlankerStepKind.AttackFromFlank ? Color.green : Color.red;
                        Gizmos.DrawLine(transform.position + Vector3.up * 1.4f, player.position + Vector3.up * 1.2f);
                    }

                    continue;
                }

                Gizmos.color = step.Kind == FlankerStepKind.Flank ? Color.green
                    : step.Kind == FlankerStepKind.TakeCover ? Color.cyan : Color.red;
                for (int i = 0; i + 1 < step.Path.Count; i++)
                    Gizmos.DrawLine(graph.NodePosition(step.Path[i]) + Vector3.up * 0.3f, graph.NodePosition(step.Path[i + 1]) + Vector3.up * 0.3f);
                Gizmos.DrawWireSphere(graph.NodePosition(step.TargetNode) + Vector3.up * 0.3f, 0.6f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || Decision == null || graph == null) return;

            // Red: tiles the player holds (watched). Yellow: visible but not watched (flank opportunities).
            var threat = Decision.Threat;
            for (int node = 0; node < graph.Graph.NodeCount; node++)
            {
                if (!threat.IsVisible(node)) continue;
                Gizmos.color = threat.IsWatched(node) ? new Color(1f, 0f, 0f, 0.5f) : new Color(1f, 0.9f, 0f, 0.5f);
                Gizmos.DrawCube(graph.NodePosition(node), new Vector3(3.6f, 0.05f, 3.6f));
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using GVGame.Combat;
using GVGame.Interaction;
using GVGame.Player;
using LabLockdown.AI.Facility;

namespace LabLockdown.AI.Hunter.Runtime
{
    /// <summary>
    /// Puts the Hunter's brain into the game. This is the only Unity-aware class of the AI: it turns scene
    /// events into brain inputs and the brain's command into movement and shots.
    ///
    ///   noise events, being shot, line of sight  ->  HunterBrain.Think  ->  A* path  ->  HunterMover
    ///                                                                     ->  HunterShooter (hitscan)
    ///
    /// The brain never sees the player's real position, only what this class reports through line of sight.
    /// All decisions live in LabLockdown.AI.Hunter.Core and are unit tested; this class has no tuning of its own
    /// beyond the senses and door handling below (see Docs/HunterWeights.md).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HunterHealth), typeof(HunterMover), typeof(HunterShooter))]
    public class HunterAgent : MonoBehaviour
    {
        [Header("Senses")]
        public float eyeHeight = 1.5f;
        public float viewDistance = 30f;
        [Range(10f, 360f)] public float fieldOfView = 140f;
        [Tooltip("Within this distance the Hunter notices the player even outside its field of view (metres).")]
        public float closeSenseDistance = 3f;
        public LayerMask sightMask = ~0;

        [Header("Movement")]
        [Tooltip("Chase speed in m/s. Group balance: 3.")]
        public float baseSpeed = 3f;
        [Range(0.1f, 1f)] public float patrolSpeedFactor = 0.5f;
        [Range(0.1f, 1f)] public float investigateSpeedFactor = 0.75f;

        [Header("Doors")]
        [Tooltip("Open a closed door when the Hunter is this close to it (metres).")]
        public float doorOpenDistance = 2.5f;
        [Tooltip("A door counts as passable once it is this far open (0 closed, 1 fully open).")]
        [Range(0.1f, 1f)] public float doorPassFraction = 0.6f;
        [Tooltip("Waiting at a door this long without it opening means it is jammed (seconds).")]
        public float doorJamSeconds = 2f;

        [Header("Brain")]
        public float thinkInterval = 0.1f;

        [Header("Debug")]
        public bool showOverlay = true;

        private LevelGraph graph;
        private EdgeCostModel edgeCosts;
        private AStar astar;
        private HunterBrain brain;
        private DoorSceneStates doorScene;
        private BlockedDoorMemory blocked;
        private HunterHealth health;
        private HunterMover mover;
        private HunterShooter shooter;
        private PlayerHealth player;

        private readonly List<int> plan = new List<int>();
        private readonly List<string> planDoors = new List<string>();
        private readonly List<Vector3> waypoints = new List<Vector3>();

        private float nextThink;
        private float nextPlayerSearch;
        private float nextDoorRefresh;
        private float jamTimer;
        private float floorY;
        private bool replanRequested;
        private HunterCommand last;
        private bool hasLast;

        private static int instanceCount;
        private int overlayIndex;

        void Awake()
        {
            health = GetComponent<HunterHealth>();
            mover = GetComponent<HunterMover>();
            shooter = GetComponent<HunterShooter>();

            graph = LabLockdownLayout.CreateGraph();
            edgeCosts = new EdgeCostModel();
            astar = new AStar(graph, edgeCosts);
            doorScene = new DoorSceneStates();
            blocked = new BlockedDoorMemory(doorScene);

            var attack = new AttackModel(new AttackParameters(), () => Random.value);
            brain = new HunterBrain(graph, new BeliefMap(graph.NodeCount), new SoundModel(), edgeCosts, attack);

            mover.Gate = CanAdvance;
            overlayIndex = instanceCount++;
        }

        void OnEnable()
        {
            InteractionNoise.NoiseEmitted += OnNoise;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            InteractionNoise.NoiseEmitted -= OnNoise;
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void OnDestroy()
        {
            instanceCount = Mathf.Max(0, instanceCount - 1);
        }

        void Start()
        {
            floorY = transform.position.y;
            doorScene.Refresh();
        }

        void Update()
        {
            float now = Time.time;
            blocked.Now = now;

            if (now >= nextDoorRefresh)
            {
                nextDoorRefresh = now + 5f;
                doorScene.Refresh();   // doors may be built or removed after the Hunter spawns
            }

            if (player == null && now >= nextPlayerSearch)
            {
                nextPlayerSearch = now + 1f;
                player = FindFirstObjectByType<PlayerHealth>();
            }

            if (now >= nextThink)
            {
                nextThink = now + thinkInterval;
                Think();
            }

            ServiceDoors();
        }

        // ---------------------------------------------------------------- perceive, decide, act

        private void Think()
        {
            Vector3 position = transform.position;
            int node = graph.FindNearestNode(position.x, position.z);

            bool sees = false;
            int playerNode = -1;
            float distance = 0f;
            if (player != null && player.IsAlive)
            {
                sees = CanSee(player.transform, out distance);
                if (sees)
                {
                    Vector3 p = player.transform.position;
                    playerNode = graph.FindNearestNode(p.x, p.z);
                }
            }

            var percepts = new HunterPercepts
            {
                Now = Time.time,
                HunterNode = node,
                IsAlive = health.IsAlive,
                HasLineOfSight = sees,
                PlayerNode = playerNode,
                PlayerDistance = distance
            };

            last = brain.Think(percepts, blocked);
            hasLast = true;
            Apply(last, node);
        }

        private void Apply(HunterCommand cmd, int node)
        {
            if (cmd.State == HunterState.Dead)
            {
                mover.Stop();
                mover.ClearFacing();
                return;
            }

            if (cmd.HoldPosition)
            {
                mover.Stop();
                if (player != null) mover.FaceTarget(player.transform);
                if (cmd.Attack.Event == AttackEvent.Fired && player != null)
                    shooter.Fire(player, brain.Attack.Parameters.Damage, cmd.Attack.Hit);
                return;
            }

            mover.ClearFacing();
            if (cmd.TargetNode < 0 || cmd.TargetNode == node)
            {
                mover.Stop();
                return;
            }

            float speed = SpeedFor(cmd.Gait);
            bool needPlan = cmd.TargetChanged || replanRequested || !mover.IsMoving || !PlanIsValid(cmd.TargetNode);
            if (needPlan) Plan(node, cmd.TargetNode, speed);
            else mover.SetSpeed(speed);
        }

        private bool CanSee(Transform target, out float distance)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = HunterShooter.AimPoint(target);
            Vector3 toTarget = aim - eye;
            distance = toTarget.magnitude;

            if (distance < 0.01f || distance > viewDistance) return false;
            if (distance > closeSenseDistance && Vector3.Angle(transform.forward, toTarget) > fieldOfView * 0.5f) return false;

            return HunterShooter.NearestHit(transform, eye, toTarget / distance, distance + 0.5f, sightMask, out RaycastHit hit)
                   && hit.collider.GetComponentInParent<PlayerHealth>() != null;
        }

        // ---------------------------------------------------------------- planning and movement

        private float SpeedFor(HunterGait gait)
        {
            switch (gait)
            {
                case HunterGait.Patrol: return baseSpeed * patrolSpeedFactor;
                case HunterGait.Investigate: return baseSpeed * investigateSpeedFactor;
                default: return baseSpeed;
            }
        }

        private void Plan(int fromNode, int toNode, float speed)
        {
            replanRequested = false;
            plan.Clear();
            planDoors.Clear();
            waypoints.Clear();

            PathResult result = astar.FindPath(fromNode, toNode, blocked);
            if (!result.Found || result.Nodes.Count < 2)
            {
                mover.Stop();
                return;
            }

            BuildWaypoints(result.Nodes);
            mover.SetPath(waypoints, speed);
        }

        private void BuildWaypoints(IReadOnlyList<int> nodes)
        {
            for (int i = 0; i < nodes.Count; i++) plan.Add(nodes[i]);

            // If the Hunter is already past the first node on its way to the second, do not walk back to it.
            GraphNode first = graph.Nodes[nodes[0]];
            GraphNode second = graph.Nodes[nodes[1]];
            Vector3 position = transform.position;
            float alongX = second.X - first.X, alongZ = second.Z - first.Z;
            float fromX = position.x - first.X, fromZ = position.z - first.Z;
            bool skipFirst = alongX * fromX + alongZ * fromZ > 0f;

            for (int i = 0; i < nodes.Count; i++)
            {
                GraphNode node = graph.Nodes[nodes[i]];
                if (!(i == 0 && skipFirst)) AddWaypoint(node.X, node.Z, null);

                if (i + 1 < nodes.Count)
                {
                    GraphEdge edge = EdgeBetween(nodes[i], nodes[i + 1]);
                    if (edge != null && edge.IsDoor)
                    {
                        GraphNode next = graph.Nodes[nodes[i + 1]];
                        AddWaypoint((node.X + next.X) * 0.5f, (node.Z + next.Z) * 0.5f, edge.DoorId);
                    }
                }
            }
        }

        private void AddWaypoint(float x, float z, string doorId)
        {
            waypoints.Add(new Vector3(x, floorY, z));
            planDoors.Add(doorId);
        }

        private GraphEdge EdgeBetween(int a, int b)
        {
            foreach (GraphEdge edge in graph.EdgesOf(a))
                if (edge.Other(a) == b) return edge;
            return null;
        }

        // The current plan stays valid while it still ends at the target and no door on it has become impassable.
        private bool PlanIsValid(int target)
        {
            if (plan.Count < 2 || plan[plan.Count - 1] != target) return false;
            for (int i = 0; i + 1 < plan.Count; i++)
            {
                GraphEdge edge = EdgeBetween(plan[i], plan[i + 1]);
                if (edge == null || float.IsPositiveInfinity(edgeCosts.Cost(edge, blocked))) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- doors

        // May the body go through this waypoint yet? Door waypoints need the door open far enough.
        private bool CanAdvance(int waypointIndex)
        {
            if (waypointIndex < 0 || waypointIndex >= planDoors.Count) return true;
            string id = planDoors[waypointIndex];
            if (id == null) return true;
            if (!doorScene.TryGet(id, out DoorController door)) return true;
            return door.OpenFraction >= doorPassFraction;
        }

        private void ServiceDoors()
        {
            if (!mover.IsMoving)
            {
                jamTimer = 0f;
                return;
            }

            int index = mover.WaypointIndex;
            string id = index < planDoors.Count ? planDoors[index] : null;
            if (id == null || !doorScene.TryGet(id, out DoorController door))
            {
                jamTimer = 0f;
                return;
            }

            Vector3 toDoor = mover.CurrentWaypoint - transform.position;
            toDoor.y = 0f;
            bool shut = door.Current == DoorController.State.Closed || door.Current == DoorController.State.Closing;
            if (toDoor.magnitude <= doorOpenDistance && !door.IsLocked && shut)
                door.Open(transform);

            // Waiting at a door that will not open: a barricade is behind it. Remember that and go round.
            if (mover.IsHolding && door.OpenFraction < doorPassFraction)
            {
                jamTimer += Time.deltaTime;
                if (door.IsBlocked || jamTimer >= doorJamSeconds)
                {
                    blocked.MarkBlocked(id);
                    jamTimer = 0f;
                    mover.Stop();
                    replanRequested = true;
                }
            }
            else
            {
                jamTimer = 0f;
            }
        }

        // ---------------------------------------------------------------- events

        private void OnNoise(Vector3 position, float loudness, GameObject source)
        {
            if (!health.IsAlive) return;

            if (source != null)
            {
                if (source.transform.IsChildOf(transform)) return;   // the Hunter's own noise (a door it opened)

                IDamageable other = source.GetComponentInParent<IDamageable>();
                if (other != null && !(other is PlayerHealth)) return;   // another robot, not the player
            }

            int node = graph.FindNearestNode(position.x, position.z);
            brain.OnNoise(node, loudness, blocked);
        }

        private void OnDamaged(DamageInfo info)
        {
            if (info.source == null) return;
            Vector3 p = info.source.transform.position;
            brain.OnShot(graph.FindNearestNode(p.x, p.z));
        }

        private void OnDied()
        {
            mover.Stop();
            mover.ClearFacing();
        }

        // ---------------------------------------------------------------- debugging aids

        void OnGUI()
        {
            if (!showOverlay || !hasLast || brain == null) return;

            float peak = brain.Belief.MaxProbability * brain.Belief.Count;
            string text = "Hunter: " + last.State + " (" + last.Gait + ")   target node " + last.TargetNode
                          + "   belief peak x" + peak.ToString("0.0")
                          + "   jammed doors " + blocked.ActiveCount
                          + "   health " + Mathf.CeilToInt(health.Health);
            GUI.Label(new Rect(10f, 10f + overlayIndex * 22f, 900f, 22f), text);
        }

        // Select the Hunter in the Scene view while playing: red spheres show where it thinks the player is.
        void OnDrawGizmosSelected()
        {
            if (brain == null || graph == null) return;

            int n = graph.NodeCount;
            for (int i = 0; i < n; i++)
            {
                GraphNode node = graph.Nodes[i];
                float ratio = brain.Belief[i] * n;   // 1 = flat
                Gizmos.color = new Color(1f, 0.2f, 0.1f, Mathf.Clamp01(0.15f + ratio * 0.1f));
                Gizmos.DrawSphere(new Vector3(node.X, floorY + 0.3f, node.Z), 0.15f + Mathf.Min(ratio, 8f) * 0.12f);
            }

            if (brain.TargetNode >= 0)
            {
                GraphNode target = graph.Nodes[brain.TargetNode];
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(new Vector3(target.X, floorY + 0.5f, target.Z), new Vector3(1f, 1f, 1f));
            }

            Gizmos.color = Color.cyan;
            Vector3 from = transform.position;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Gizmos.DrawLine(from, waypoints[i]);
                from = waypoints[i];
            }
        }
    }
}

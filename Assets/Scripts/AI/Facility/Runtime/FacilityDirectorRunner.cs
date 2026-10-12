using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LabLockdown.AI.Facility.Director;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// The Facility AI's "body" in the scene. Senses the world (player position, health, line of sight,
    /// doors, enemies, blocked spawn points and doorways), asks the pure C# <see cref="FacilityDirector"/>
    /// for a decision when one is due, acts on it through the spawn and door services, and announces it
    /// through <see cref="FacilityEvents"/>. Also draws the debug overlay and live intensity graph.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FacilityDirectorRunner : MonoBehaviour
    {
        private const float GraphSampleSeconds = 0.5f;
        private const float OffGraphRetrySeconds = 1f;

        [Header("Scene")]
        [SerializeField] private LevelGraphProvider graphProvider;

        [Tooltip("The player. Found by the \"Player\" tag if empty.")]
        [SerializeField] private Transform player;

        [Tooltip("Eye height above the player's pivot, used for line-of-sight checks.")]
        [SerializeField] private float eyeHeight = 1.6f;

        [Tooltip("Layers that block line of sight (walls, cover, doors).")]
        [SerializeField] private LayerMask lineOfSightBlockers = ~0;

        [Header("Services: any component implementing the interface (placeholders are added if empty)")]
        [SerializeField] private MonoBehaviour spawnService;
        [SerializeField] private MonoBehaviour doorService;

        [Tooltip("Optional: the player's health source. Without it, Debug Health below is used.")]
        [SerializeField] private MonoBehaviour playerStatus;

        [Header("Director")]
        [SerializeField] private DirectorSettings settings = new DirectorSettings();

        [Header("Edge cases")]
        [Tooltip("Layers checked for something standing on a spawn point or in a doorway (enemies, props, player).")]
        [SerializeField] private LayerMask occupancyLayers = ~0;

        [Tooltip("Radius around a spawn marker that must be free of enemies and movable props.")]
        [SerializeField] private float spawnClearance = 0.75f;

        [Tooltip("Depth of the doorway checked before a door is locked (width and height follow the door).")]
        [SerializeField] private float doorwayDepth = 1.5f;

        [Tooltip("Further than this from every graph node, the player is off the level (falling, out of bounds) " +
                 "and decisions wait.")]
        [SerializeField] private float offGraphDistance = LabLockdownLayout.TileSize * 0.75f;

        [Tooltip("Below this height the player is treated as having fallen out of the level.")]
        [SerializeField] private float minPlayerHeight = -2f;

        [Header("Debug")]
        [Tooltip("Player health used when no health source is assigned. Drag it in Play mode to test relief.")]
        [Range(0f, 1f)] [SerializeField] private float debugHealth = 1f;

        [SerializeField] private float damageWindowSeconds = 5f;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showIntensityGraph = true;
        [SerializeField] private float graphWindowSeconds = 60f;
        [SerializeField] private bool logDecisions = true;

        private FacilityDirector director;
        private ISpawnService spawns;
        private IDoorService doors;
        private IPlayerStatusSource status;
        private readonly Queue<(float time, float health)> healthHistory = new Queue<(float time, float health)>();
        private readonly Dictionary<int, SpawnPointMarker> markerByNode = new Dictionary<int, SpawnPointMarker>();
        private readonly Dictionary<string, Transform> doorMarkers = new Dictionary<string, Transform>();
        private readonly Collider[] overlapBuffer = new Collider[32];
        private float lastHealth = 1f;
        private float lastDamageTime;
        private float lastEnemyTime;
        private float nextHealthSample;
        private float nextAttempt;
        private string waitingReason;
        private FacilityPercepts lastPercepts;

        // Decision timing (shown in the overlay; the Director must fit comfortably inside a frame).
        private readonly Stopwatch stopwatch = new Stopwatch();
        private double lastDecisionMs;
        private double maxDecisionMs;
        private int decisionCount;

        // Live intensity graph: ring buffer of samples.
        private struct GraphSample
        {
            public float Time, Intensity, Health;
            public DirectorPhase Phase;
        }

        private GraphSample[] graphSamples;
        private int graphHead, graphCount;
        private float nextGraphSample;
        private Texture2D pixel;

        public FacilityDirector Director => director;
        public DirectorDecision LastDecision => director?.LastDecision;

        /// <summary>Why the Director is not deciding right now (null when running normally).</summary>
        public string WaitingReason => waitingReason;

        private void Reset()
        {
            graphProvider = FindAnyObjectByType<LevelGraphProvider>();
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) player = tagged.transform;
            lineOfSightBlockers = LayerMask.GetMask("Default", "Doors");
        }

        private void OnValidate()
        {
            if (spawnService != null && !(spawnService is ISpawnService))
            {
                Debug.LogWarning($"[FacilityAI] {spawnService.GetType().Name} does not implement ISpawnService.", this);
                spawnService = null;
            }

            if (doorService != null && !(doorService is IDoorService))
            {
                Debug.LogWarning($"[FacilityAI] {doorService.GetType().Name} does not implement IDoorService.", this);
                doorService = null;
            }

            if (playerStatus != null && !(playerStatus is IPlayerStatusSource))
            {
                Debug.LogWarning($"[FacilityAI] {playerStatus.GetType().Name} does not implement IPlayerStatusSource.", this);
                playerStatus = null;
            }

            graphWindowSeconds = Mathf.Max(5f, graphWindowSeconds);
        }

        private void Start()
        {
            if (graphProvider == null) graphProvider = FindAnyObjectByType<LevelGraphProvider>();
            if (graphProvider == null)
            {
                Debug.LogError("[FacilityAI] No LevelGraphProvider in the scene; the Facility AI is disabled.", this);
                enabled = false;
                return;
            }

            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            // Use the assigned service, else one on this object, else add a placeholder.
            spawns = spawnService as ISpawnService ?? GetComponent<ISpawnService>() ?? gameObject.AddComponent<PlaceholderSpawnService>();
            doors = doorService as IDoorService ?? GetComponent<IDoorService>() ?? gameObject.AddComponent<PlaceholderDoorService>();
            status = playerStatus as IPlayerStatusSource;

            var graph = graphProvider.Graph;
            Vector3 startPosition = player != null ? player.position : new Vector3(LabLockdownLayout.PlayerStartX, 0f, LabLockdownLayout.PlayerStartZ);
            int startNode = graph.FindNearestNode(startPosition.x, startPosition.z);

            var level = new FacilityLevel(graph, graphProvider.SpawnNodes, graphProvider.ExitNode, startNode);
            director = new FacilityDirector(level, settings);
            director.Reset(Time.timeAsDouble);

            markerByNode.Clear();
            var nodes = graphProvider.SpawnNodes;
            var markers = graphProvider.SpawnMarkers;
            for (int i = 0; i < nodes.Count && i < markers.Count; i++) markerByNode[nodes[i]] = markers[i];

            doorMarkers.Clear();
            var doorRoot = graphProvider.DoorMarkersRoot;
            foreach (string id in settings.LockableDoors)
            {
                var marker = doorRoot != null ? doorRoot.Find(id) : null;
                if (marker != null) doorMarkers[id] = marker;
                else Debug.LogWarning($"[FacilityAI] No door marker {id}: its doorway cannot be checked for obstructions.", this);
            }

            graphSamples = new GraphSample[Mathf.CeilToInt(graphWindowSeconds / GraphSampleSeconds) + 1];
            graphHead = graphCount = 0;

            lastHealth = CurrentHealth();
            lastDamageTime = lastEnemyTime = Time.time;
        }

        private void OnDestroy()
        {
            if (pixel != null) Destroy(pixel);
        }

        private void Update()
        {
            if (director == null) return;
            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged == null)
                {
                    waitingReason = "No player in the scene";
                    return;
                }

                player = tagged.transform;
            }

            TrackHealthAndEnemies();
            SampleGraph();
            if (director.IsDue(Time.timeAsDouble) && Time.time >= nextAttempt) DecideNow();
        }

        /// <summary>Forces a decision immediately (also available from the component's context menu in Play mode).</summary>
        [ContextMenu("Decide Now")]
        public void DecideNow()
        {
            if (director == null || player == null) return;

            // Edge case: the player is not on the level graph (falling, clipping out, teleporting).
            // Deciding from the "nearest" node would place robots relative to the wrong room, so wait.
            if (!IsOnGraph(out int playerNode, out string reason))
            {
                waitingReason = reason;
                nextAttempt = Time.time + OffGraphRetrySeconds;
                return;
            }

            waitingReason = null;
            lastPercepts = BuildPercepts(playerNode);

            stopwatch.Restart();
            var decision = director.Decide(lastPercepts);
            stopwatch.Stop();
            lastDecisionMs = stopwatch.Elapsed.TotalMilliseconds;
            if (decisionCount++ > 0) maxDecisionMs = System.Math.Max(maxDecisionMs, lastDecisionMs); // skip JIT warm-up

            var spawnPositions = Apply(decision);
            FacilityEvents.RaiseDecision(decision, spawnPositions);

            if (logDecisions)
            {
                var log = new StringBuilder($"[FacilityAI] {decision} ({lastDecisionMs:0.0} ms)");
                foreach (string note in decision.Notes) log.Append($"\n  note: {note}");
                Debug.Log(log.ToString(), this);
            }
        }

        /// <summary>Call when the player respawns: clears the Director's memory.</summary>
        public void ResetDirector()
        {
            director?.Reset(Time.timeAsDouble);
            healthHistory.Clear();
            lastDamageTime = lastEnemyTime = Time.time;
            nextAttempt = 0f;
            waitingReason = null;
        }

        // ---------- Sensing ----------

        private float CurrentHealth() => Mathf.Clamp01(status != null ? status.Health01 : debugHealth);

        private bool IsOnGraph(out int node, out string reason)
        {
            var graph = graphProvider.Graph;
            Vector3 position = player.position;
            node = graph.FindNearestNode(position.x, position.z);
            reason = null;

            if (position.y < minPlayerHeight)
            {
                reason = $"Player is below the level (y = {position.y:0.0})";
                return false;
            }

            if (node < 0)
            {
                reason = "Level graph is empty";
                return false;
            }

            var tile = graph.Nodes[node];
            float dx = position.x - tile.X, dz = position.z - tile.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance > offGraphDistance)
            {
                reason = $"Player is off the level graph ({distance:0.0} m from {tile.Area})";
                return false;
            }

            return true;
        }

        private void TrackHealthAndEnemies()
        {
            float health = CurrentHealth();
            if (health < lastHealth - 0.001f) lastDamageTime = Time.time;
            lastHealth = health;

            if (spawns.AliveCount > 0) lastEnemyTime = Time.time;

            // Sample health 10 times a second; keep only the damage window.
            if (Time.time >= nextHealthSample)
            {
                nextHealthSample = Time.time + 0.1f;
                healthHistory.Enqueue((Time.time, health));
            }

            while (healthHistory.Count > 0 && healthHistory.Peek().time < Time.time - damageWindowSeconds)
                healthHistory.Dequeue();
        }

        /// <summary>Health, damage, calm and enemy count: cheap, used for every graph sample.</summary>
        private FacilityPercepts BuildBasePercepts(int playerNode)
        {
            float health = CurrentHealth();
            float highest = health;
            foreach (var sample in healthHistory)
                if (sample.health > highest) highest = sample.health;

            return new FacilityPercepts
            {
                Time = Time.timeAsDouble,
                PlayerNode = playerNode,
                Health01 = health,
                RecentDamage01 = Mathf.Clamp01(highest - health),
                CalmSeconds = Time.time - Mathf.Max(lastDamageTime, lastEnemyTime),
                EnemiesAlive = spawns.AliveCount,
            };
        }

        /// <summary>Full percepts for a decision: adds line of sight, doors, occupied spawns and blocked doorways.</summary>
        private FacilityPercepts BuildPercepts(int playerNode)
        {
            var percepts = BuildBasePercepts(playerNode);

            Vector3 eye = player.position + Vector3.up * eyeHeight;
            foreach (int node in graphProvider.SpawnNodes)
            {
                Vector3 spawn = SpawnPosition(node);
                if (!Physics.Linecast(eye, spawn + Vector3.up * 1.2f, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
                    percepts.VisibleNodes.Add(node);

                if (IsSpawnOccupied(node, spawn)) percepts.OccupiedSpawnNodes.Add(node);
            }

            foreach (string door in settings.LockableDoors)
            {
                if (doors.IsLocked(door)) percepts.LockedDoors.Add(door);
                else if (IsDoorwayObstructed(door)) percepts.ObstructedDoors.Add(door); // only open doors can be obstructed
            }

            return percepts;
        }

        private bool IsSpawnOccupied(int node, Vector3 spawn)
        {
            markerByNode.TryGetValue(node, out var marker);
            int count = Physics.OverlapSphereNonAlloc(spawn + Vector3.up * 1f, spawnClearance, overlapBuffer, occupancyLayers, QueryTriggerInteraction.Ignore);
            return ContainsMovableObject(count, marker != null ? marker.transform : null);
        }

        private bool IsDoorwayObstructed(string door)
        {
            if (!doorMarkers.TryGetValue(door, out var marker) || marker == null) return false;

            // Same frame as DoorLockBarrier: x across the doorway (4 m), z through it.
            var halfExtents = new Vector3(LabLockdownLayout.TileSize * 0.5f, 1f, doorwayDepth * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(marker.position + Vector3.up * 1f, halfExtents, overlapBuffer, marker.rotation,
                occupancyLayers, QueryTriggerInteraction.Ignore);
            return ContainsMovableObject(count, marker);
        }

        /// <summary>
        /// True if the overlap found an enemy, the player, or a physics prop. Walls, floors and the door itself
        /// (anything under the marker) do not count.
        /// </summary>
        private bool ContainsMovableObject(int count, Transform ignoreRoot)
        {
            for (int i = 0; i < count; i++)
            {
                var hit = overlapBuffer[i];
                if (hit == null) continue;
                if (ignoreRoot != null && hit.transform.IsChildOf(ignoreRoot)) continue;
                if (hit.attachedRigidbody != null || hit is CharacterController || hit.GetComponentInParent<NavMeshAgent>() != null)
                    return true;
            }

            return false;
        }

        // ---------- Acting ----------

        private List<Vector3> Apply(DirectorDecision decision)
        {
            var positions = new List<Vector3>(decision.SpawnNodes.Count);
            foreach (int node in decision.SpawnNodes)
            {
                Vector3 position = SpawnPosition(node);
                Vector3 toPlayer = player.position - position;
                toPlayer.y = 0f;
                Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
                string id = markerByNode.TryGetValue(node, out var marker) ? marker.SpawnId : graphProvider.Graph.Nodes[node].ToString();
                spawns.Spawn(position, rotation, id);
                positions.Add(position);
            }

            foreach (string door in decision.DoorsToLock) doors.SetLocked(door, true);
            foreach (string door in decision.DoorsToUnlock) doors.SetLocked(door, false);
            return positions;
        }

        private Vector3 SpawnPosition(int node) =>
            markerByNode.TryGetValue(node, out var marker) && marker != null
                ? marker.transform.position
                : graphProvider.NodePosition(node);

        // ---------- Debug: live intensity graph ----------

        private void SampleGraph()
        {
            if (graphSamples == null || Time.time < nextGraphSample) return;
            nextGraphSample = Time.time + GraphSampleSeconds;

            int node = graphProvider.NearestNode(player.position);
            var percepts = BuildBasePercepts(node);
            var decision = director.LastDecision;

            graphSamples[graphHead] = new GraphSample
            {
                Time = Time.time,
                Intensity = director.PreviewIntensity(percepts),
                Health = percepts.Health01,
                Phase = decision != null ? decision.Evaluation.Phase : DirectorPhase.Relief,
            };
            graphHead = (graphHead + 1) % graphSamples.Length;
            graphCount = Mathf.Min(graphCount + 1, graphSamples.Length);
        }

        private GraphSample SampleAt(int i) => graphSamples[(graphHead - graphCount + i + graphSamples.Length) % graphSamples.Length];

        private static readonly Color IntensityColour = new Color(0.165f, 0.471f, 0.839f);   // #2a78d6
        private static readonly Color HealthColour = new Color(0.922f, 0.408f, 0.204f);      // #eb6834
        private static readonly Color BuildUpBand = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color PeakBand = new Color(1f, 1f, 1f, 0.25f);

        private void DrawIntensityGraph(Rect area)
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }

            GUI.Box(area, GUIContent.none);
            var plot = new Rect(area.x + 8, area.y + 20, area.width - 16, area.height - 28);
            float now = Time.time;
            float X(float time) => plot.x + plot.width * Mathf.Clamp01(1f - (now - time) / graphWindowSeconds);
            float Y(float value) => plot.yMax - plot.height * Mathf.Clamp01(value);

            var previousColour = GUI.color;

            // Phase bands
            for (int i = 0; i + 1 < graphCount; i++)
            {
                var a = SampleAt(i);
                if (a.Phase == DirectorPhase.Relief) continue;
                float x0 = X(a.Time), x1 = X(SampleAt(i + 1).Time);
                Fill(new Rect(x0, plot.y, Mathf.Max(1f, x1 - x0), plot.height), a.Phase == DirectorPhase.Peak ? PeakBand : BuildUpBand);
            }

            // Thresholds
            Fill(new Rect(plot.x, Y(settings.LockThreshold), plot.width, 1), new Color(1f, 1f, 1f, 0.35f));
            Fill(new Rect(plot.x, Y(settings.PeakThreshold), plot.width, 1), new Color(1f, 1f, 1f, 0.35f));

            // Lines (drawn as connected horizontal and vertical 2 px segments)
            for (int i = 0; i + 1 < graphCount; i++)
            {
                var a = SampleAt(i);
                var b = SampleAt(i + 1);
                Segment(X(a.Time), X(b.Time), Y(a.Health), Y(b.Health), HealthColour);
                Segment(X(a.Time), X(b.Time), Y(a.Intensity), Y(b.Intensity), IntensityColour);
            }

            GUI.color = previousColour;
            GUI.Label(new Rect(area.x + 8, area.y + 2, area.width - 16, 18),
                $"<color=#5b9be6>intensity</color>  <color=#f08a5d>health</color>   bands: build-up / peak   lines: lock {settings.LockThreshold:0.00}, peak {settings.PeakThreshold:0.00}   last {graphWindowSeconds:0} s",
                new GUIStyle(GUI.skin.label) { richText = true, fontSize = 10 });
        }

        private void Segment(float x0, float x1, float y0, float y1, Color colour)
        {
            Fill(new Rect(x0, y0 - 1, Mathf.Max(1f, x1 - x0), 2), colour);
            if (Mathf.Abs(y1 - y0) > 1f) Fill(new Rect(x1 - 1, Mathf.Min(y0, y1) - 1, 2, Mathf.Abs(y1 - y0) + 2), colour);
        }

        private void Fill(Rect rect, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(rect, pixel);
        }

        // ---------- Debug: overlay ----------

        private void OnGUI()
        {
            if (!showOverlay || director == null) return;

            var decision = director.LastDecision;
            var text = new StringBuilder();
            text.AppendLine("<b>Facility AI</b>");
            text.AppendLine($"Health {CurrentHealth():P0}   enemies {spawns.AliveCount}   next decision in {Mathf.Max(0f, (float)(director.NextDecisionTime - Time.timeAsDouble)):0.0} s");
            if (waitingReason != null) text.AppendLine($"<color=#f0b429>Waiting: {waitingReason}</color>");

            if (decision != null)
            {
                var graph = graphProvider.Graph;
                text.AppendLine($"<b>{decision.Evaluation.Phase}</b>   intensity {decision.Evaluation.Intensity:F2}   progress {decision.Progress:P0}");
                text.AppendLine(decision.Evaluation.Reason);

                var spawnAreas = new List<string>();
                foreach (int node in decision.SpawnNodes) spawnAreas.Add(graph.Nodes[node].Area);
                text.AppendLine($"Spawned {decision.SpawnNodes.Count}/{decision.RequestedSpawns}: {string.Join(", ", spawnAreas)}");

                var locked = new List<string>();
                foreach (var door in decision.DoorLocks)
                    if (door.Value) locked.Add(door.Key.Replace("Door_", ""));
                text.AppendLine($"Locked: {(locked.Count > 0 ? string.Join(", ", locked) : "none")}");

                if (lastPercepts != null && (lastPercepts.OccupiedSpawnNodes.Count > 0 || lastPercepts.ObstructedDoors.Count > 0))
                    text.AppendLine($"Blocked: {lastPercepts.OccupiedSpawnNodes.Count} spawn point(s), " +
                                    $"{lastPercepts.ObstructedDoors.Count} doorway(s)");

                foreach (string rule in decision.Rules) text.AppendLine($"  • {rule}");
                foreach (string note in decision.Notes) text.AppendLine($"  ! {note}");
                if (decision.Stats != null)
                    text.AppendLine($"Solver: {decision.Stats.NodesExpanded} nodes, {decision.Stats.SolutionsFound} solutions, " +
                                    $"{decision.Stats.BoundPrunes} bound prunes{(decision.IsProvenOptimal ? ", optimal" : "")}   " +
                                    $"{lastDecisionMs:0.0} ms (max {maxDecisionMs:0.0})");
            }

            const float width = 430f;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true, fontSize = 12 };
            var content = new GUIContent(text.ToString());
            float height = style.CalcHeight(content, width);
            GUI.Box(new Rect(10, 10, width, height), content, style);

            if (showIntensityGraph && graphCount > 1) DrawIntensityGraph(new Rect(10, 16 + height, width, 120));
        }

        private void OnDrawGizmos()
        {
            if (director == null || graphProvider == null) return;

            if (lastPercepts != null)
            {
                Gizmos.color = Color.yellow;
                foreach (int node in lastPercepts.VisibleNodes) Gizmos.DrawWireSphere(SpawnPosition(node) + Vector3.up * 1.2f, 0.6f);

                Gizmos.color = Color.magenta;
                foreach (int node in lastPercepts.OccupiedSpawnNodes) Gizmos.DrawWireSphere(SpawnPosition(node) + Vector3.up, spawnClearance);
                foreach (string door in lastPercepts.ObstructedDoors)
                {
                    if (!doorMarkers.TryGetValue(door, out var marker) || marker == null) continue;
                    Gizmos.matrix = Matrix4x4.TRS(marker.position + Vector3.up, marker.rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(LabLockdownLayout.TileSize, 2f, doorwayDepth));
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }

            var decision = director.LastDecision;
            if (decision == null) return;

            Gizmos.color = Color.red;
            foreach (int node in decision.SpawnNodes)
            {
                Vector3 position = SpawnPosition(node);
                Gizmos.DrawSphere(position + Vector3.up * 2.5f, 0.4f);
                if (player != null) Gizmos.DrawLine(position + Vector3.up, player.position + Vector3.up);
            }
        }
    }
}

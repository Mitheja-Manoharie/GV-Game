using System.Collections.Generic;
using System.Text;
using LabLockdown.AI.Facility.Director;
using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// The Facility AI's "body" in the scene. Senses the world (player position, health, line of sight,
    /// doors, enemies), asks the pure C# <see cref="FacilityDirector"/> for a decision when one is due,
    /// and acts on it through the spawn and door services. Also draws the debug overlay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FacilityDirectorRunner : MonoBehaviour
    {
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

        [Header("Debug")]
        [Tooltip("Player health used when no health source is assigned. Drag it in Play mode to test relief.")]
        [Range(0f, 1f)] [SerializeField] private float debugHealth = 1f;

        [SerializeField] private float damageWindowSeconds = 5f;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logDecisions = true;

        private FacilityDirector director;
        private ISpawnService spawns;
        private IDoorService doors;
        private IPlayerStatusSource status;
        private readonly Queue<(float time, float health)> healthHistory = new Queue<(float time, float health)>();
        private readonly Dictionary<int, SpawnPointMarker> markerByNode = new Dictionary<int, SpawnPointMarker>();
        private float lastHealth = 1f;
        private float lastDamageTime;
        private float lastEnemyTime;
        private float nextHealthSample;
        private FacilityPercepts lastPercepts;

        public FacilityDirector Director => director;
        public DirectorDecision LastDecision => director?.LastDecision;

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

            lastHealth = CurrentHealth();
            lastDamageTime = lastEnemyTime = Time.time;
        }

        private void Update()
        {
            if (director == null) return;
            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged == null) return;
                player = tagged.transform;
            }

            TrackHealthAndEnemies();
            if (director.IsDue(Time.timeAsDouble)) DecideNow();
        }

        /// <summary>Forces a decision immediately (also available from the component's context menu in Play mode).</summary>
        [ContextMenu("Decide Now")]
        public void DecideNow()
        {
            if (director == null || player == null) return;

            lastPercepts = BuildPercepts();
            var decision = director.Decide(lastPercepts);
            Apply(decision);

            if (logDecisions)
            {
                var log = new StringBuilder($"[FacilityAI] {decision}");
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
        }

        // ---------- Sensing ----------

        private float CurrentHealth() => Mathf.Clamp01(status != null ? status.Health01 : debugHealth);

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

        private FacilityPercepts BuildPercepts()
        {
            var graph = graphProvider.Graph;
            float health = CurrentHealth();

            float highest = health;
            foreach (var sample in healthHistory)
                if (sample.health > highest) highest = sample.health;

            var percepts = new FacilityPercepts
            {
                Time = Time.timeAsDouble,
                PlayerNode = graph.FindNearestNode(player.position.x, player.position.z),
                Health01 = health,
                RecentDamage01 = Mathf.Clamp01(highest - health),
                CalmSeconds = Time.time - Mathf.Max(lastDamageTime, lastEnemyTime),
                EnemiesAlive = spawns.AliveCount,
            };

            Vector3 eye = player.position + Vector3.up * eyeHeight;
            foreach (int node in graphProvider.SpawnNodes)
            {
                Vector3 target = SpawnPosition(node) + Vector3.up * 1.2f;
                if (!Physics.Linecast(eye, target, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
                    percepts.VisibleNodes.Add(node);
            }

            foreach (string door in settings.LockableDoors)
                if (doors.IsLocked(door)) percepts.LockedDoors.Add(door);

            return percepts;
        }

        // ---------- Acting ----------

        private void Apply(DirectorDecision decision)
        {
            foreach (int node in decision.SpawnNodes)
            {
                Vector3 position = SpawnPosition(node);
                Vector3 toPlayer = player.position - position;
                toPlayer.y = 0f;
                Quaternion rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
                string id = markerByNode.TryGetValue(node, out var marker) ? marker.SpawnId : graphProvider.Graph.Nodes[node].ToString();
                spawns.Spawn(position, rotation, id);
            }

            foreach (string door in decision.DoorsToLock) doors.SetLocked(door, true);
            foreach (string door in decision.DoorsToUnlock) doors.SetLocked(door, false);
        }

        private Vector3 SpawnPosition(int node) =>
            markerByNode.TryGetValue(node, out var marker) && marker != null
                ? marker.transform.position
                : graphProvider.NodePosition(node);

        // ---------- Debug ----------

        private void OnGUI()
        {
            if (!showOverlay || director == null) return;

            var decision = director.LastDecision;
            var text = new StringBuilder();
            text.AppendLine("<b>Facility AI</b>");
            text.AppendLine($"Health {CurrentHealth():P0}   enemies {spawns.AliveCount}   next decision in {Mathf.Max(0f, (float)(director.NextDecisionTime - Time.timeAsDouble)):0.0} s");

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

                foreach (string rule in decision.Rules) text.AppendLine($"  • {rule}");
                foreach (string note in decision.Notes) text.AppendLine($"  ! {note}");
                if (decision.Stats != null)
                    text.AppendLine($"Solver: {decision.Stats.NodesExpanded} nodes, {decision.Stats.SolutionsFound} solutions, " +
                                    $"{decision.Stats.BoundPrunes} bound prunes{(decision.IsProvenOptimal ? ", optimal" : "")}");
            }

            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true, fontSize = 12 };
            GUI.Box(new Rect(10, 10, 430, 240), text.ToString(), style);
        }

        private void OnDrawGizmos()
        {
            if (director == null || graphProvider == null) return;

            if (lastPercepts != null)
            {
                Gizmos.color = Color.yellow;
                foreach (int node in lastPercepts.VisibleNodes) Gizmos.DrawWireSphere(SpawnPosition(node) + Vector3.up * 1.2f, 0.6f);
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

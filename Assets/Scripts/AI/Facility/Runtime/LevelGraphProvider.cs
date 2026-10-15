using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// Unity-side owner of the Facility AI's level graph (the "body" around the pure C# graph).
    /// Builds the graph, maps scene markers (spawn points, exit) onto nodes, checks the scene still
    /// matches the layout data, and draws the graph as Gizmos for debugging and the demo.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelGraphProvider : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("Parent of the Door_* markers (Environment/Doors). Used to check doors match the layout data.")]
        [SerializeField] private Transform doorMarkersRoot;

        [Tooltip("The ExitPoint marker in the control room. Falls back to the layout's exit tile if empty.")]
        [SerializeField] private Transform exitPoint;

        [Tooltip("Height of the graph above the floor, used for gizmos and NavMesh checks.")]
        [SerializeField] private float nodeHeight = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool drawGraph = true;

        [Tooltip("Optional: colour nodes by Dijkstra distance from this object (green = near, red = far, black = unreachable).")]
        [SerializeField] private Transform distanceProbe;

        [Tooltip("Doors treated as locked for the debug distance view, to preview how locking reroutes paths.")]
        [SerializeField] private List<string> debugLockedDoors = new List<string>();

        [SerializeField] private bool validateOnStart = true;

        private LevelGraph graph;
        private readonly List<int> spawnNodes = new List<int>();
        private readonly List<SpawnPointMarker> spawnMarkers = new List<SpawnPointMarker>();
        private int exitNode = -1;

        public LevelGraph Graph
        {
            get
            {
                if (graph == null) Build();
                return graph;
            }
        }

        public IReadOnlyList<int> SpawnNodes
        {
            get
            {
                if (graph == null) Build();
                return spawnNodes;
            }
        }

        public int ExitNode
        {
            get
            {
                if (graph == null) Build();
                return exitNode;
            }
        }

        /// <summary>Spawn markers in the same order as <see cref="SpawnNodes"/>.</summary>
        public IReadOnlyList<SpawnPointMarker> SpawnMarkers
        {
            get
            {
                if (graph == null) Build();
                return spawnMarkers;
            }
        }

        /// <summary>Parent of the Door_* markers (Environment/Doors), or null if the scene has none.</summary>
        public Transform DoorMarkersRoot
        {
            get
            {
                if (doorMarkersRoot == null)
                {
                    var doors = GameObject.Find("Environment/Doors");
                    if (doors != null) doorMarkersRoot = doors.transform;
                }

                return doorMarkersRoot;
            }
        }

        public Vector3 NodePosition(int nodeId)
        {
            var node = Graph.Nodes[nodeId];
            return new Vector3(node.X, nodeHeight, node.Z);
        }

        public int NearestNode(Vector3 worldPosition) => Graph.FindNearestNode(worldPosition.x, worldPosition.z);

        /// <summary>
        /// Shortest open route between two world positions, as tile-centre waypoints: the tile nearest
        /// <paramref name="from"/> first, the tile nearest <paramref name="to"/> last. Empty if the goal cannot be reached.
        /// Pass <see cref="FacilityEvents.LockedDoors"/> to route around the doors the Facility AI has locked.
        /// </summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to, IEnumerable<string> lockedDoors = null)
        {
            var locked = lockedDoors as ICollection<string> ?? (lockedDoors != null ? new HashSet<string>(lockedDoors) : null);
            int[] route = Graph.ShortestPath(NearestNode(from), NearestNode(to), locked);

            var points = new List<Vector3>(route.Length);
            foreach (int node in route) points.Add(NodePosition(node));
            return points;
        }

        private void Reset()
        {
            // Called when the component is first added: wire up the usual scene objects automatically.
            var doors = GameObject.Find("Environment/Doors");
            if (doors != null) doorMarkersRoot = doors.transform;

            var exit = GameObject.Find("ExitPoint");
            if (exit != null) exitPoint = exit.transform;
        }

        private void Awake() => Build();

        private void Start()
        {
            if (validateOnStart) Validate();
        }

        private void OnValidate() => graph = null; // rebuild with the new settings on next access

        [ContextMenu("Rebuild Graph")]
        public void Build()
        {
            graph = LabLockdownLayout.CreateGraph();

            spawnMarkers.Clear();
            spawnNodes.Clear();
            spawnMarkers.AddRange(FindObjectsByType<SpawnPointMarker>(FindObjectsSortMode.InstanceID));
            foreach (var marker in spawnMarkers)
                spawnNodes.Add(graph.FindNearestNode(marker.transform.position.x, marker.transform.position.z));

            exitNode = exitPoint != null
                ? graph.FindNearestNode(exitPoint.position.x, exitPoint.position.z)
                : graph.FindNearestNode(LabLockdownLayout.ExitX, LabLockdownLayout.ExitZ);
        }

        /// <summary>
        /// Checks that the scene and the layout data agree. Logs a warning for each problem and returns
        /// true if everything matches. Run from the component's context menu or automatically on Start.
        /// </summary>
        [ContextMenu("Validate Against Scene")]
        public bool Validate()
        {
            Build();
            int problems = 0;
            float tolerance = LabLockdownLayout.TileSize * 0.25f;

            // 1. Every door in the layout has a matching Door_* marker, and vice versa.
            if (doorMarkersRoot == null)
            {
                Debug.LogWarning("[FacilityAI] Door markers root is not assigned; door positions were not checked.", this);
                problems++;
            }
            else
            {
                var expected = new Dictionary<string, Vector2>();
                foreach (var door in LabLockdownLayout.Doors) expected[door.Id] = new Vector2(door.X, door.Z);

                foreach (Transform marker in doorMarkersRoot)
                {
                    if (!marker.name.StartsWith("Door_")) continue;
                    var markerXZ = new Vector2(marker.position.x, marker.position.z);

                    if (!expected.TryGetValue(marker.name, out Vector2 layoutXZ))
                    {
                        Debug.LogWarning($"[FacilityAI] Scene door {marker.name} is not in LabLockdownLayout.Doors.", marker);
                        problems++;
                        continue;
                    }

                    if (Vector2.Distance(markerXZ, layoutXZ) > tolerance)
                    {
                        Debug.LogWarning($"[FacilityAI] {marker.name} is at {markerXZ} but the layout expects {layoutXZ}.", marker);
                        problems++;
                    }

                    expected.Remove(marker.name);
                }

                foreach (var missing in expected.Keys)
                {
                    Debug.LogWarning($"[FacilityAI] Layout door {missing} has no marker under {doorMarkersRoot.name}.", this);
                    problems++;
                }
            }

            // 2. Every spawn marker sits on a tile, and no two share one.
            if (spawnMarkers.Count == 0)
            {
                Debug.LogWarning("[FacilityAI] No SpawnPointMarker found in the scene.", this);
                problems++;
            }

            var usedNodes = new HashSet<int>();
            for (int i = 0; i < spawnMarkers.Count; i++)
            {
                var marker = spawnMarkers[i];
                var node = graph.Nodes[spawnNodes[i]];
                float offset = Vector2.Distance(
                    new Vector2(marker.transform.position.x, marker.transform.position.z),
                    new Vector2(node.X, node.Z));

                if (offset > LabLockdownLayout.TileSize * 0.75f)
                {
                    Debug.LogWarning($"[FacilityAI] Spawn {marker.SpawnId} is {offset:F1} m from the nearest tile ({node}). Is it outside the level?", marker);
                    problems++;
                }

                if (!usedNodes.Add(node.Id))
                {
                    Debug.LogWarning($"[FacilityAI] Spawn {marker.SpawnId} shares tile {node} with another spawn point.", marker);
                    problems++;
                }
            }

            // 3. Every node lies on the baked NavMesh, so agents can actually stand where the graph says.
            foreach (var node in graph.Nodes)
            {
                if (!NavMesh.SamplePosition(new Vector3(node.X, 0f, node.Z), out _, 1.5f, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[FacilityAI] Node {node} has no NavMesh within 1.5 m.", this);
                    problems++;
                }
            }

            // 4. The exit can be reached from the player start with every door open.
            int start = graph.FindNearestNode(LabLockdownLayout.PlayerStartX, LabLockdownLayout.PlayerStartZ);
            if (!graph.IsReachable(start, exitNode))
            {
                Debug.LogWarning("[FacilityAI] The exit is not reachable from the player start.", this);
                problems++;
            }

            if (problems == 0)
                Debug.Log($"[FacilityAI] Level graph OK: {graph.NodeCount} nodes, {graph.Edges.Count} edges, " +
                          $"{LabLockdownLayout.Doors.Count} doors, {spawnMarkers.Count} spawn points.", this);

            return problems == 0;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor helper: creates a SpawnPointMarker for each default spawn in LabLockdownLayout,
        /// as children of this object. SP_MaintenanceBay reuses the existing EnemySpawnPoint.
        /// Skips spawns that already exist, so it is safe to run more than once. Supports Undo.
        /// </summary>
        [ContextMenu("Create Default Spawn Points")]
        private void CreateDefaultSpawnPoints()
        {
            var existingIds = new HashSet<string>();
            foreach (var marker in FindObjectsByType<SpawnPointMarker>(FindObjectsSortMode.None))
                existingIds.Add(marker.SpawnId);

            Transform parent = transform.Find("SpawnPoints");
            if (parent == null)
            {
                var parentObject = new GameObject("SpawnPoints");
                UnityEditor.Undo.RegisterCreatedObjectUndo(parentObject, "Create Spawn Points");
                parentObject.transform.SetParent(transform, false);
                parent = parentObject.transform;
            }

            foreach (var spawn in LabLockdownLayout.DefaultSpawnPoints)
            {
                if (existingIds.Contains(spawn.Id)) continue;

                GameObject target = spawn.Id == "SP_MaintenanceBay" ? GameObject.Find("EnemySpawnPoint") : null;
                if (target != null && target.GetComponent<SpawnPointMarker>() != null) continue;

                if (target == null)
                {
                    target = new GameObject(spawn.Id);
                    UnityEditor.Undo.RegisterCreatedObjectUndo(target, "Create Spawn Points");
                    target.transform.SetParent(parent, false);
                    target.transform.position = new Vector3(spawn.X, 0f, spawn.Z);
                }

                var added = UnityEditor.Undo.AddComponent<SpawnPointMarker>(target);
                var serialized = new UnityEditor.SerializedObject(added);
                serialized.FindProperty("spawnId").stringValue = spawn.Id;
                serialized.ApplyModifiedProperties();
            }

            Build();
            Debug.Log($"[FacilityAI] Spawn points ready: {spawnMarkers.Count} in the scene.", this);
        }
#endif

        private void OnDrawGizmos()
        {
            if (!drawGraph) return;
            var g = Graph;

            float[] distances = null;
            float maxDistance = 1f;
            if (distanceProbe != null)
            {
                int from = g.FindNearestNode(distanceProbe.position.x, distanceProbe.position.z);
                distances = g.ShortestDistances(from, debugLockedDoors);
                foreach (float d in distances)
                    if (!float.IsPositiveInfinity(d) && d > maxDistance) maxDistance = d;
            }

            foreach (var edge in g.Edges)
            {
                bool locked = edge.IsDoor && debugLockedDoors.Contains(edge.DoorId);
                Gizmos.color = edge.IsDoor ? (locked ? Color.red : new Color(1f, 0.6f, 0f)) : new Color(0.6f, 0.6f, 0.6f);
                Gizmos.DrawLine(NodePosition(edge.A), NodePosition(edge.B));
            }

            for (int i = 0; i < g.NodeCount; i++)
            {
                if (distances != null)
                {
                    Gizmos.color = float.IsPositiveInfinity(distances[i])
                        ? Color.black
                        : Color.Lerp(Color.green, Color.red, distances[i] / maxDistance);
                }
                else
                {
                    Gizmos.color = Color.cyan;
                }

                float radius = i == exitNode ? 0.5f : 0.25f;
                Gizmos.DrawSphere(NodePosition(i), radius);
            }

            if (exitNode >= 0)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(NodePosition(exitNode) + Vector3.up, new Vector3(1.2f, 2f, 1.2f));
            }

#if UNITY_EDITOR
            if (distances != null)
            {
                for (int i = 0; i < g.NodeCount; i++)
                {
                    string text = float.IsPositiveInfinity(distances[i]) ? "∞" : distances[i].ToString("0");
                    UnityEditor.Handles.Label(NodePosition(i) + Vector3.up * 0.6f, text);
                }
            }
#endif
        }
    }
}

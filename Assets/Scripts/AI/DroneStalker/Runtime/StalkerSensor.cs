using System.Collections.Generic;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;

namespace LabLockdown.AI.DroneStalker.Runtime
{
    /// <summary>
    /// The drone's senses: fills a <see cref="StalkerPercepts"/> from the scene each time the brain thinks.
    /// It only reads the scene (positions, raycasts, overlaps); it never moves anything.
    ///
    /// Drone sight (who it can see): within SightRangeMetres and FieldOfViewDegrees of the eye's forward direction,
    /// with a clear line of sight. Walls and door leaves block it; darkness does not (night vision).
    ///
    /// Player sight (who can see the drone): inside the player camera's view, within the notice range, with a clear
    /// line of sight, and either the drone's tile is lit or the drone is close. In the dark the player only notices
    /// it up close. The same rule rates every tile, giving the set of tiles the player can see.
    ///
    /// Doors are sensed physically: a door counts as closed while one of its own leaves blocks the doorway centre.
    /// A locked door is shut, so it is covered too.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StalkerSensor : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("The shared level graph. Found automatically if left empty.")]
        [SerializeField] private LevelGraphProvider graphProvider;
        [Tooltip("The drone's eye. Its forward direction is where the drone looks. Defaults to this transform.")]
        [SerializeField] private Transform eye = null;
        [Tooltip("The player. Found by the Player tag if left empty.")]
        [SerializeField] private Transform player;
        [Tooltip("Places the player goes to heal (infirmary, medkits). Used to predict the player's route when hurt.")]
        [SerializeField] private Transform[] healPoints = new Transform[0];

        [Header("Sight")]
        [Tooltip("Layers that block sight and light: walls (Default) and door leaves (Doors).")]
        [SerializeField] private LayerMask sightBlockers;
        [Tooltip("Height of the player's eyes above their position (the Facility AI uses 1.6 m).")]
        [SerializeField] private float playerEyeHeight = 1.6f;
        [Tooltip("Height above the floor at which the drone hovers; used for the light map and for tile visibility.")]
        [SerializeField] private float hoverHeight = 1.4f;
        [Tooltip("How far away the player can notice the drone at all.")]
        [SerializeField] private float playerNoticeRange = 30f;
        [Tooltip("In the dark the player only notices the drone within this distance.")]
        [SerializeField] private float darkNoticeDistance = 5f;
        [Tooltip("How often the set of tiles the player can see is recomputed (seconds).")]
        [SerializeField] private float visibleTilesInterval = 0.25f;

        [Header("Doors")]
        [Tooltip("Radius of the check at each doorway centre.")]
        [SerializeField] private float doorProbeRadius = 0.3f;
        [Tooltip("Height of the doorway check above the door marker.")]
        [SerializeField] private float doorProbeHeight = 1.0f;

        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] overlaps = new Collider[16];
        readonly List<(string id, Transform marker)> doors = new List<(string, Transform)>();
        readonly HashSet<int> visibleTiles = new HashSet<int>();
        readonly HashSet<string> closedDoors = new HashSet<string>();
        readonly HashSet<int> healNodes = new HashSet<int>();
        IPlayerStatusSource playerHealth;
        Camera playerCamera;
        double nextVisibleTilesTime = double.NegativeInfinity;

        /// <summary>The light level of every tile, built when the sensor initialises.</summary>
        public StalkerLightMap LightMap { get; private set; }

        public LevelGraphProvider GraphProvider => graphProvider;
        public Transform Eye => eye != null ? eye : transform;
        public Transform Player => player;
        public bool IsReady => graphProvider != null && graphProvider.Graph != null;

        void Reset()
        {
            sightBlockers = LayerMask.GetMask("Default", "Doors");
        }

        void Awake()
        {
            if (sightBlockers.value == 0) sightBlockers = LayerMask.GetMask("Default", "Doors");
        }

        /// <summary>
        /// Sets the heal points for a drone spawned at runtime (a prefab cannot reference scene objects).
        /// Call before <see cref="Initialise"/>, i.e. straight after Instantiate.
        /// </summary>
        public void SetHealPoints(Transform[] points)
        {
            healPoints = points ?? new Transform[0];
        }

        /// <summary>Finds the scene references, builds the light map and caches the doors. Safe to call again.</summary>
        public void Initialise()
        {
            if (graphProvider == null) graphProvider = FindAnyObjectByType<LevelGraphProvider>();
            if (graphProvider == null)
            {
                Debug.LogWarning("[DroneStalker] No LevelGraphProvider in the scene; the drone cannot sense its surroundings.", this);
                return;
            }

            FindPlayer();

            doors.Clear();
            if (graphProvider.DoorMarkersRoot != null)
                foreach (Transform marker in graphProvider.DoorMarkersRoot)
                    if (marker.name.StartsWith("Door_")) doors.Add((marker.name, marker));

            healNodes.Clear();
            foreach (Transform h in healPoints)
                if (h != null) healNodes.Add(graphProvider.NearestNode(h.position));

            LightMap = StalkerLightMap.Build(graphProvider.Graph, graphProvider.NodePosition, hoverHeight, null, sightBlockers);
        }

        void FindPlayer()
        {
            if (player == null)
            {
                GameObject tagged = GameObject.FindWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }
            playerHealth = null;
            playerCamera = null;
            if (player == null) return;

            foreach (MonoBehaviour mb in player.GetComponentsInChildren<MonoBehaviour>())
                if (mb is IPlayerStatusSource source) { playerHealth = source; break; }
            playerCamera = player.GetComponentInChildren<Camera>();
        }

        /// <summary>Fills <paramref name="p"/> with what the drone senses right now.</summary>
        public void Sense(StalkerPercepts p, StalkerSettings settings, double time)
        {
            p.Time = time;
            if (!IsReady)
            {
                p.SelfNode = -1;
                p.PlayerInSight = false;
                return;
            }
            if (player == null) FindPlayer();

            Vector3 eyePos = Eye.position;
            p.SelfNode = graphProvider.NearestNode(eyePos);
            p.LightByNode = LightMap?.Levels;
            p.ExitNode = graphProvider.ExitNode;
            p.HealNodes = healNodes;

            SenseDoors();
            p.ClosedDoors = closedDoors;
            p.LockedDoors = closedDoors;   // a locked door is shut, so sensing it physically covers both

            if (player == null)
            {
                p.PlayerInSight = false;
                p.PlayerSeesMe = false;
                p.PlayerNode = -1;
                p.DistanceToPlayerMetres = float.PositiveInfinity;
                return;
            }

            Vector3 playerEye = player.position + Vector3.up * playerEyeHeight;
            p.PlayerNode = graphProvider.NearestNode(player.position);
            p.DistanceToPlayerMetres = Vector3.Distance(eyePos, playerEye);
            p.PlayerHealth01 = playerHealth != null ? Mathf.Clamp01(playerHealth.Health01) : 1f;
            p.PlayerInSight = DroneSees(eyePos, playerEye, settings);

            // What the player sees only matters while the drone knows where the player is.
            if (p.PlayerInSight)
            {
                float lightHere = LightMap != null ? p.LightAt(p.SelfNode) : StalkerPercepts.NeutralLight;
                p.PlayerSeesMe = PlayerNotices(playerEye, eyePos, lightHere, settings);
                if (time >= nextVisibleTilesTime)
                {
                    nextVisibleTilesTime = time + visibleTilesInterval;
                    UpdateVisibleTiles(playerEye, p, settings);
                }
                p.PlayerVisibleNodes = visibleTiles;
            }
            else
            {
                p.PlayerSeesMe = false;
                visibleTiles.Clear();
                p.PlayerVisibleNodes = visibleTiles;
            }
        }

        // ---------------- drone sight ----------------

        bool DroneSees(Vector3 eyePos, Vector3 target, StalkerSettings s)
        {
            Vector3 toTarget = target - eyePos;
            if (toTarget.magnitude > s.SightRangeMetres) return false;

            Vector3 flatForward = Vector3.ProjectOnPlane(Eye.forward, Vector3.up);
            Vector3 flatTo = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            if (flatForward.sqrMagnitude > 1e-6f && flatTo.sqrMagnitude > 1e-6f &&
                Vector3.Angle(flatForward, flatTo) > s.FieldOfViewDegrees * 0.5f) return false;

            return !Blocked(eyePos, target);
        }

        // ---------------- player sight ----------------

        bool PlayerNotices(Vector3 playerEye, Vector3 point, float lightAtPoint, StalkerSettings s)
        {
            float distance = Vector3.Distance(playerEye, point);
            if (distance > playerNoticeRange) return false;
            if (!InPlayerView(point)) return false;
            bool visibleInLight = lightAtPoint > s.DarkLightLevel || distance <= darkNoticeDistance;
            if (!visibleInLight) return false;
            return !Blocked(playerEye, point);
        }

        bool InPlayerView(Vector3 point)
        {
            if (playerCamera != null)
            {
                Vector3 v = playerCamera.WorldToViewportPoint(point);
                return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
            }
            // No camera found: assume a 90 degree view along the player's facing.
            Vector3 to = Vector3.ProjectOnPlane(point - player.position, Vector3.up);
            return Vector3.Angle(Vector3.ProjectOnPlane(player.forward, Vector3.up), to) <= 45f;
        }

        void UpdateVisibleTiles(Vector3 playerEye, StalkerPercepts p, StalkerSettings s)
        {
            visibleTiles.Clear();
            int n = graphProvider.Graph.NodeCount;
            for (int i = 0; i < n; i++)
            {
                Vector3 tile = graphProvider.NodePosition(i) + Vector3.up * hoverHeight;
                if (PlayerNotices(playerEye, tile, p.LightAt(i), s)) visibleTiles.Add(i);
            }
        }

        // ---------------- doors ----------------

        void SenseDoors()
        {
            closedDoors.Clear();
            foreach (var (id, marker) in doors)
            {
                if (marker == null) continue;
                Vector3 centre = marker.position + Vector3.up * doorProbeHeight;
                int count = Physics.OverlapSphereNonAlloc(centre, doorProbeRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < count; k++)
                {
                    // Only this door's own leaves count, so a player or prop standing in the doorway is not a "closed door".
                    if (overlaps[k] != null && overlaps[k].transform.IsChildOf(marker)) { closedDoors.Add(id); break; }
                }
            }
        }

        // ---------------- helpers ----------------

        /// <summary>True if a wall or door leaf lies between the two points (ignoring the drone and the player themselves).</summary>
        bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 1e-4f) return false;
            int count = Physics.RaycastNonAlloc(from, dir / length, hits, length, sightBlockers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
            {
                Transform t = hits[k].transform;
                if (t == null) continue;
                if (t.IsChildOf(transform)) continue;                 // the drone's own colliders
                if (player != null && t.IsChildOf(player)) continue;  // the player's own colliders
                return true;
            }
            return false;
        }
    }
}

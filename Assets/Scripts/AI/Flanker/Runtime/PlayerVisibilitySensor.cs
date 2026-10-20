using System.Collections.Generic;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace LabLockdown.AI.Flanker.Runtime
{
    /// <summary>
    /// Which tiles of the level graph the player can see: one ray from the player's eyes to each tile centre
    /// (the same eye height and blockers as the Facility AI's line-of-sight checks). Shared by every flanker and
    /// refreshed at most every <see cref="interval"/> seconds, so ten flankers cost the same 45 rays as one.
    /// Robots and the player never block the view: a flanker standing on a tile must not hide that tile from itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVisibilitySensor : MonoBehaviour
    {
        static PlayerVisibilitySensor instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [SerializeField] private float interval = 0.2f;
        [Tooltip("Player eye height above the pivot (as in the Facility AI).")]
        [SerializeField] private float eyeHeight = 1.6f;
        [Tooltip("Height above each tile centre that is checked (a robot's chest).")]
        [SerializeField] private float targetHeight = 1.2f;
        [Tooltip("What blocks the view. Default + Doors when empty.")]
        [SerializeField] private LayerMask blockers;

        private readonly HashSet<int> visible = new HashSet<int>();
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private LevelGraphProvider graph;
        private Transform player;
        private float nextRefresh = float.NegativeInfinity;

        /// <summary>The scene's sensor, created on first use.</summary>
        public static PlayerVisibilitySensor Get(LevelGraphProvider graph)
        {
            if (instance == null) instance = FindAnyObjectByType<PlayerVisibilitySensor>();
            if (instance == null) instance = new GameObject("Flanker Visibility Sensor").AddComponent<PlayerVisibilitySensor>();
            if (instance.graph == null) instance.graph = graph;
            return instance;
        }

        /// <summary>Tile ids the player has line of sight to, refreshed if older than the interval.</summary>
        public ICollection<int> VisibleNodes
        {
            get
            {
                Refresh();
                return visible;
            }
        }

        private void Awake()
        {
            if (blockers.value == 0) blockers = LayerMask.GetMask("Default", "Doors");
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Refresh()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + interval;
            visible.Clear();

            if (player == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            if (player == null || graph == null) return;

            Vector3 eye = player.position + Vector3.up * eyeHeight;
            for (int node = 0; node < graph.Graph.NodeCount; node++)
                if (IsClear(eye, graph.NodePosition(node) + Vector3.up * targetHeight)) visible.Add(node);
        }

        private bool IsClear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-3f) return true;

            int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, blockers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false; // too many hits to check them all: assume blocked
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i].collider;
                if (hit.transform.IsChildOf(player)) continue;
                if (hit.GetComponentInParent<NavMeshAgent>() != null) continue; // robots do not block the view
                return false;
            }

            return true;
        }
    }
}

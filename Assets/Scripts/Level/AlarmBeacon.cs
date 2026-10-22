using GVGame.Player;
using LabLockdown.AI.Facility.Director;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// A rotating red alarm beacon that switches on while the Facility AI is at its Peak, so the player can see the
    /// facility escalate.
    ///
    /// Put it on a ceiling light fixture (e.g. a Light_Small_Red door accent) or on an empty object at the ceiling.
    /// It creates its own Realtime spot light at runtime, so it never affects the lighting bake, and the light casts
    /// no shadows, so it stays cheap. The light is only enabled while the alarm is on.
    /// Driven by <see cref="FacilityEvents.PhaseChanged"/>; turns off when the run ends (the player dies or escapes).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlarmBeacon : MonoBehaviour
    {
        [Header("When")]
        [Tooltip("Also turn on during Build-up (normally Peak only).")]
        [SerializeField] private bool onDuringBuildUp = false;

        [Header("Light")]
        [SerializeField] private Color color = new Color(1f, 0.231f, 0.188f); // #FF3B30, the level's emergency red
        [SerializeField] private float intensity = 6f;
        [SerializeField] private float range = 9f;
        [Range(10f, 120f)] [SerializeField] private float spotAngle = 60f;
        [Tooltip("Degrees below horizontal the beam points.")]
        [Range(0f, 80f)] [SerializeField] private float tilt = 35f;
        [Tooltip("World-space offset of the beacon light from this object, e.g. just below a ceiling fixture.")]
        [SerializeField] private Vector3 offset = new Vector3(0f, -0.4f, 0f);
        [Tooltip("Two beams back to back, like a real rotating beacon (one extra light).")]
        [SerializeField] private bool twoBeams = false;

        [Header("Motion")]
        [Tooltip("Rotation speed in degrees per second.")]
        [SerializeField] private float degreesPerSecond = 200f;
        [Tooltip("Seconds to fade the light in and out.")]
        [SerializeField] private float fadeSeconds = 0.4f;

        private Transform pivot;
        private float angle;
        private Light[] beams;
        private float level;
        private bool phaseOn, ended, forced;
        private PlayerHealth player;
        private ExitTrigger exit;

        private bool On => forced || (phaseOn && !ended);

        private void Awake()
        {
            pivot = new GameObject("AlarmBeacon_Pivot").transform;
            pivot.SetParent(transform, false);
            // World space, so the beacon spins about the vertical even on a rotated or scaled fixture.
            pivot.position = transform.position + offset;
            angle = Random.Range(0f, 360f); // beacons out of sync look natural
            pivot.rotation = Quaternion.Euler(0f, angle, 0f);

            beams = new Light[twoBeams ? 2 : 1];
            for (int i = 0; i < beams.Length; i++)
            {
                var go = new GameObject("Beam" + i);
                go.transform.SetParent(pivot, false);
                go.transform.localRotation = Quaternion.Euler(tilt, i * 180f, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = color;
                light.range = range;
                light.spotAngle = spotAngle;
                light.innerSpotAngle = spotAngle * 0.5f;
                light.shadows = LightShadows.None;
                light.intensity = 0f;
                light.enabled = false;
                beams[i] = light;
            }

            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) player = tagged.GetComponentInParent<PlayerHealth>();
            exit = FindAnyObjectByType<ExitTrigger>();
        }

        private void OnEnable()
        {
            FacilityEvents.PhaseChanged += OnPhaseChanged;
            if (player != null)
            {
                player.Died += OnRunEnded;
                player.Respawned += OnRunRestarted;
            }
            if (exit != null) exit.Escaped += OnRunEnded;
            SetPhase(FacilityEvents.Phase);
        }

        private void OnDisable()
        {
            FacilityEvents.PhaseChanged -= OnPhaseChanged;
            if (player != null)
            {
                player.Died -= OnRunEnded;
                player.Respawned -= OnRunRestarted;
            }
            if (exit != null) exit.Escaped -= OnRunEnded;
        }

        private void OnPhaseChanged(DirectorPhase previous, DirectorPhase current) => SetPhase(current);

        private void SetPhase(DirectorPhase phase) =>
            phaseOn = phase == DirectorPhase.Peak || (onDuringBuildUp && phase == DirectorPhase.BuildUp);

        private void OnRunEnded() => ended = true;
        // Respawn or play again: the Director restarts in Relief, so the alarm starts off.
        private void OnRunRestarted()
        {
            ended = false;
            phaseOn = false;
        }

        private void Update()
        {
            float target = On ? 1f : 0f;
            if (level == 0f && target == 0f) return; // off: no work at all

            // Unscaled time, so the beacon still fades out on the paused escape screen.
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, target, fadeSeconds > 0f ? dt / fadeSeconds : 1f);
            angle = (angle + degreesPerSecond * dt) % 360f;
            pivot.rotation = Quaternion.Euler(0f, angle, 0f);

            foreach (var beam in beams)
            {
                beam.intensity = intensity * level;
                beam.enabled = level > 0f;
            }
        }

        /// <summary>Right-click the component header and pick this to see the beacon without waiting for a Peak.</summary>
        [ContextMenu("Test: toggle beacon")]
        private void ToggleTest() => forced = !forced;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.23f, 0.19f, 0.8f);
            Vector3 origin = transform.position + offset;
            Gizmos.DrawWireSphere(origin, 0.15f);
            Gizmos.DrawWireSphere(origin, range);
        }
    }
}

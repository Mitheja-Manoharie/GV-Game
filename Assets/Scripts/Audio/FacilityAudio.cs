using System.Collections.Generic;
using GVGame.Combat;
using GVGame.Level;
using GVGame.Player;
using LabLockdown.AI.Facility.Director;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;
using UnityEngine.Audio;

namespace GVGame.Audio
{
    /// <summary>
    /// The facility's sound: an ambient hum, music that follows the Facility AI's pacing, and an alarm.
    ///
    /// Listens to <see cref="FacilityEvents"/> and the player, so it needs no references to the Director:
    ///   - Relief: calm music.   Build-up: calm and tense mixed.   Peak: tense music, plus the alarm.
    ///   - Combat: tense music while the player is shooting or being hit, and for a few seconds after.
    ///   - A door locked by the Facility AI: a lock sound at that door (3D), and the alarm (rate-limited).
    /// When the run ends (the player dies or escapes) everything fades out and no alarms play; it all starts again
    /// from calm music when the player respawns or plays again.
    /// Every clip is optional; missing clips are simply silent. Put it on any object in Main (e.g. FacilityAI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FacilityAudio : MonoBehaviour
    {
        [Header("Clips (all optional)")]
        [Tooltip("Looping facility hum, always playing quietly.")]
        [SerializeField] private AudioClip ambientLoop;
        [Tooltip("Looping music for relief and build-up.")]
        [SerializeField] private AudioClip calmMusic;
        [Tooltip("Looping music for peaks. Plays in sync with the calm music and is cross-faded in.")]
        [SerializeField] private AudioClip tenseMusic;
        [Tooltip("One-shot alarm when a peak starts or a door is locked.")]
        [SerializeField] private AudioClip alarm;
        [Tooltip("One-shot sound played at a door when the Facility AI locks it.")]
        [SerializeField] private AudioClip doorLock;

        [Header("Levels")]
        [Range(0f, 1f)] [SerializeField] private float ambientVolume = 0.25f;
        [Tooltip("Volume of the calm track.")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.35f;
        [Tooltip("Volume of the tense track (usually louder than the calm one).")]
        [Range(0f, 1f)] [SerializeField] private float tenseMusicVolume = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float alarmVolume = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float doorLockVolume = 0.9f;
        [Tooltip("Below 1 makes the lock sound deeper and heavier (and a little slower).")]
        [Range(0.3f, 1.5f)] [SerializeField] private float doorLockPitch = 0.6f;
        [Tooltip("Seconds to cross-fade between calm and tense music.")]
        [SerializeField] private float crossFadeSeconds = 2f;
        [Tooltip("How much of the tense track plays during build-up (0 = calm, 1 = fully tense).")]
        [Range(0f, 1f)] [SerializeField] private float buildUpTension = 0.6f;
        [Tooltip("Seconds the music stays tense after the player last fired or was hit.")]
        [SerializeField] private float combatHoldSeconds = 8f;
        [Tooltip("Minimum seconds between two alarms.")]
        [SerializeField] private float alarmCooldown = 8f;
        [Tooltip("Seconds to fade everything out when the player dies or escapes.")]
        [SerializeField] private float endFadeSeconds = 1.5f;

        [Header("Optional")]
        [Tooltip("Mixer group for all of these sounds (e.g. Music/Ambience). Empty = default output.")]
        [SerializeField] private AudioMixerGroup output;
        [Tooltip("Parent of the Door_* markers. Found as Environment/Doors if empty.")]
        [SerializeField] private Transform doorMarkersRoot;

        private AudioSource ambientSource, calmSource, tenseSource, alarmSource;
        private readonly Dictionary<string, Transform> doors = new Dictionary<string, Transform>();
        private float tenseTarget, tenseLevel;
        private float lastAlarm = -999f;
        private PlayerHealth player;
        private WeaponController weapon;
        private float phaseTension, lastCombatTime = -999f;
        private ExitTrigger exit;
        private bool ended;
        private float masterLevel = 1f;

        private void Awake()
        {
            ambientSource = CreateSource("Ambient", ambientLoop, loop: true);
            calmSource = CreateSource("CalmMusic", calmMusic, loop: true);
            tenseSource = CreateSource("TenseMusic", tenseMusic, loop: true);
            alarmSource = CreateSource("Alarm", null, loop: false);

            if (doorMarkersRoot == null)
            {
                var found = GameObject.Find("Environment/Doors");
                if (found != null) doorMarkersRoot = found.transform;
            }

            if (doorMarkersRoot != null)
                foreach (Transform marker in doorMarkersRoot)
                    if (marker.name.StartsWith("Door_")) doors[marker.name] = marker;

            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                player = tagged.GetComponentInParent<PlayerHealth>();
                weapon = tagged.GetComponentInChildren<WeaponController>();
            }
            exit = FindAnyObjectByType<ExitTrigger>();
        }

        private void Start()
        {
            if (ambientSource.clip != null) ambientSource.Play();

            // Start both music tracks together so the cross-fade stays in time.
            if (calmSource.clip != null) calmSource.Play();
            if (tenseSource.clip != null) tenseSource.Play();

            SetPhase(FacilityEvents.Phase, instant: true);
        }

        private void OnEnable()
        {
            FacilityEvents.PhaseChanged += OnPhaseChanged;
            FacilityEvents.DoorLockChanged += OnDoorLockChanged;
            if (player != null)
            {
                player.Died += OnRunEnded;
                player.Respawned += OnRunRestarted;
                player.Damaged += OnPlayerDamaged;
            }

            if (weapon != null) weapon.Fired += OnCombat;

            if (exit != null) exit.Escaped += OnRunEnded;
        }

        private void OnDisable()
        {
            FacilityEvents.PhaseChanged -= OnPhaseChanged;
            FacilityEvents.DoorLockChanged -= OnDoorLockChanged;
            if (player != null)
            {
                player.Died -= OnRunEnded;
                player.Respawned -= OnRunRestarted;
                player.Damaged -= OnPlayerDamaged;
            }

            if (weapon != null) weapon.Fired -= OnCombat;

            if (exit != null) exit.Escaped -= OnRunEnded;
        }

        private void OnPlayerDamaged(DamageInfo _) => OnCombat();

        private void OnCombat() => lastCombatTime = Time.unscaledTime;

        /// <summary>The player died or escaped: fade everything out and stay silent.</summary>
        private void OnRunEnded()
        {
            ended = true;
            alarmSource.Stop();
        }

        /// <summary>Respawn or play again: start over with calm music.</summary>
        private void OnRunRestarted()
        {
            ended = false;
            masterLevel = 1f;
            lastAlarm = -999f;
            lastCombatTime = -999f;
            SetPhase(DirectorPhase.Relief, instant: true);
            foreach (var source in new[] { ambientSource, calmSource, tenseSource })
            {
                if (source.clip == null) continue;
                source.Stop();
                source.Play();
            }
        }

        private void OnPhaseChanged(DirectorPhase previous, DirectorPhase current)
        {
            if (ended) return;
            SetPhase(current, instant: false);
            if (current == DirectorPhase.Peak) PlayAlarm();
        }

        private void OnDoorLockChanged(string doorId, bool locked)
        {
            if (!locked || ended) return;

            if (doorLock != null)
            {
                bool atDoor = doors.TryGetValue(doorId, out var marker) && marker != null;
                PlayOnce(doorLock, atDoor ? marker.position + Vector3.up * 1.5f : transform.position, doorLockVolume, doorLockPitch, spatial: atDoor);
            }

            PlayAlarm();
        }

        private void SetPhase(DirectorPhase phase, bool instant)
        {
            phaseTension = phase == DirectorPhase.Peak ? 1f : phase == DirectorPhase.BuildUp ? buildUpTension : 0f;
            tenseTarget = phaseTension;
            if (instant) tenseLevel = tenseTarget;
            ApplyMusicLevels();
        }

        private void PlayAlarm()
        {
            if (alarm == null || Time.unscaledTime - lastAlarm < alarmCooldown) return;
            lastAlarm = Time.unscaledTime;
            alarmSource.PlayOneShot(alarm, alarmVolume);
        }

        private void Update()
        {
            // Unscaled time: the music keeps fading normally on the paused escape screen.
            bool inCombat = Time.unscaledTime - lastCombatTime < combatHoldSeconds;
            tenseTarget = inCombat ? 1f : phaseTension;

            float step = crossFadeSeconds > 0f ? Time.unscaledDeltaTime / crossFadeSeconds : 1f;
            tenseLevel = Mathf.MoveTowards(tenseLevel, tenseTarget, step);

            float fadeStep = endFadeSeconds > 0f ? Time.unscaledDeltaTime / endFadeSeconds : 1f;
            masterLevel = Mathf.MoveTowards(masterLevel, ended ? 0f : 1f, fadeStep);
            ApplyMusicLevels();
        }

        private void ApplyMusicLevels()
        {
            // Equal-power cross-fade, so the total loudness stays even.
            calmSource.volume = masterLevel * musicVolume * Mathf.Cos(tenseLevel * Mathf.PI * 0.5f);
            tenseSource.volume = masterLevel * tenseMusicVolume * Mathf.Sin(tenseLevel * Mathf.PI * 0.5f);
            ambientSource.volume = masterLevel * ambientVolume;
        }

        /// <summary>One-shot sound with its own pitch (AudioSource.PlayClipAtPoint cannot change pitch).</summary>
        private void PlayOnce(AudioClip clip, Vector3 position, float volume, float pitch, bool spatial)
        {
            var go = new GameObject(clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = 3f;
            source.outputAudioMixerGroup = output;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.05f, pitch) + 0.1f);
        }

        private AudioSource CreateSource(string name, AudioClip clip, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D: heard the same everywhere
            source.outputAudioMixerGroup = output;
            source.ignoreListenerPause = true;
            return source;
        }
    }
}

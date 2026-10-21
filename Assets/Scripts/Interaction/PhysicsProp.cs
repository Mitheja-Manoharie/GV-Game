using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Interaction
{
    /// <summary>
    /// Makes a prop physically interactive: configures its Rigidbody, plays an impact sound when it hits
    /// anything, raises impact noise for the AI, and (for barricades) keeps the NavMesh in sync via a
    /// carving NavMeshObstacle so AI agents path around a barricade the player has dragged.
    ///
    /// Impact sounds need no audio files: if no Impact Clip is assigned, a short sound is synthesized once
    /// per style (ceramic clink, metal box, heavy thud) and shared by every prop. Drop a real clip into
    /// Impact Clip at any time to override it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsProp : MonoBehaviour
    {
        public enum Kind { Throwable, Barricade }
        public enum SoundStyle { Auto, Ceramic, MetalBox, Heavy }

        [Header("Type")]
        public Kind kind = Kind.Throwable;

        [Header("Rigidbody")]
        public float mass = 1f;
        public float linearDamping = 0.1f;
        public float angularDamping = 0.2f;

        [Header("Impact sound")]
        [Tooltip("Optional real clip. Leave empty to use the built-in synthesized sound.")]
        public AudioClip impactClip;
        [Tooltip("Use the synthesized sound when no clip is assigned.")]
        public bool synthesizeSound = true;
        [Tooltip("Auto picks by mass: under 0.5 kg ceramic, under 8 kg metal box, otherwise heavy.")]
        public SoundStyle soundStyle = SoundStyle.Auto;
        [Range(0f, 1f)] public float impactVolume = 0.7f;
        [Tooltip("Minimum impact speed (m/s) that makes a sound.")]
        public float soundSpeedThreshold = 1.5f;
        [Tooltip("Random pitch variation (+/-) so repeated hits do not sound identical.")]
        [Range(0f, 0.4f)] public float pitchVariation = 0.12f;
        public float soundCooldown = 0.08f;
        public float maxHearingDistance = 30f;

        [Header("Impact noise (AI)")]
        [Tooltip("Minimum impact speed (m/s) that raises noise for the AI.")]
        public float noiseSpeedThreshold = 2.5f;
        public float noiseLoudness = 8f;
        public float noiseCooldown = 0.25f;

        [Header("Barricade NavMesh sync")]
        public bool carveNavMesh = true;

        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; set; }

        float _lastNoiseTime = -10f;
        float _lastSoundTime = -10f;
        AudioSource _audio;
        AudioClip _clip;
        NavMeshObstacle _obstacle;

        // Synthesized clips are built once per style and shared by every prop.
        static readonly Dictionary<SoundStyle, AudioClip> s_clips = new Dictionary<SoundStyle, AudioClip>();

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            ApplyRigidbodySettings();

            _clip = impactClip != null ? impactClip : (synthesizeSound ? GetSynthesizedClip(ResolveStyle()) : null);
            if (_clip != null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.minDistance = 1.5f;
                _audio.maxDistance = maxHearingDistance;
            }

            if (kind == Kind.Barricade && carveNavMesh)
                SetupObstacle();
        }

        void Reset() => ApplyDefaultsForKind();

        void OnValidate()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
        }

        public void ApplyDefaultsForKind()
        {
            if (kind == Kind.Barricade)
            {
                mass = 40f; linearDamping = 1.5f; angularDamping = 2f; noiseLoudness = 12f;
            }
            else
            {
                mass = 1f; linearDamping = 0.05f; angularDamping = 0.1f; noiseLoudness = 8f;
            }
        }

        public void ApplyRigidbodySettings()
        {
            Body.mass = mass;
            Body.linearDamping = linearDamping;
            Body.angularDamping = angularDamping;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            // Small fast throwables need continuous detection so they don't tunnel through walls.
            Body.collisionDetectionMode = kind == Kind.Throwable
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.Discrete;
            Body.sleepThreshold = 0.05f;
        }

        void SetupObstacle()
        {
            _obstacle = GetComponent<NavMeshObstacle>();
            if (_obstacle == null) _obstacle = gameObject.AddComponent<NavMeshObstacle>();
            var box = GetComponent<BoxCollider>();
            _obstacle.shape = NavMeshObstacleShape.Box;
            if (box != null) { _obstacle.center = box.center; _obstacle.size = box.size; }
            _obstacle.carving = true;
            // Only carve while the barricade is at rest, so dragging it is cheap.
            _obstacle.carveOnlyStationary = true;
            _obstacle.carvingMoveThreshold = 0.1f;
            _obstacle.carvingTimeToStationary = 0.5f;
        }

        void OnCollisionEnter(Collision c)
        {
            float speed = c.relativeVelocity.magnitude;

            // Sound: any surface (floor, wall, another prop, a door), above a low speed threshold.
            if (_audio != null && speed >= soundSpeedThreshold && Time.time - _lastSoundTime >= soundCooldown)
            {
                _lastSoundTime = Time.time;
                _audio.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
                _audio.PlayOneShot(_clip, impactVolume * Mathf.Clamp(speed / 10f, 0.25f, 1f));
            }

            // Noise for the AI: only harder impacts.
            if (speed < noiseSpeedThreshold) return;
            if (Time.time - _lastNoiseTime < noiseCooldown) return;
            _lastNoiseTime = Time.time;

            float scale = Mathf.Clamp(speed / 8f, 0.4f, 1.5f);
            InteractionNoise.Emit(c.GetContact(0).point, noiseLoudness * scale, gameObject);
        }

        // ---------- Synthesized impact sounds ----------
        SoundStyle ResolveStyle()
        {
            if (soundStyle != SoundStyle.Auto) return soundStyle;
            if (kind == Kind.Barricade || mass >= 8f) return SoundStyle.Heavy;
            return mass < 0.5f ? SoundStyle.Ceramic : SoundStyle.MetalBox;
        }

        static AudioClip GetSynthesizedClip(SoundStyle style)
        {
            if (s_clips.TryGetValue(style, out AudioClip existing) && existing != null) return existing;
            AudioClip clip = Synthesize(style);
            s_clips[style] = clip;
            return clip;
        }

        // A short decaying mix of sine partials plus a noise burst for the "hit". Cheap, no assets needed.
        static AudioClip Synthesize(SoundStyle style)
        {
            const int rate = 22050;
            float[] freqs, amps;
            float decay, duration, noise, noiseDecay;

            switch (style)
            {
                case SoundStyle.Ceramic:   // mug: bright, short, ringing
                    freqs = new[] { 1850f, 2770f, 4130f };
                    amps = new[] { 1f, 0.7f, 0.4f };
                    decay = 32f; duration = 0.28f; noise = 0.35f; noiseDecay = 420f;
                    break;
                case SoundStyle.MetalBox:  // ammo boxes: mid, metallic knock
                    freqs = new[] { 430f, 1040f, 1710f, 2650f };
                    amps = new[] { 1f, 0.8f, 0.5f, 0.3f };
                    decay = 20f; duration = 0.35f; noise = 0.55f; noiseDecay = 300f;
                    break;
                default:                    // barrels and crates: low, long thud
                    freqs = new[] { 62f, 118f, 243f, 410f };
                    amps = new[] { 1f, 0.8f, 0.5f, 0.3f };
                    decay = 8f; duration = 0.6f; noise = 0.7f; noiseDecay = 120f;
                    break;
            }

            int samples = (int)(rate * duration);
            var data = new float[samples];
            var rng = new System.Random(12345 + (int)style);
            float peak = 0.0001f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float tone = 0f;
                for (int k = 0; k < freqs.Length; k++)
                    tone += amps[k] * Mathf.Sin(2f * Mathf.PI * freqs[k] * t);
                tone *= Mathf.Exp(-decay * t) / freqs.Length;

                float hit = ((float)rng.NextDouble() * 2f - 1f) * noise * Mathf.Exp(-noiseDecay * t);
                float sample = (tone + hit) * Mathf.Clamp01(t / 0.001f);   // 1 ms fade-in avoids a click
                data[i] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            float gain = 0.9f / peak;
            for (int i = 0; i < samples; i++) data[i] *= gain;

            AudioClip clip = AudioClip.Create("Impact_" + style, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

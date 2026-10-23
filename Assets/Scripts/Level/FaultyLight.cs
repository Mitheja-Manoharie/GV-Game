using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// A damaged ceiling light: every few seconds it shorts out. The fixture's glowing panel stutters, a shower of
    /// sparks falls and a brief warm flash lights the corridor, all at the same moment.
    ///
    /// An electrical crackle plays at the fixture with each fault (a synthesized one unless a clip is assigned).
    /// Put it on a spark particle system (Environment/Effects/Sparks/Sparks_*). It takes over the system's emission
    /// (the sparks then only fall during a fault) and finds the light fixture above it by itself.
    /// The flash is a Realtime point light with no shadows, created at runtime and enabled only for a split second,
    /// so it never affects the bake. The panel flicker uses a MaterialPropertyBlock on that one fixture, so other
    /// fixtures sharing the material are untouched.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [DisallowMultipleComponent]
    public sealed class FaultyLight : MonoBehaviour
    {
        [Header("Fixture")]
        [Tooltip("The light fixture that shorts out. Empty: the nearest Light_* object within 1 m is used.")]
        [SerializeField] private Transform fixture;

        [Header("Timing")]
        [Tooltip("Seconds between faults (random in this range).")]
        [SerializeField] private Vector2 interval = new Vector2(1.5f, 5f);
        [Tooltip("Flickers per fault (random in this range).")]
        [SerializeField] private Vector2Int flickers = new Vector2Int(3, 7);

        [Header("Sparks")]
        [SerializeField] private Vector2Int sparksPerFault = new Vector2Int(8, 18);

        [Header("Flash")]
        [SerializeField] private Color flashColor = new Color(1f, 0.8f, 0.55f);
        [SerializeField] private float flashIntensity = 4f;
        [SerializeField] private float flashRange = 3f;

        [Header("Sound")]
        [Tooltip("Crackle played at the fixture on each fault, in 3D. Empty: an electrical crackle is synthesized.")]
        [SerializeField] private AudioClip crackle = null;
        [Tooltip("Synthesize the crackle when no clip is assigned.")]
        [SerializeField] private bool synthesizeCrackle = true;
        [Range(0f, 1f)] [SerializeField] private float crackleVolume = 0.6f;
        [Tooltip("Distance (m) at which the crackle can no longer be heard.")]
        [SerializeField] private float crackleRange = 15f;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private ParticleSystem sparks;
        private Light flash;
        private AudioSource audioSource;
        private readonly List<(Renderer renderer, int index, Color baseColor)> panels = new List<(Renderer, int, Color)>();
        private MaterialPropertyBlock block;

        private void Awake()
        {
            sparks = GetComponent<ParticleSystem>();
            var emission = sparks.emission;
            emission.enabled = false;                    // sparks only come from faults now (Emit ignores this module)

            if (fixture == null) fixture = FindFixture();
            if (fixture != null) CollectPanels(fixture);
            else Debug.LogWarning("[FaultyLight] No Light_* fixture within 1 m; only sparks and the flash will play.", this);

            var flashObject = new GameObject("FaultFlash");
            flashObject.transform.SetParent(transform, false);
            flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = flashColor;
            flash.intensity = flashIntensity;
            flash.range = flashRange;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            if (crackle != null || synthesizeCrackle)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.minDistance = 1.5f;
                audioSource.maxDistance = crackleRange;
            }

            block = new MaterialPropertyBlock();
        }

        private void OnEnable() => StartCoroutine(Run());

        private void OnDisable()
        {
            StopAllCoroutines();
            SetPanel(1f);
            if (flash != null) flash.enabled = false;
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(Random.Range(0f, interval.y)); // the two faulty lights are out of step

            while (true)
            {
                // Fault: sparks, flash and crackle together, then the panel stutters.
                sparks.Emit(Random.Range(sparksPerFault.x, sparksPerFault.y + 1));
                flash.enabled = true;
                if (audioSource != null)
                {
                    audioSource.pitch = Random.Range(0.9f, 1.12f);
                    audioSource.PlayOneShot(crackle != null ? crackle : SynthCrackle(), crackleVolume);
                }

                int count = Random.Range(flickers.x, flickers.y + 1);
                for (int i = 0; i < count; i++)
                {
                    SetPanel(i % 2 == 0 ? Random.Range(0f, 0.15f) : Random.Range(0.6f, 1f));
                    if (i == 1) flash.enabled = false;
                    yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
                }
                flash.enabled = false;

                // Sometimes it stays dim for a moment before recovering.
                if (Random.value < 0.35f)
                {
                    SetPanel(0.35f);
                    yield return new WaitForSeconds(Random.Range(0.3f, 0.8f));
                }
                SetPanel(1f);

                yield return new WaitForSeconds(Random.Range(interval.x, interval.y));
            }
        }

        // ---------- Synthesized crackle ----------

        private static AudioClip[] synthClips;

        /// <summary>One of four electrical crackles, built once and shared by every faulty light.</summary>
        private static AudioClip SynthCrackle()
        {
            if (synthClips == null || synthClips[0] == null)   // also after Play mode ends without a domain reload
            {
                synthClips = new AudioClip[4];
                for (int i = 0; i < synthClips.Length; i++) synthClips[i] = BuildCrackle(1234 + i * 97);
            }
            return synthClips[Random.Range(0, synthClips.Length)];
        }

        /// <summary>
        /// An electrical short: a dense run of sharp clicks (tiny high-passed noise bursts, most of them early),
        /// over a short hiss and a decaying 120 Hz arc buzz. About 0.35-0.6 s, mono, 44.1 kHz.
        /// </summary>
        private static AudioClip BuildCrackle(int seed)
        {
            const int rate = 44100;
            var rng = new System.Random(seed);
            float length = 0.35f + (float)rng.NextDouble() * 0.25f;
            int n = (int)(rate * length);
            var s = new float[n];

            // Arc buzz: 120 Hz with odd harmonics, a little detuned, fading over ~0.15 s.
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Exp(-t / 0.06f);
                float ph = 2f * Mathf.PI * 120f * t;
                s[i] += 0.10f * env * (Mathf.Sin(ph) + 0.5f * Mathf.Sin(3f * ph + 0.3f) + 0.3f * Mathf.Sin(5f * ph + 1.1f));
            }

            // Hiss bed: white noise, fast decay.
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                s[i] += 0.12f * Mathf.Exp(-t / 0.07f) * ((float)rng.NextDouble() * 2f - 1f);
            }

            // Clicks: 20-40 short bursts, clustered at the start (exponential timing).
            int clicks = 20 + rng.Next(21);
            for (int c = 0; c < clicks; c++)
            {
                float t = -Mathf.Log(1f - (float)rng.NextDouble() * 0.98f) * length * 0.22f;
                int start = (int)(t * rate);
                int width = (int)(rate * (0.0006f + (float)rng.NextDouble() * 0.0025f));
                float amp = (0.35f + 0.65f * (float)rng.NextDouble()) * Mathf.Exp(-t / (length * 0.5f));
                float previous = 0f;
                for (int k = 0; k < width && start + k < n; k++)
                {
                    float noise = (float)rng.NextDouble() * 2f - 1f;
                    float hp = noise - previous;                     // first difference: crisp, high-frequency
                    previous = noise;
                    s[start + k] += amp * hp * (1f - (float)k / width);
                }
            }

            // Normalise to -1 dBFS with a short fade-out so the end never clicks.
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(s[i]));
            int fade = rate / 100;
            for (int i = 0; i < n; i++)
            {
                s[i] *= 0.89f / peak;
                if (i > n - fade) s[i] *= (float)(n - i) / fade;
            }

            var clip = AudioClip.Create("SparkCrackle_" + seed, n, 1, rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        /// <summary>Scales the fixture's emission: 1 = normal, 0 = dark.</summary>
        private void SetPanel(float level)
        {
            foreach (var (renderer, index, baseColor) in panels)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetColor(EmissionColor, baseColor * level);
                renderer.SetPropertyBlock(block, index);
            }
        }

        private void CollectPanels(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (m == null || !m.HasProperty(EmissionColor) || !m.IsKeywordEnabled("_EMISSION")) continue;
                    panels.Add((renderer, i, m.GetColor(EmissionColor)));
                }
            }
        }

        private Transform FindFixture()
        {
            Transform best = null;
            float bestDistance = 1f;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("Light_")) continue;
                float d = Vector3.Distance(t.position, transform.position);
                if (d < bestDistance) { bestDistance = d; best = t; }
            }
            return best;
        }
    }
}

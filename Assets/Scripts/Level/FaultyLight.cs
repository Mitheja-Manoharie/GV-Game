using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// A damaged ceiling light: every few seconds it shorts out. The fixture's glowing panel stutters, a shower of
    /// sparks falls and a brief warm flash lights the corridor, all at the same moment.
    ///
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

        [Header("Sound (optional)")]
        [Tooltip("Crackle played at the fixture on each fault, in 3D.")]
        [SerializeField] private AudioClip crackle = null;
        [Range(0f, 1f)] [SerializeField] private float crackleVolume = 0.6f;

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

            if (crackle != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.minDistance = 1.5f;
                audioSource.maxDistance = 15f;
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
                if (audioSource != null) audioSource.PlayOneShot(crackle, crackleVolume);

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

using System.Collections.Generic;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// Makes a kit computer screen look live: scrolling rows of data in the control room's screen blue, with a
    /// faint flicker and an occasional refresh glitch.
    ///
    /// Put it on a MegaKit Prop_Computer or Prop_AccessPoint. It finds the screen by its material (the kit's
    /// M_Decal_Screen, imported as T_Decals because the importer names materials after their texture)
    /// and swaps that one material slot's base and emission textures for a small generated data texture using a
    /// MaterialPropertyBlock, so the shared kit material and every other prop stay untouched. Each screen gets its own
    /// random layout and speed. Cost: one 64 x 64 texture per screen and a property block update per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LiveScreen : MonoBehaviour
    {
        [Tooltip("Material slots whose name, or whose base texture's name, starts with this are treated as the screen. " +
                 "The kit imports its screen material named after its texture (T_Decals).")]
        [SerializeField] private string screenMaterial = "T_Decals";
        [Tooltip("Screen colour (the control room accent, #5AB4FF).")]
        [SerializeField] private Color screenColor = new Color(0.353f, 0.706f, 1f);
        [Tooltip("Glow strength of the data rows.")]
        [SerializeField] private float emission = 1.6f;
        [Tooltip("How many times the data texture repeats across the screen (more = smaller text).")]
        [SerializeField] private Vector2 tiling = new Vector2(3f, 3f);
        [Tooltip("Scroll speed, in screens per second.")]
        [SerializeField] private float scrollSpeed = 0.04f;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private readonly List<(Renderer renderer, int index)> screens = new List<(Renderer, int)>();
        private MaterialPropertyBlock block;
        private Texture2D data;
        private float offset, speed, glitchUntil, nextGlitch;

        private void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (IsScreen(materials[i])) screens.Add((r, i));
            }

            if (screens.Count == 0)
            {
                var found = new List<string>();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null) found.Add(m.name + (m.mainTexture != null ? $" ({m.mainTexture.name})" : ""));
                Debug.LogWarning($"[LiveScreen] No '{screenMaterial}' material on {name}; nothing to animate. " +
                                 $"Materials found: {string.Join(", ", found)}", this);
                enabled = false;
                return;
            }

            data = MakeDataTexture(Random.Range(0, 100000));
            block = new MaterialPropertyBlock();
            offset = Random.value;
            speed = scrollSpeed * Random.Range(0.7f, 1.3f);
            nextGlitch = Time.time + Random.Range(3f, 9f);
        }

        private bool IsScreen(Material m)
        {
            if (m == null) return false;
            var texture = m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : null;
            foreach (var key in new[] { screenMaterial, "M_Decal_Screen", "T_Decals" })
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (m.name.StartsWith(key) || (texture != null && texture.name.StartsWith(key))) return true;
            }
            return false;
        }

        private void Update()
        {
            float t = Time.time;
            offset = (offset + speed * Time.deltaTime) % 1f;

            // Faint flicker, plus a short refresh glitch now and then (jump and dim).
            float flicker = 0.92f + 0.08f * Mathf.PerlinNoise(t * 7f, offset * 10f);
            float jump = 0f;
            if (t >= nextGlitch)
            {
                glitchUntil = t + Random.Range(0.06f, 0.15f);
                nextGlitch = t + Random.Range(3f, 9f);
            }
            if (t < glitchUntil)
            {
                flicker *= 0.45f;
                jump = 0.13f;
            }

            foreach (var (renderer, index) in screens)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetTexture(BaseMap, data);
                block.SetTexture(EmissionMap, data);
                block.SetVector(BaseMapST, new Vector4(tiling.x, tiling.y, jump, offset));
                block.SetColor(BaseColor, Color.white);
                block.SetColor(EmissionColor, screenColor * (emission * flicker));
                renderer.SetPropertyBlock(block, index);
            }
        }

        private void OnDisable()
        {
            // Back to the original kit screen (e.g. when the component is turned off in the Editor).
            foreach (var (renderer, index) in screens)
                if (renderer != null) renderer.SetPropertyBlock(null, index);
        }

        private void OnDestroy()
        {
            if (data != null) Destroy(data);
        }

        /// <summary>
        /// 64 x 64, tiling: a dark background with rows of "text" — bars of random length in 1-pixel gaps — and the
        /// odd full-width separator. White data on near-black, so the emission colour sets the hue.
        /// </summary>
        private static Texture2D MakeDataTexture(int seed)
        {
            const int size = 64;
            var rng = new System.Random(seed);
            var pixels = new Color32[size * size];
            var background = new Color32(6, 10, 16, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            for (int row = 0; row < size; row += 4)
            {
                if (rng.NextDouble() < 0.15) continue;                    // blank line
                bool separator = rng.NextDouble() < 0.08;
                byte bright = (byte)(separator ? 120 : 150 + rng.Next(105));
                var ink = new Color32(bright, bright, bright, 255);

                int x = 2 + rng.Next(4);
                int end = separator ? size - 2 : 8 + rng.Next(size - 10);
                while (x < end)
                {
                    int word = separator ? end - x : 2 + rng.Next(7);  // "words" with 1-2 px gaps
                    for (int dx = 0; dx < word && x + dx < end; dx++)
                        for (int dy = 0; dy < 2; dy++)
                            pixels[(row + dy) * size + x + dx] = ink;
                    x += word + 1 + rng.Next(2);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "LiveScreenData", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}

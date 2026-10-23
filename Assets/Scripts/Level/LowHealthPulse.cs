using GVGame.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GVGame.Level
{
    /// <summary>
    /// Warning when the player is badly hurt: below the health threshold a red glow pulses in from the screen edges
    /// like a heartbeat and the colours drain a little. The lower the health, the stronger and faster the pulse.
    ///
    ///   - The red edges are a screen overlay (a soft radial texture made at startup, drawn with IMGUI like the HUD).
    ///     A post-processing Vignette multiplies the image, so in this dark level it only made the edges blacker;
    ///     the overlay adds red on top and reads clearly.
    ///   - The desaturation is a second global URP Volume (priority above the level's Global Volume) with Color
    ///     Adjustments, so the level's own post-processing profile is untouched.
    /// Both are off while the player is healthy, so it costs nothing then. It fades out when the player dies and
    /// resets on respawn. The HUD's own damage flash is separate and still plays on every hit.
    /// Put it on the Global Volume object (or any object in Main); it finds the player by the Player tag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LowHealthPulse : MonoBehaviour
    {
        [Tooltip("The pulse starts below this fraction of health and is strongest at 0.")]
        [Range(0.05f, 0.8f)] [SerializeField] private float threshold = 0.35f;
        [SerializeField] private Color color = new Color(0.8f, 0.02f, 0.02f);
        [Tooltip("Opacity of the red edges at the strongest point of a beat, at almost no health.")]
        [Range(0f, 1f)] [SerializeField] private float maxOpacity = 0.6f;
        [Tooltip("How far in from the edges the red reaches (0 = only the very edge, 1 = to the centre).")]
        [Range(0.1f, 1f)] [SerializeField] private float reach = 0.55f;
        [Tooltip("Saturation at the lowest health (0 = unchanged, -100 = grey).")]
        [Range(-100f, 0f)] [SerializeField] private float saturation = -35f;
        [Tooltip("Heartbeats per minute at the threshold and at almost no health.")]
        [SerializeField] private Vector2 beatsPerMinute = new Vector2(70f, 120f);

        private PlayerHealth player;
        private Volume volume;
        private VolumeProfile profile;
        private Texture2D edges;
        private float level, beatPhase, strength;

        private void Awake()
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) player = tagged.GetComponentInParent<PlayerHealth>();
            if (player == null) Debug.LogWarning("[LowHealthPulse] No PlayerHealth on the Player; the pulse is off.", this);

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(saturation);

            var go = new GameObject("LowHealthVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
            volume.weight = 0f;
            volume.enabled = false;

            edges = MakeEdgeTexture(128, reach);
        }

        private void Update()
        {
            if (player == null) return;

            // 0 above the threshold (or dead), 1 at almost no health.
            float target = player.IsAlive ? Mathf.InverseLerp(threshold, 0f, player.Health01) : 0f;
            float dt = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, target, dt * (target > level ? 2f : 1.5f));

            if (level <= 0f)
            {
                strength = 0f;
                beatPhase = 0f;
                if (volume.enabled) { volume.weight = 0f; volume.enabled = false; }
                return;
            }

            // Heartbeat: a sharp peak once per beat, faster as health falls. Never fully off while low.
            float bpm = Mathf.Lerp(beatsPerMinute.x, beatsPerMinute.y, level);
            beatPhase = (beatPhase + dt * bpm / 60f) % 1f;
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(beatPhase * Mathf.PI)), 6f);
            strength = Mathf.Lerp(0.35f, 1f, level) * (0.45f + 0.55f * beat);

            volume.enabled = true;
            volume.weight = level;
        }

        private void OnGUI()
        {
            if (strength <= 0f || edges == null || Event.current.type != EventType.Repaint) return;
            var previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, maxOpacity * strength);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), edges, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        /// <summary>White texture, transparent in the middle and opaque at the edges (stretched to the screen).</summary>
        private static Texture2D MakeEdgeTexture(int size, float reach)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LowHealthEdges" };
            var pixels = new Color32[size * size];
            float inner = 1f - reach;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v) / 1.41421f;        // 0 centre, 1 corner
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner * 0.75f, 1f, d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
            if (edges != null) Destroy(edges);
        }
    }
}

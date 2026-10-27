using System;
using GVGame.Interaction;
using GVGame.Player;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// The keycard that unlocks the exit. It hovers and turns slowly with a small glow so it can be spotted; the
    /// player looks at it and presses E to take it ("Take keycard"). Until then the ExitTrigger refuses to open.
    ///
    /// Put it on a Prop_KeyCard model in the scene. It fits its own box collider to the model (the interact ray
    /// ignores triggers, so the collider is solid but tiny) and adds its glow light at runtime (Realtime, no
    /// shadows, range 1.2 m). A small objective line at the top of the screen tells the player what to do next.
    /// A respawn or "play again" is a new run, so the keycard comes back and has to be found again.
    /// Random location: if Environment/KeycardSpots holds KeycardSpot_* markers, every run (start, respawn, play again)
    /// puts the keycard on one of them at random, never the same spot twice in a row. All spots are placed before
    /// any lockable door, so the Facility AI can
    /// never lock the player away from the keycard. Without markers the keycard stays where it was placed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeycardPickup : MonoBehaviour, IInteractable
    {
        /// <summary>True once the player has taken the keycard in this run.</summary>
        public static bool HasKeycard { get; private set; }

        /// <summary>True when a keycard exists in the scene, so the exit needs one.</summary>
        public static bool InScene => instance != null;

        /// <summary>Raised when the keycard is taken (true) or returned by a respawn (false).</summary>
        public static event Action<bool> KeycardChanged;

        [Header("Look")]
        [SerializeField] private float hoverHeight = 0.08f;
        [SerializeField] private float spinDegreesPerSecond = 60f;
        [SerializeField] private Color glowColor = new Color(0.353f, 0.706f, 1f);   // screen blue #5AB4FF
        [SerializeField] private float glowIntensity = 1.5f;

        [Header("Location")]
        [Tooltip("Parent of the KeycardSpot_* markers. Empty: Environment/KeycardSpots is used if it exists.")]
        [SerializeField] private Transform spotsRoot;

        [Header("Text")]
        [SerializeField] private string objectiveFind = "OBJECTIVE   Find the keycard";
        [SerializeField] private string objectiveExit = "OBJECTIVE   Reach the exit (Control Room)";
        [SerializeField] private string pickedUpMessage = "KEYCARD ACQUIRED   The exit is unlocked";

        private static KeycardPickup instance;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Renderer[] renderers;
        private Collider box;
        private Light glow;
        private AudioSource sound;
        private AudioClip chime;
        private PlayerHealth player;
        private float messageUntil = -1f;
        private readonly System.Collections.Generic.List<Transform> spots = new System.Collections.Generic.List<Transform>();
        private int currentSpot = -1;
        private GUIStyle objectiveStyle, messageStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            HasKeycard = false;
            instance = null;
            KeycardChanged = null;
        }

        private void Awake()
        {
            instance = this;
            HasKeycard = false;
            basePosition = transform.position;
            baseRotation = transform.rotation;
            renderers = GetComponentsInChildren<Renderer>(true);

            if (spotsRoot == null)
            {
                var found = GameObject.Find("Environment/KeycardSpots");
                if (found != null) spotsRoot = found.transform;
            }
            if (spotsRoot != null)
                foreach (Transform spot in spotsRoot)
                    if (spot.name.StartsWith("KeycardSpot_")) spots.Add(spot);
            PickSpot();

            box = GetComponent<Collider>();
            if (box == null) box = FitBoxCollider();

            var glowObject = new GameObject("KeycardGlow");
            glowObject.transform.SetParent(transform, false);
            glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = glowColor;
            glow.intensity = glowIntensity;
            glow.range = 1.2f;
            glow.shadows = LightShadows.None;

            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            chime = BuildChime();

            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) player = tagged.GetComponentInParent<PlayerHealth>();
        }

        private void OnEnable()
        {
            if (player != null) player.Respawned += ResetKeycard;
        }

        private void OnDisable()
        {
            if (player != null) player.Respawned -= ResetKeycard;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (chime != null) Destroy(chime);
        }

        private void Update()
        {
            if (HasKeycard) return;
            float t = Time.time;
            transform.SetPositionAndRotation(
                basePosition + Vector3.up * (hoverHeight * (0.5f + 0.5f * Mathf.Sin(t * 2f))),
                Quaternion.Euler(0f, t * spinDegreesPerSecond, 0f) * baseRotation);
        }

        // ---------- IInteractable ----------

        public string GetPrompt(Transform interactor) => "Take keycard";

        public void Interact(Transform interactor)
        {
            if (HasKeycard) return;
            HasKeycard = true;
            SetVisible(false);
            if (chime != null) sound.PlayOneShot(chime, 0.7f);
            messageUntil = Time.unscaledTime + 3f;
            KeycardChanged?.Invoke(true);
        }

        /// <summary>A new run: the keycard is back, at a new random spot.</summary>
        private void ResetKeycard()
        {
            HasKeycard = false;
            PickSpot();
            transform.SetPositionAndRotation(basePosition, baseRotation);
            SetVisible(true);
            messageUntil = -1f;
            KeycardChanged?.Invoke(false);
        }

        /// <summary>Chooses a random spot (not the previous one) and makes it the keycard's resting place.</summary>
        private void PickSpot()
        {
            if (spots.Count == 0) return;
            int index = UnityEngine.Random.Range(0, spots.Count);
            if (spots.Count > 1 && index == currentSpot) index = (index + 1 + UnityEngine.Random.Range(0, spots.Count - 1)) % spots.Count;
            currentSpot = index;
            basePosition = spots[index].position;
            baseRotation = spots[index].rotation;
            transform.SetPositionAndRotation(basePosition, baseRotation);
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in renderers) if (r != null) r.enabled = visible;
            if (box != null) box.enabled = visible;
            if (glow != null) glow.enabled = visible;
        }

        // ---------- objective line and pickup message ----------

        private void OnGUI()
        {
            if (Time.timeScale == 0f || (player != null && !player.IsAlive)) return;   // escape or death screen

            if (objectiveStyle == null)
            {
                objectiveStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                objectiveStyle.normal.textColor = new Color(0.86f, 0.9f, 1f, 0.9f);
                messageStyle = new GUIStyle(objectiveStyle) { fontSize = 22 };
                messageStyle.normal.textColor = glowColor;
            }

            float w = Screen.width;
            GUI.Label(new Rect(0f, 12f, w, 24f), HasKeycard ? objectiveExit : objectiveFind, objectiveStyle);
            if (Time.unscaledTime < messageUntil)
                GUI.Label(new Rect(0f, Screen.height * 0.3f, w, 30f), pickedUpMessage, messageStyle);
        }

        // ---------- helpers ----------

        /// <summary>A solid box collider fitted to the model's meshes in its own local space.</summary>
        private Collider FitBoxCollider()
        {
            bool any = false;
            var local = new Bounds();
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            var collider = gameObject.AddComponent<BoxCollider>();
            // A little larger than the card, so it is easy to aim at.
            collider.center = any ? local.center : Vector3.zero;
            collider.size = any ? local.size + Vector3.one * 0.06f : Vector3.one * 0.2f;
            return collider;
        }

        /// <summary>A short rising two-note chime, synthesized once.</summary>
        private static AudioClip BuildChime()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.32f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = t < 0.12f ? 880f : 1318.5f;                     // A5, then E6
                float local = t < 0.12f ? t : t - 0.12f;
                float env = Mathf.Min(1f, local / 0.004f) * Mathf.Exp(-local / (t < 0.12f ? 0.05f : 0.09f));
                s[i] = 0.5f * env * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * t));
            }
            var clip = AudioClip.Create("KeycardChime", n, 1, rate, false);
            clip.SetData(s, 0);
            return clip;
        }
    }
}

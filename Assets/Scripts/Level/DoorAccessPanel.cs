using System.Collections;
using System.Collections.Generic;
using LabLockdown.AI.Facility.Runtime;
using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// A wall panel beside a lockable door that shows the door's state: green while it is open, red while the
    /// Facility AI has it locked (with a quick blink and a short beep when it locks).
    ///
    /// Put it on a MegaKit Prop_AccessPoint. It follows FacilityEvents.DoorLockChanged for its door (set Door Id, or
    /// leave it empty to use the nearest Door_* marker within 4 m). It tints the panel's light strip and screen with
    /// a MaterialPropertyBlock (that panel only, the kit materials are untouched) and adds a small coloured Realtime
    /// light with no shadows (range 1.2 m) so the colour spills onto the wall.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorAccessPanel : MonoBehaviour
    {
        [Tooltip("The door this panel shows, e.g. Door_LabSouth. Empty: the nearest Door_* marker within 4 m.")]
        [SerializeField] private string doorId = "";
        [SerializeField] private Color openColor = new Color(0.25f, 1f, 0.4f);
        [SerializeField] private Color lockedColor = new Color(1f, 0.231f, 0.188f);   // emergency red #FF3B30
        [Tooltip("Glow strength of the light strip and screen.")]
        [SerializeField] private float emission = 2f;
        [SerializeField] private float lightIntensity = 1.2f;
        [Tooltip("Beep when the door locks (synthesized).")]
        [SerializeField] private bool beepOnLock = true;
        [Range(0f, 1f)] [SerializeField] private float beepVolume = 0.5f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static AudioClip beep;

        private readonly List<(Renderer renderer, int index)> slots = new List<(Renderer, int)>();
        private MaterialPropertyBlock block;
        private Light glow;
        private AudioSource sound;
        private bool locked;

        private void Awake()
        {
            if (string.IsNullOrEmpty(doorId)) doorId = NearestDoor();

            // The light strip (M_LightFade_*) and the screen (M_Decal_Screen, imported as T_Decals).
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (m == null) continue;
                    if (m.name.StartsWith("M_LightFade") || m.name.StartsWith("M_Decal_Screen") || m.name.StartsWith("T_Decals"))
                        slots.Add((r, i));
                }
            }

            block = new MaterialPropertyBlock();

            var glowObject = new GameObject("PanelGlow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = new Vector3(0.35f, 1.35f, 0f);   // in front of the panel face
            glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 1.2f;
            glow.intensity = lightIntensity;
            glow.shadows = LightShadows.None;

            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 1f;
            sound.rolloffMode = AudioRolloffMode.Linear;
            sound.minDistance = 1.5f;
            sound.maxDistance = 12f;

            if (string.IsNullOrEmpty(doorId)) Debug.LogWarning($"[DoorAccessPanel] {name}: no door found within 4 m; it stays green.", this);
        }

        private void OnEnable()
        {
            FacilityEvents.DoorLockChanged += OnDoorLockChanged;
            locked = !string.IsNullOrEmpty(doorId) && FacilityEvents.IsDoorLocked(doorId);
            Apply(locked ? lockedColor : openColor, 1f);
        }

        private void OnDisable()
        {
            FacilityEvents.DoorLockChanged -= OnDoorLockChanged;
            StopAllCoroutines();
        }

        private void OnDoorLockChanged(string id, bool isLocked)
        {
            if (id != doorId || isLocked == locked) return;
            locked = isLocked;
            StopAllCoroutines();
            if (locked) StartCoroutine(LockBlink());
            else Apply(openColor, 1f);
        }

        private IEnumerator LockBlink()
        {
            if (beepOnLock) sound.PlayOneShot(Beep(), beepVolume);
            for (int i = 0; i < 3; i++)
            {
                Apply(lockedColor, 1f);
                yield return new WaitForSeconds(0.12f);
                Apply(lockedColor, 0.15f);
                yield return new WaitForSeconds(0.1f);
            }
            Apply(lockedColor, 1f);
        }

        private void Apply(Color color, float level)
        {
            foreach (var (renderer, index) in slots)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetColor(BaseColor, Color.Lerp(Color.white, color, 0.6f));
                block.SetColor(EmissionColor, color * (emission * level));
                renderer.SetPropertyBlock(block, index);
            }
            if (glow != null)
            {
                glow.color = color;
                glow.intensity = lightIntensity * level;
            }
        }

        private string NearestDoor()
        {
            var doors = GameObject.Find("Environment/Doors");
            if (doors == null) return "";
            string best = "";
            float bestDistance = 4f;
            foreach (Transform door in doors.transform)
            {
                if (!door.name.StartsWith("Door_")) continue;
                float d = Vector3.Distance(door.position, transform.position);
                if (d < bestDistance) { bestDistance = d; best = door.name; }
            }
            return best;
        }

        /// <summary>Two short low beeps, built once and shared.</summary>
        private static AudioClip Beep()
        {
            if (beep != null) return beep;
            const int rate = 44100;
            int n = (int)(rate * 0.3f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                bool on = t < 0.09f || (t > 0.15f && t < 0.24f);
                float local = t < 0.12f ? t : t - 0.15f;
                float env = on ? Mathf.Min(1f, local / 0.004f) * Mathf.Min(1f, (0.09f - local) / 0.01f) : 0f;
                s[i] = 0.45f * Mathf.Max(0f, env) * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 520f * t)) * 0.6f;
            }
            beep = AudioClip.Create("DoorLockBeep", n, 1, rate, false);
            beep.SetData(s, 0);
            return beep;
        }
    }
}

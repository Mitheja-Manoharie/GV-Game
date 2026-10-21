using GVGame.Interaction;
using UnityEngine;
using UnityEngine.Audio;

namespace GVGame.Audio
{
    /// <summary>
    /// Player footstep sounds, timed by the footstep noise the PlayerController already raises for the AI.
    ///
    /// The controller emits <see cref="InteractionNoise"/> on every step (louder when sprinting, nothing when
    /// crouching or in the air). This plays a footstep clip on each of those events, so what the player hears is
    /// exactly what the Security Hunter hears. No change to the player's own scripts is needed.
    /// Put it on any object in Main (e.g. FacilityAI); it finds the player by the Player tag.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        [Tooltip("Footstep clips; one is picked at random each step (never the same twice in a row).")]
        [SerializeField] private AudioClip[] steps = new AudioClip[0];

        [Range(0f, 1f)] [SerializeField] private float walkVolume = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float sprintVolume = 0.6f;
        [Tooltip("Noise loudness at or above this counts as sprinting (PlayerController: walk 3, sprint 8).")]
        [SerializeField] private float sprintLoudness = 5f;
        [Tooltip("Random pitch range, so repeated steps don't sound identical.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.1f);
        [Tooltip("Optional mixer group, e.g. SFX.")]
        [SerializeField] private AudioMixerGroup output;

        private GameObject player;
        private AudioSource source;
        private int lastIndex = -1;

        private void Awake()
        {
            player = GameObject.FindWithTag("Player");
            if (player == null) Debug.LogWarning("[PlayerFootsteps] No object tagged Player; footsteps are silent.", this);

            var child = new GameObject("Footsteps");
            child.transform.SetParent(transform, false);
            source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // the player's own steps: heard the same from the camera
            source.outputAudioMixerGroup = output;
        }

        private void OnEnable() => InteractionNoise.NoiseEmitted += OnNoise;
        private void OnDisable() => InteractionNoise.NoiseEmitted -= OnNoise;

        private void OnNoise(Vector3 position, float loudness, GameObject noiseSource)
        {
            // Only the player's footsteps. Doors and gunshots can also name the player as their source, but steps are
            // emitted exactly at the player's position and are much quieter than a gunshot.
            if (player == null || noiseSource != player || loudness > sprintLoudness * 3f) return;
            if ((position - player.transform.position).sqrMagnitude > 0.25f) return;
            if (steps == null || steps.Length == 0) return;

            int index = Random.Range(0, steps.Length);
            if (steps.Length > 1 && index == lastIndex) index = (index + 1) % steps.Length;
            lastIndex = index;
            if (steps[index] == null) return;

            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.PlayOneShot(steps[index], loudness >= sprintLoudness ? sprintVolume : walkVolume);
        }
    }
}

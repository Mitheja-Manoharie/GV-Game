using GVGame.Agents;
using GVGame.Combat;
using LabLockdown.AI.Hunter.Runtime;
using UnityEngine;
using UnityEngine.Audio;

namespace GVGame.Audio
{
    /// <summary>
    /// Gunshot sounds, played on the guns' existing "Fired" events, so no weapon script has to change.
    ///
    /// Two ways to use it:
    ///   - On a scene object (e.g. FacilityAI): plays the player's rifle (WeaponController on the Player), heard in 2D.
    ///   - On a robot prefab: plays that robot's shots (HunterShooter, else RobotGun) in 3D at the robot,
    ///     so distant robots sound distant.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GunshotAudio : MonoBehaviour
    {
        [Tooltip("Gunshot clips; one is picked at random each shot.")]
        [SerializeField] private AudioClip[] shots = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float volume = 0.7f;
        [Tooltip("Random pitch range, so rapid fire doesn't sound like one repeated sample.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(0.93f, 1.07f);
        [Tooltip("Robots only: distance (m) at which a shot is no longer heard.")]
        [SerializeField] private float maxDistance = 40f;
        [Tooltip("Optional mixer group, e.g. SFX.")]
        [SerializeField] private AudioMixerGroup output;

        private WeaponController playerWeapon;
        private HunterShooter hunterGun;
        private RobotGun robotGun;
        private AudioSource source;
        private int lastIndex = -1;

        private void Awake()
        {
            hunterGun = GetComponentInChildren<HunterShooter>();
            if (hunterGun == null) robotGun = GetComponentInChildren<RobotGun>();
            bool onRobot = hunterGun != null || robotGun != null;

            if (!onRobot)
            {
                playerWeapon = GetComponentInChildren<WeaponController>();
                if (playerWeapon == null)
                {
                    var player = GameObject.FindWithTag("Player");
                    if (player != null) playerWeapon = player.GetComponentInChildren<WeaponController>();
                }

                if (playerWeapon == null) Debug.LogWarning("[GunshotAudio] No gun found; gunshots are silent.", this);
            }

            var child = new GameObject("Gunshots");
            child.transform.SetParent(transform, false);
            source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = output;
            source.spatialBlend = onRobot ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = maxDistance;
        }

        private void OnEnable()
        {
            if (playerWeapon != null) playerWeapon.Fired += Play;
            if (hunterGun != null) hunterGun.Fired += OnRobotFired;
            if (robotGun != null) robotGun.Fired += OnRobotFired;
        }

        private void OnDisable()
        {
            if (playerWeapon != null) playerWeapon.Fired -= Play;
            if (hunterGun != null) hunterGun.Fired -= OnRobotFired;
            if (robotGun != null) robotGun.Fired -= OnRobotFired;
        }

        private void OnRobotFired(bool hit) => Play();

        private void Play()
        {
            if (shots == null || shots.Length == 0) return;

            int index = Random.Range(0, shots.Length);
            if (shots.Length > 1 && index == lastIndex) index = (index + 1) % shots.Length;
            lastIndex = index;
            if (shots[index] == null) return;

            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.PlayOneShot(shots[index], volume);
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Interaction
{
    /// <summary>
    /// Makes a prop physically interactive: configures its Rigidbody, handles impact noise,
    /// and (for barricades) keeps the NavMesh in sync via a carving NavMeshObstacle so AI
    /// agents path around a barricade the player has dragged into a new spot.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsProp : MonoBehaviour
    {
        public enum Kind { Throwable, Barricade }

        [Header("Type")]
        public Kind kind = Kind.Throwable;

        [Header("Rigidbody")]
        public float mass = 1f;
        public float linearDamping = 0.1f;
        public float angularDamping = 0.2f;

        [Header("Impact noise")]
        [Tooltip("Minimum impact speed (m/s) that makes a sound / noise.")]
        public float noiseSpeedThreshold = 2.5f;
        public float noiseLoudness = 8f;
        public AudioClip impactClip;
        [Range(0f, 1f)] public float impactVolume = 0.7f;
        public float noiseCooldown = 0.25f;

        [Header("Barricade NavMesh sync")]
        public bool carveNavMesh = true;

        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; set; }

        float _lastNoiseTime = -10f;
        AudioSource _audio;
        NavMeshObstacle _obstacle;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            ApplyRigidbodySettings();

            if (impactClip != null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.maxDistance = 25f;
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
            if (speed < noiseSpeedThreshold) return;
            if (Time.time - _lastNoiseTime < noiseCooldown) return;
            _lastNoiseTime = Time.time;

            float scale = Mathf.Clamp(speed / 8f, 0.4f, 1.5f);
            if (_audio != null) _audio.PlayOneShot(impactClip, impactVolume * Mathf.Clamp01(scale));
            InteractionNoise.Emit(c.GetContact(0).point, noiseLoudness * scale, gameObject);
        }
    }
}

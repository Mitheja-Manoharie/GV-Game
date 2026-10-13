using UnityEngine;
using GVGame.Combat;

namespace GVGame.Interaction
{
    /// <summary>
    /// Health pack. Walk into the trigger to heal (only if you actually need healing).
    /// Works with anything implementing IHealable, so it does not depend on the player class.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HealthPickup : MonoBehaviour
    {
        public float healAmount = 35f;
        [Tooltip("Seconds until the pack comes back. 0 = one use.")]
        public float respawnSeconds = 0f;
        public AudioClip pickupClip;
        [Header("Idle animation")]
        public float spinSpeed = 90f;
        public float bobHeight = 0.08f;
        public float bobSpeed = 2f;

        Vector3 _startPos;
        bool _active = true;
        Renderer[] _renderers;

        void Reset() { GetComponent<Collider>().isTrigger = true; }

        void Awake()
        {
            _startPos = transform.position;
            _renderers = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            if (!_active) return;
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            Vector3 p = _startPos;
            p.y += Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = p;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_active) return;
            var target = other.GetComponentInParent<IHealable>();
            if (target == null || !target.NeedsHealing) return;

            target.Heal(healAmount);
            if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position);
            SetActive(false);
            if (respawnSeconds > 0f) Invoke(nameof(Restore), respawnSeconds);
        }

        void Restore() => SetActive(true);

        void SetActive(bool a)
        {
            _active = a;
            foreach (var r in _renderers) r.enabled = a;
            GetComponent<Collider>().enabled = a;
        }
    }
}

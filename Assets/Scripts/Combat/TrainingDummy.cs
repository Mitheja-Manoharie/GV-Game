using UnityEngine;

namespace GVGame.Combat
{
    /// <summary>
    /// Simple shootable target for testing the weapon before robots exist.
    /// Flashes when hit, dies at 0 health, and respawns after a delay.
    /// </summary>
    public class TrainingDummy : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public float respawnDelay = 3f;

        public bool IsAlive { get; private set; } = true;
        public float Health { get; private set; }

        Renderer[] _renderers;
        Collider[] _colliders;
        float _flashEnd;

        void Awake()
        {
            Health = maxHealth;
            _renderers = GetComponentsInChildren<Renderer>();
            _colliders = GetComponentsInChildren<Collider>();
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            Health -= info.amount;
            Tint(new Color(1f, 0.25f, 0.2f));
            _flashEnd = Time.time + 0.12f;
            if (Health <= 0f) Die();
        }

        void Die()
        {
            IsAlive = false;
            SetVisible(false);
            Invoke(nameof(Respawn), respawnDelay);
        }

        void Respawn()
        {
            Health = maxHealth;
            IsAlive = true;
            SetVisible(true);
            Tint(Color.white);
        }

        void SetVisible(bool v)
        {
            foreach (var r in _renderers) r.enabled = v;
            foreach (var c in _colliders) c.enabled = v;
        }

        void Tint(Color c)
        {
            foreach (var r in _renderers)
            {
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
                else if (r.material.HasProperty("_Color")) r.material.color = c;
            }
        }

        void Update()
        {
            if (IsAlive && _flashEnd > 0f && Time.time >= _flashEnd)
            {
                _flashEnd = 0f;
                Tint(new Color(0.75f, 0.8f, 0.9f));
            }
        }
    }
}

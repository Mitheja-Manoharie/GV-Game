using System;
using GVGame.Combat;
using LabLockdown.AI.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace GVGame.Agents
{
    /// <summary>
    /// Robot health. The player's weapon damages it through <see cref="IDamageable"/>; the brains read it through
    /// <see cref="IRobotHealth"/>. On death the robot stops moving and thinking straight away (so the Facility AI's
    /// spawner no longer counts it as alive), plays its death animation, and is removed a few seconds later.
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotHealth : MonoBehaviour, IDamageable, IRobotHealth
    {
        [Tooltip("125 = five hits from the player's rifle (25 each): about 4 s to kill for an average player, " +
                 "the time the Facility AI's pacing simulation assumes.")]
        [Min(1f)] public float maxHealth = 125f;

        [Tooltip("Seconds the body stays after death, for the death animation.")]
        public float removeAfter = 6f;

        public float Health { get; private set; }
        public float Health01 => Mathf.Clamp01(Health / maxHealth);
        public bool IsAlive => Health > 0f;

        public event Action<DamageInfo> Damaged;
        public event Action Died;

        void Awake() => Health = maxHealth;

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive || info.amount <= 0f) return;

            Health = Mathf.Max(0f, Health - info.amount);
            Damaged?.Invoke(info);
            if (Health <= 0f) Die();
        }

        void Die()
        {
            Died?.Invoke();

            // Stop moving: the follower first (no events), then the agent.
            var follower = GetComponent<RobotPathFollower>();
            if (follower != null) follower.enabled = false;
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            // No colliders on the body: bullets pass through, and it no longer blocks doorways or spawn points.
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

            Destroy(gameObject, removeAfter);
        }
    }
}

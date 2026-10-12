using System.Collections.Generic;
using UnityEngine;

namespace LabLockdown.AI.Facility.Runtime
{
    /// <summary>
    /// Stand-in enemy spawner: instantiates the assigned prefab (or a red capsule) and removes it after a
    /// lifetime, as if the player had defeated it, so the Facility AI's pacing loop can be watched in play.
    /// Swap for the Agent Controller's spawner once robots exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderSpawnService : MonoBehaviour, ISpawnService
    {
        [Tooltip("Optional enemy prefab. Leave empty to spawn red capsules.")]
        [SerializeField] private GameObject enemyPrefab;

        [Tooltip("Seconds before a placeholder enemy disappears (simulates being defeated). 0 = never.")]
        [SerializeField] private float lifetimeSeconds = 20f;

        private readonly List<GameObject> alive = new List<GameObject>();
        private Material capsuleMaterial;

        public int AliveCount
        {
            get
            {
                alive.RemoveAll(enemy => enemy == null);
                return alive.Count;
            }
        }

        public void Spawn(Vector3 position, Quaternion rotation, string spawnPointId)
        {
            GameObject enemy;
            if (enemyPrefab != null)
            {
                enemy = Instantiate(enemyPrefab, position, rotation);
            }
            else
            {
                enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                enemy.transform.SetPositionAndRotation(position + Vector3.up, rotation);
                enemy.GetComponent<Renderer>().sharedMaterial = CapsuleMaterial();
            }

            enemy.name = $"Enemy ({spawnPointId})";
            alive.Add(enemy);
            if (lifetimeSeconds > 0f) Destroy(enemy, lifetimeSeconds);
        }

        [ContextMenu("Defeat All Enemies")]
        public void DefeatAll()
        {
            foreach (var enemy in alive)
                if (enemy != null) Destroy(enemy);
            alive.Clear();
        }

        private Material CapsuleMaterial()
        {
            if (capsuleMaterial != null) return capsuleMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            capsuleMaterial = new Material(shader) { name = "PlaceholderEnemy", color = new Color(0.9f, 0.25f, 0.2f) };
            return capsuleMaterial;
        }
    }
}

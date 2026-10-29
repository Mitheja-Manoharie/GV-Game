using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// Spins the propeller of a MegaKit Prop_Fan_Small. The fan is rigged: its blades are skinned to a bone called
    /// "Propeller", so turning that bone about its own axis turns the blades while the housing stays still.
    /// Each fan starts at a random angle and runs at a slightly different speed, so several fans never look in step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FanSpin : MonoBehaviour
    {
        [Tooltip("Name of the bone that carries the blades.")]
        [SerializeField] private string propellerBone = "Propeller";
        [Tooltip("Spin speed in degrees per second.")]
        [SerializeField] private float degreesPerSecond = 540f;

        private Transform propeller;
        private float speed;

        private void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == propellerBone) { propeller = t; break; }

            if (propeller == null)
            {
                Debug.LogWarning($"[FanSpin] No '{propellerBone}' bone under {name}; nothing to spin.", this);
                enabled = false;
                return;
            }

            speed = degreesPerSecond * Random.Range(0.85f, 1.15f);
            propeller.Rotate(0f, Random.Range(0f, 360f), 0f, Space.Self);
        }

        private void Update() => propeller.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
    }
}

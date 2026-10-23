using UnityEngine;

namespace GVGame.Level
{
    /// <summary>
    /// Makes a specimen in a tank look suspended in liquid: a slow bob up and down, a gentle turn back and forth
    /// and a slight tilt. Put it on each alien inside a Prop_SpecimenTank.
    ///
    /// Every specimen gets a random phase and slightly different speeds, so they never move in step. The motion is
    /// an offset from the pose placed in the scene, applied in LateUpdate (after any Animator), and costs a few
    /// sine calls per frame. The object must not be Batching Static, or Unity cannot move its meshes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpecimenFloat : MonoBehaviour
    {
        [Tooltip("How far it rises and sinks from its placed height (metres).")]
        [SerializeField] private float bobHeight = 0.06f;
        [Tooltip("Seconds for one full rise and sink.")]
        [SerializeField] private float bobPeriod = 5f;
        [Tooltip("How far it turns either way about the vertical (degrees).")]
        [SerializeField] private float turnAngle = 8f;
        [Tooltip("Seconds for one full turn back and forth.")]
        [SerializeField] private float turnPeriod = 11f;
        [Tooltip("Slight sideways tilt (degrees).")]
        [SerializeField] private float tiltAngle = 2f;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private float phase, speed;

        private void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            phase = Random.Range(0f, 100f);
            speed = Random.Range(0.85f, 1.15f);
        }

        private void LateUpdate()
        {
            float t = (Time.time + phase) * speed;
            float bob = Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(0.1f, bobPeriod)) * bobHeight;
            float turn = Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(0.1f, turnPeriod)) * turnAngle;
            float tilt = Mathf.Sin(t * 2f * Mathf.PI / Mathf.Max(0.1f, bobPeriod * 1.7f)) * tiltAngle;

            transform.localPosition = basePosition + Vector3.up * bob;
            transform.localRotation = Quaternion.Euler(0f, turn, tilt) * baseRotation;
        }

        private void OnDisable()
        {
            // Leave it where it was placed (e.g. when the component is turned off in the Editor).
            transform.localPosition = basePosition;
            transform.localRotation = baseRotation;
        }
    }
}

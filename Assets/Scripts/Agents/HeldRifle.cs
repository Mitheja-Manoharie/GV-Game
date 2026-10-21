using UnityEngine;

namespace GVGame.Agents
{
    /// <summary>
    /// Keeps a rifle in a humanoid's hands, whatever the animation does with the hand bone's rotation.
    /// Every frame, after the Animator: the grip sits in the right palm and the barrel points from the right hand
    /// towards the left hand, which supports the barrel in the Mixamo rifle clips (idle, run, firing). When the left
    /// hand is not near the barrel (e.g. the plain Walking clip swings the arms), the barrel points where the body
    /// faces instead, so the rifle never flips round.
    /// Used by the robots and the player's Alien body. Added by GV Game > Agent Setup (steps 4 and 7, or 9 on its own).
    /// </summary>
    [DisallowMultipleComponent]
    public class HeldRifle : MonoBehaviour
    {
        public Transform rightHand;
        public Transform leftHand;
        [Tooltip("The character root: its forward is the fallback barrel direction.")]
        public Transform body;
        [Tooltip("Turns the model's barrel axis to +Z and its top to +Y (Quaternius Gun_Rifle: barrel along +X).")]
        public Vector3 modelRotation = new Vector3(0f, -90f, 0f);
        [Tooltip("Point of the rifle model (its own space) that sits in the palm: the pistol grip.")]
        public Vector3 gripPoint = new Vector3(0f, -0.07f, 0f);
        [Tooltip("The palm, relative to the right hand bone (the bone starts at the wrist).")]
        public Vector3 palmOffset = new Vector3(0f, 0.08f, 0f);
        [Tooltip("The left hand counts as supporting the barrel between these distances from the right hand (m).")]
        public Vector2 supportDistance = new Vector2(0.15f, 0.9f);
        [Tooltip("...and when it is at least this much in front (dot product with the body's forward).")]
        [Range(-1f, 1f)] public float minSupportForward = 0.3f;
        [Tooltip("Pulls the barrel towards where the body faces. The rifle clips carry the rifle across the chest with " +
                 "the hands close together; 0 follows the hands exactly, 1 is halfway to straight ahead.")]
        [Min(0f)] public float forwardBias = 1f;

        void LateUpdate() => Align();

        /// <summary>Puts the rifle in the hands for the current pose.</summary>
        public void Align()
        {
            if (rightHand == null) return;

            Vector3 forward = body != null ? body.forward : transform.root.forward;
            Vector3 palm = rightHand.TransformPoint(palmOffset);
            Vector3 barrel = forward;

            if (leftHand != null)
            {
                Vector3 toLeft = leftHand.position - palm;
                float distance = toLeft.magnitude;
                if (distance > supportDistance.x && distance < supportDistance.y &&
                    Vector3.Dot(toLeft / distance, forward) >= minSupportForward)
                    barrel = (toLeft / distance + forward * forwardBias).normalized;
            }

            Quaternion rotation = Quaternion.LookRotation(barrel, Vector3.up) * Quaternion.Euler(modelRotation);
            Vector3 grip = rotation * Vector3.Scale(gripPoint, transform.lossyScale); // the rifle may be scaled down
            transform.SetPositionAndRotation(palm - grip, rotation);
        }
    }
}

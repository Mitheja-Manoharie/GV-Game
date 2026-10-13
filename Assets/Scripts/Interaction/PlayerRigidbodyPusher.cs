using UnityEngine;

namespace GVGame.Interaction
{
    /// <summary>
    /// Add this next to the player's CharacterController.
    ///  - Walking into SMALL props (mugs, ammo boxes) nudges them.
    ///  - Heavy props (barrels, crates, barricades) are NOT pushed by walking: they block the player
    ///    like a wall and can only be moved deliberately by dragging them (F).
    ///  - Walking into a door leaf that is blocked by a heavy prop forces it open slowly (shouldering it),
    ///    just far enough to squeeze through (see DoorController.ForceOpen).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerRigidbodyPusher : MonoBehaviour
    {
        [Tooltip("Top speed (m/s) a light prop reaches when walked into.")]
        public float pushSpeed = 2.5f;
        [Tooltip("Props heavier than this cannot be pushed by walking into them.")]
        public float maxPushMass = 10f;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Walking INTO a door (not sliding along it) counts as trying to force it open.
            DoorController door = hit.collider.GetComponentInParent<DoorController>();
            if (door != null)
            {
                if (Vector3.Dot(hit.moveDirection, hit.normal) < -0.3f)
                    door.ForceOpen(transform, hit.collider);
                return;
            }

            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;
            if (rb.mass > maxPushMass) return;               // heavy: acts as a solid obstacle
            if (hit.moveDirection.y < -0.3f) return;         // don't push things we're standing on

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // Only add speed up to pushSpeed: no endless acceleration, no launching props.
            float along = Vector3.Dot(rb.linearVelocity, dir);
            if (along < pushSpeed)
                rb.AddForce(dir * (pushSpeed - along), ForceMode.VelocityChange);
        }
    }
}

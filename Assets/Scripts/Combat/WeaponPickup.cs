using UnityEngine;

namespace GVGame.Combat
{
    /// <summary>
    /// Trigger that gives the player the weapon (first time) and extra ammo. Place on the
    /// WeaponPickup marker in the armoury.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WeaponPickup : MonoBehaviour
    {
        public int ammo = 24;
        public bool oneUse = true;
        public AudioClip pickupClip;

        void Reset() { GetComponent<Collider>().isTrigger = true; }

        void OnTriggerEnter(Collider other)
        {
            var weapon = other.GetComponentInParent<WeaponController>();
            if (weapon == null) return;
            weapon.Equip(ammo);
            if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position);
            if (oneUse) gameObject.SetActive(false);
        }
    }
}

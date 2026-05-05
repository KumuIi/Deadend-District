#nullable enable
using UnityEngine;
using FracturedProtocol.Combat.Instances;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.FireBehaviors
{
    /// <summary>
    /// Instant-hit fire strategy. Casts a ray from muzzle, draws a debug
    /// line for 0.5 s, and logs the first collider hit.
    /// </summary>
    [CreateAssetMenu(fileName = "New_HitscanFire", menuName = "FracturedProtocol/Fire Behaviors/Hitscan")]
    public sealed class HitscanFireSO : FireBehaviorSO
    {
        [SerializeField] private float maxRange = 1000f;

        public override void Fire(WeaponInstance weapon, Vector3 origin, Vector3 direction, AmmoSO? ammo)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxRange))
            {
                Debug.Log($"[HitscanFire] Hit: {hit.collider.name} at {hit.distance:F1} m");
                Debug.DrawLine(origin, hit.point, Color.red, 0.5f);
            }
            else
            {
                Debug.DrawRay(origin, direction * maxRange, Color.red, 0.5f);
            }
        }
    }
}

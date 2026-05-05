#nullable enable
using UnityEngine;
using FracturedProtocol.Combat.Instances;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.FireBehaviors
{
    /// <summary>
    /// Strategy object for weapon firing. Subclass to add new fire types
    /// (hitscan, projectile, burst, etc.) without modifying WeaponSO.
    /// </summary>
    public abstract class FireBehaviorSO : ScriptableObject
    {
        /// <summary>Execute one fire event from the given weapon.</summary>
        public abstract void Fire(WeaponInstance weapon, Vector3 origin, Vector3 direction, AmmoSO? ammo);
    }
}

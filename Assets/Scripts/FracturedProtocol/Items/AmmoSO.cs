#nullable enable
using UnityEngine;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>Defines a single ammunition type's ballistic properties.</summary>
    [CreateAssetMenu(fileName = "New_Ammo", menuName = "Items/Ammo")]
    public sealed class AmmoSO : ItemSO
    {
        public float damage;
        public float penetration;
        public float muzzleVelocity;
        public float dropFactor;
        public float fragmentationChance;
    }
}

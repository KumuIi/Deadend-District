#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>Defines a magazine template: capacity and which ammo types it accepts.</summary>
    [CreateAssetMenu(fileName = "New_Magazine", menuName = "Items/Magazine")]
    public sealed class MagazineSO : ItemSO
    {
        public int capacity;
        public List<AmmoSO> compatibleAmmo = new List<AmmoSO>();
    }
}

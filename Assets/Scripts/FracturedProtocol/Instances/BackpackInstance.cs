#nullable enable
using System;
using FracturedProtocol.Combat.Containers;
using UnityEngine;

namespace FracturedProtocol.Combat.Instances
{
    /// <summary>
    /// Runtime state for a backpack. Carries its own Inventory grid so contents
    /// persist when the backpack is unequipped and re-equipped.
    /// </summary>
    [Serializable]
    public sealed class BackpackInstance : ItemInstance
    {
        /// <summary>The persistent item grid owned by this backpack.</summary>
        public Inventory contents = new Inventory(new Vector2Int(1, 1));
    }
}

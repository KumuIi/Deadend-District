#nullable enable
using System;
using FracturedProtocol.Combat.Items;
using FracturedProtocol.Combat.Registry;

namespace FracturedProtocol.Combat.Instances
{
    /// <summary>
    /// Runtime state for a magazine. Tracks loaded ammo type and remaining rounds.
    /// </summary>
    [Serializable]
    public sealed class MagazineInstance : ItemInstance
    {
        /// <summary>ItemId of the loaded AmmoSO, or empty if the magazine is empty.</summary>
        public string loadedAmmoId = string.Empty;
        public int currentRounds;

        /// <summary>True when no rounds remain.</summary>
        public bool IsEmpty => currentRounds <= 0;

        /// <summary>Clear existing rounds, set ammo type, fill to the provided amount.</summary>
        public void Load(AmmoSO ammoType, int amount)
        {
            loadedAmmoId = ammoType.ItemId;
            int capacity = (def as MagazineSO)?.capacity ?? amount;
            currentRounds = Math.Min(amount, capacity);
        }

        /// <summary>Consume one round. Returns the resolved AmmoSO or null if empty.</summary>
        public AmmoSO? Consume()
        {
            if (IsEmpty) return null;
            currentRounds--;
            return ItemRegistry.Instance?.Get(loadedAmmoId) as AmmoSO;
        }
    }
}

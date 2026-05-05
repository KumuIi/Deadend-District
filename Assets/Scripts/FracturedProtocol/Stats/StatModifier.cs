#nullable enable
using System;
using UnityEngine;

namespace FracturedProtocol.Combat.Stats
{
    /// <summary>Which weapon stat a modifier targets.</summary>
    public enum StatType
    {
        Spread,
        FireRate,
        Damage,
        Penetration,
        MuzzleVelocity,
        RecoilX,
        RecoilY,
    }

    /// <summary>How a modifier's value is applied to the base stat.</summary>
    public enum ModifierOperation
    {
        /// <summary>effectiveStat += value</summary>
        Additive,
        /// <summary>effectiveStat *= value  (e.g. 0.8 = −20%)</summary>
        Multiplicative,
    }

    /// <summary>A single stat tweak applied by an attachment or magazine.</summary>
    [Serializable]
    public sealed class StatModifier
    {
        public StatType statType;
        public ModifierOperation operation;
        public float value;
    }
}

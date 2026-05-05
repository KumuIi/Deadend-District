#nullable enable
using System;
using System.Collections.Generic;
using FracturedProtocol.Combat.Stats;

namespace FracturedProtocol.Combat.Instances
{
    /// <summary>
    /// Runtime state for a weapon: current magazine, inserted attachments, and
    /// the pre-calculated effective stats (never walked per-frame).
    /// </summary>
    [Serializable]
    public sealed class WeaponInstance : ItemInstance
    {
        /// <summary>The magazine currently inserted. Null if the weapon is unloaded.</summary>
        public MagazineInstance? currentMagazine;

        public List<AttachmentInstance> attachments = new List<AttachmentInstance>();

        /// <summary>
        /// Fully resolved stats. Recalculated by StatCalculator on equip/attach/detach/reload.
        /// Never recomputed inside Update.
        /// </summary>
        [NonSerialized] public WeaponStats effectiveStats;
    }
}

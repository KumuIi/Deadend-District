#nullable enable
using System;
using UnityEngine;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.Instances
{
    /// <summary>
    /// Base runtime state for any item. itemId resolves to an ItemSO template
    /// via ItemRegistry; def is never serialized and is rebuilt on load.
    /// </summary>
    [Serializable]
    public class ItemInstance
    {
        public string itemId = string.Empty;
        public Vector2Int gridPos;
        public bool rotated;

        /// <summary>Resolved template reference. Set by ItemRegistry on load; never serialized.</summary>
        [NonSerialized] public ItemSO? def;
    }
}

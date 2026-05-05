#nullable enable
using System;
using UnityEngine;

namespace FracturedProtocol.Combat.Items
{
    /// <summary>
    /// Base template for every item type. Never mutated at runtime —
    /// all mutable state lives in the corresponding ItemInstance subclass.
    /// </summary>
    public abstract class ItemSO : ScriptableObject
    {
        [SerializeField] private string itemId = string.Empty;

        /// <summary>Stable GUID that identifies this template across save files.</summary>
        public string ItemId => itemId;

        public string displayName = string.Empty;
        public Sprite? icon;
        public GameObject? worldPrefab;
        public float weight;
        public Vector2Int gridSize = Vector2Int.one;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(itemId))
            {
                itemId = Guid.NewGuid().ToString();
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}

#nullable enable
using System.Collections.Generic;
using UnityEngine;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.Registry
{
    /// <summary>
    /// SO singleton that maps itemId GUIDs to their ItemSO templates.
    /// In editor builds the lookup is refreshed via RefreshFromAssets().
    /// In player builds it is built from the serialized itemList on Awake.
    /// Load via Resources.Load&lt;ItemRegistry&gt;("FracturedProtocol/ItemRegistry").
    /// </summary>
    [CreateAssetMenu(fileName = "ItemRegistry", menuName = "FracturedProtocol/Item Registry")]
    public sealed class ItemRegistry : ScriptableObject
    {
        private static ItemRegistry? _instance;

        /// <summary>The active registry. Null until the registry asset is loaded.</summary>
        public static ItemRegistry? Instance => _instance;

        [SerializeField] private List<ItemSO> itemList = new List<ItemSO>();

        [System.NonSerialized] private Dictionary<string, ItemSO> _lookup = new Dictionary<string, ItemSO>();

        // Auto-load the registry before any scene starts so Consume() can resolve ammo IDs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAtStartup()
        {
            Resources.Load<ItemRegistry>("FracturedProtocol/ItemRegistry");
        }

        private void OnEnable()
        {
            _instance = this;
            BuildLookup();
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Rebuild the runtime lookup from the serialized list.</summary>
        public void BuildLookup()
        {
            _lookup = new Dictionary<string, ItemSO>(itemList.Count);
            foreach (ItemSO item in itemList)
            {
                if (item != null && !string.IsNullOrEmpty(item.ItemId))
                    _lookup[item.ItemId] = item;
            }
        }

        /// <summary>Resolve an itemId to its template, or null if not registered.</summary>
        public ItemSO? Get(string itemId)
        {
            _lookup.TryGetValue(itemId, out ItemSO? result);
            return result;
        }

        /// <summary>All registered items.</summary>
        public IReadOnlyList<ItemSO> AllItems => itemList;

#if UNITY_EDITOR
        /// <summary>
        /// Scan the AssetDatabase for all ItemSO assets and rebuild the serialized list.
        /// </summary>
        public void RefreshFromAssets()
        {
            itemList.Clear();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemSO");
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                ItemSO? item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemSO>(path);
                if (item != null) itemList.Add(item);
            }
            BuildLookup();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}

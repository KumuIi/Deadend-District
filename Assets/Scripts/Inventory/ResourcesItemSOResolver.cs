using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loads ItemSO assets from Resources/Items/ and any subfolders (Weapons, Loot, etc.).
/// Builds a name-to-asset cache on first use so subfolders are transparent to the caller.
/// </summary>
public sealed class ResourcesItemSOResolver : IItemSOResolver
{
    private const string ResourcesFolder = "Items";

    private static Dictionary<string, ItemSO> _cache;

    public ItemSO Resolve(string soName)
    {
        if (string.IsNullOrEmpty(soName)) return null;

        if (_cache == null) BuildCache();

        _cache.TryGetValue(soName, out var result);
        return result;
    }

    private static void BuildCache()
    {
        _cache = new Dictionary<string, ItemSO>();
        var all = Resources.LoadAll<ItemSO>(ResourcesFolder);
        foreach (var item in all)
        {
            if (!_cache.ContainsKey(item.name))
                _cache[item.name] = item;
        }
    }
}

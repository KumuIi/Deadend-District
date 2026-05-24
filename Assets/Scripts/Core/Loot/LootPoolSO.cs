using UnityEngine;

/// <summary>
/// Weighted random item pool. Used by LootSpawnSystem, TraderSO stock, chest spawns,
/// enemy drops. Designers build pools in the Inspector — no code changes per content addition.
/// </summary>
[CreateAssetMenu(menuName = "Loot/Loot Pool", fileName = "NewLootPool")]
public class LootPoolSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public ItemSO Item;
        [Range(0f, 1f)] public float Weight;
    }

    public Entry[] Entries;

    /// <summary>Rolls using Unity's global Random. For gameplay use.</summary>
    public ItemSO Roll() => Roll(null);

    /// <summary>
    /// Rolls using a seeded System.Random — pass null to use UnityEngine.Random.
    /// Use the seeded overload in tests and deterministic run generation.
    /// </summary>
    public ItemSO Roll(System.Random rng)
    {
        if (Entries == null || Entries.Length == 0) return null;

        float total = 0f;
        foreach (var e in Entries)
            if (e.Item != null && e.Weight > 0f) total += e.Weight;

        if (total <= 0f) return null;

        float r = rng != null ? (float)rng.NextDouble() * total : Random.value * total;

        foreach (var e in Entries)
        {
            if (e.Item == null || e.Weight <= 0f) continue;
            r -= e.Weight;
            if (r <= 0f) return e.Item;
        }

        // Fallback: last valid entry (floating-point accumulation safety).
        for (int i = Entries.Length - 1; i >= 0; i--)
            if (Entries[i].Item != null && Entries[i].Weight > 0f) return Entries[i].Item;

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Entries == null) return;
        foreach (var e in Entries)
        {
            if (e.Item == null)
                Debug.LogWarning($"[LootPoolSO] '{name}' has an entry with a null Item.", this);
            if (e.Weight < 0f)
                Debug.LogWarning($"[LootPoolSO] '{name}' has an entry with negative weight.", this);
        }
    }
#endif
}

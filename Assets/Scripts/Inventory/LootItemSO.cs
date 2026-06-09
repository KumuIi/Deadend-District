using UnityEngine;

/// <summary>
/// ScriptableObject definition for a plain loot item.
/// Carries no behaviour of its own — it exists purely to be picked up, carried,
/// and sold. All it needs is the base <see cref="ItemSO"/> data:
/// name, weight, grid footprint, sell value, and model prefab.
/// </summary>
[CreateAssetMenu(menuName = "Deadend/Items/Loot")]
public class LootItemSO : ItemSO
{
}

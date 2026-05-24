using System.Collections.Generic;

/// <summary>
/// Anything that holds a collection of items accessible to a container UI.
/// Implementors: InventoryGrid, StashSystem, TraderSystem stock, chest world objects,
///               enemy corpse loot.
/// Trader price data lives on ITraderContainer : ILootContainer — not here.
/// </summary>
public interface ILootContainer
{
    string ContainerName { get; }
    IReadOnlyList<ItemInstance> Items { get; }
    bool CanAddItem(ItemInstance item);
    bool TryAddItem(ItemInstance item);
    bool TryRemoveItem(ItemInstance item);
}

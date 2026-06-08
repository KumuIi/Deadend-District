/// <summary>
/// Small shared helpers for dialogue gating — kept here so DialogueUI and QuestGiver share one copy.
/// </summary>
public static class DialogueUtil
{
    /// <summary>
    /// A null or empty-key condition is treated as 'always true'. We must NOT call Evaluate() on an
    /// empty condition — it returns false for an empty wsmKey, which would wrongly hide content.
    /// </summary>
    public static bool ConditionPassesOrEmpty(QuestConditionDefinition c) =>
        c == null || string.IsNullOrEmpty(c.wsmKey) || c.Evaluate();

    /// <summary>True if item is null (no requirement) or the player's grid currently holds one.</summary>
    public static bool PlayerHasItem(ItemSO item)
    {
        if (item == null) return true;
        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return false;
        foreach (var inst in grid.PlacedItems)
            if (inst != null && inst.data == item) return true;
        return false;
    }
}

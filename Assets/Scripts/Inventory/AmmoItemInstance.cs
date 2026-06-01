using UnityEngine;

/// <summary>
/// Runtime state for an ammo box in the inventory grid.
/// Tracks how many rounds remain in this stack.
/// </summary>
public class AmmoItemInstance : ItemInstance
{
    public int CurrentCount { get; private set; }

    public AmmoItemInstance(AmmunitionSO definition, int count = -1) : base(definition)
    {
        CurrentCount = count < 0 ? definition.stackSize : count;
    }

    public AmmunitionSO AmmoDef => (AmmunitionSO)data;
    public bool IsEmpty => CurrentCount <= 0;

    /// <summary>Maximum rounds this stack can hold.</summary>
    public int StackSize => AmmoDef.stackSize;
    /// <summary>True when the stack is at its cap.</summary>
    public bool IsFull => CurrentCount >= StackSize;

    /// <summary>Sell value of the whole stack = per-round price × count (W3-09).</summary>
    public int StackSellValue => AmmoDef.EffectivePricePerRound * CurrentCount;

    /// <summary>
    /// Provides up to <paramref name="requested"/> rounds, reducing the stack.
    /// Returns the actual number taken.
    /// </summary>
    public int TakeRounds(int requested)
    {
        int taken = Mathf.Min(Mathf.Max(0, requested), CurrentCount);
        CurrentCount -= taken;
        return taken;
    }

    /// <summary>
    /// Adds rounds up to <see cref="StackSize"/>. Returns the overflow that did not fit
    /// (0 if it all stacked) so the caller can place the remainder in a new stack.
    /// </summary>
    public int AddRounds(int amount)
    {
        if (amount <= 0) return 0;
        int space = Mathf.Max(0, StackSize - CurrentCount);
        int added = Mathf.Min(space, amount);
        CurrentCount += added;
        return amount - added;
    }

    /// <summary>
    /// Splits <paramref name="amount"/> rounds off this stack into a brand-new
    /// <see cref="AmmoItemInstance"/> of the same ammo type, leaving at least one round here.
    /// Returns null if the split is invalid (amount ≤ 0 or ≥ current count).
    /// The caller is responsible for placing the returned instance in a grid.
    /// </summary>
    public AmmoItemInstance Split(int amount)
    {
        if (amount <= 0 || amount >= CurrentCount) return null;
        CurrentCount -= amount;
        return new AmmoItemInstance(AmmoDef, amount);
    }

    /// <summary>Save/load: restores the exact stack count (clamped to [0, stackSize]).</summary>
    public void RestoreCount(int count) => CurrentCount = Mathf.Clamp(count, 0, StackSize);
}

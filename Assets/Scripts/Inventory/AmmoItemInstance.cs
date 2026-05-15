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

    /// <summary>
    /// Provides up to <paramref name="requested"/> rounds, reducing the stack.
    /// Returns the actual number taken.
    /// </summary>
    public int TakeRounds(int requested)
    {
        int taken = Mathf.Min(requested, CurrentCount);
        CurrentCount -= taken;
        return taken;
    }
}

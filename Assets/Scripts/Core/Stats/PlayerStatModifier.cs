using System.Collections.Generic;

public enum StatType
{
    Speed,
    SprintSpeed,
    NoiseMultiplier,
    StaminaDrain,
    EnergyRegen,
    EnergyDrain,
    VisibilityScore,
    CarryCapacity,
    HazardResist,
    BatteryEfficiency,
}

/// <summary>
/// A single stat modifier pushed by one system (encumbrance, augment, hazard zone, etc.).
/// Remove by Id — never by value. Two systems may push the same StatType with different Ids.
/// IsMultiplier=true: multiplied into the stack product. false: added as flat bonus after.
/// </summary>
public struct PlayerStatModifier
{
    /// <summary>Unique per applier, e.g. "encumbrance.heavy", "augment.exoskeleton".</summary>
    public string   Id;
    public StatType Stat;
    /// <summary>Multiplier: 0.8 = 20% reduction. Flat bonus: raw value added after all multipliers.</summary>
    public float    Value;
    public bool     IsMultiplier;
}

/// <summary>
/// Plain C# class — lives as a field on PlayerMotor and PlayerHealth.
/// Net(stat) = (product of all multipliers) + (sum of all flat bonuses).
/// With an empty stack, Net returns 1.0.
/// </summary>
public class StatModifierStack
{
    private readonly List<PlayerStatModifier> _modifiers = new List<PlayerStatModifier>();

    public void Add(PlayerStatModifier modifier)
    {
        _modifiers.Add(modifier);
    }

    public void Remove(string id)
    {
        _modifiers.RemoveAll(m => m.Id == id);
    }

    public bool Has(string id)
    {
        foreach (var m in _modifiers)
            if (m.Id == id) return true;
        return false;
    }

    /// <summary>
    /// Returns the net value for stat: (product of all multipliers) + (sum of all flat bonuses).
    /// Flat bonuses are additive on top of the multiplied base of 1.0.
    /// </summary>
    public float Net(StatType stat)
    {
        float product  = 1f;
        float flatSum  = 0f;

        foreach (var m in _modifiers)
        {
            if (m.Stat != stat) continue;
            if (m.IsMultiplier) product *= m.Value;
            else                flatSum  += m.Value;
        }

        return product + flatSum;
    }
}

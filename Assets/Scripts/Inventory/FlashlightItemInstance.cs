using UnityEngine;

/// <summary>
/// Runtime state for a flashlight in the inventory.
/// Mirrors the WeaponItemInstance / MagazineItemInstance pattern exactly:
/// InsertedBattery is owned here (removed from inventory grid), ejectable back.
/// CurrentCharge is the live drain value — drained by FlashlightSlot.Update().
/// </summary>
public class FlashlightItemInstance : ItemInstance
{
    public FlashlightSO FlashlightDef => (FlashlightSO)data;

    public float CurrentCharge    { get; set; }
    public float MaxCharge        => FlashlightDef.maxCharge;
    public float ChargeNormalized => MaxCharge > 0f ? CurrentCharge / MaxCharge : 0f;
    public bool  IsDepleted       => CurrentCharge <= 0f;

    /// <summary>The battery currently loaded — removed from inventory grid on load, returned on eject.</summary>
    public BatteryItemInstance InsertedBattery { get; private set; }

    public FlashlightItemInstance(FlashlightSO definition) : base(definition)
    {
        CurrentCharge = definition.maxCharge;
    }

    /// <summary>
    /// Loads a battery into the flashlight. Caller must remove the battery from the inventory grid first.
    /// If a battery is already loaded, it is ejected (caller must handle the returned instance).
    /// </summary>
    public BatteryItemInstance LoadBattery(BatteryItemInstance battery)
    {
        BatteryItemInstance ejected = EjectBattery(); // eject current if any
        InsertedBattery = battery;
        CurrentCharge   = Mathf.Min(MaxCharge, battery.CurrentCharge);
        return ejected; // null if nothing was loaded before
    }

    /// <summary>
    /// Ejects the inserted battery with remaining charge synced back to it.
    /// Returns null if no battery is loaded.
    /// </summary>
    public BatteryItemInstance EjectBattery()
    {
        if (InsertedBattery == null) return null;
        InsertedBattery.CurrentCharge = CurrentCharge;
        var b      = InsertedBattery;
        InsertedBattery = null;
        CurrentCharge   = 0f;
        return b;
    }
}

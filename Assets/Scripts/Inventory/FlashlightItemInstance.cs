/// <summary>
/// Runtime state for a flashlight in the inventory.
/// CurrentCharge drains while equipped and the light is on — same pattern as BulletCount on MagazineItemInstance.
/// </summary>
public class FlashlightItemInstance : ItemInstance
{
    public FlashlightSO FlashlightDef => (FlashlightSO)data;

    public float CurrentCharge    { get; set; }
    public float MaxCharge        => FlashlightDef.maxCharge;
    public float ChargeNormalized => MaxCharge > 0f ? CurrentCharge / MaxCharge : 0f;
    public bool  IsDepleted       => CurrentCharge <= 0f;

    public FlashlightItemInstance(FlashlightSO definition) : base(definition)
    {
        CurrentCharge = definition.maxCharge;
    }

    /// <summary>Transfers charge from a physical battery into this flashlight, consuming the battery.</summary>
    public void SwapWith(BatteryItemInstance battery)
    {
        float toTransfer  = UnityEngine.Mathf.Min(MaxCharge - CurrentCharge, battery.CurrentCharge);
        CurrentCharge    += toTransfer;
        battery.CurrentCharge -= toTransfer;
    }
}

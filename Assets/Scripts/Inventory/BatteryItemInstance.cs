/// <summary>
/// Runtime state for a battery in the inventory.
/// CurrentCharge is mutable — BatterySystem drains it directly.
/// </summary>
public class BatteryItemInstance : ItemInstance
{
    public float CurrentCharge { get; set; }

    public BatteryType BatteryType    => ((BatteryItemSO)data).batteryType;
    public float       MaxCharge      => ((BatteryItemSO)data).maxCharge;
    public float       ChargeNormalized => MaxCharge > 0f ? CurrentCharge / MaxCharge : 0f;

    public BatteryItemInstance(BatteryItemSO definition) : base(definition)
    {
        CurrentCharge = definition.maxCharge;
    }
}

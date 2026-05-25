using UnityEngine;

public enum BatteryType { Rechargeable, OneTime }

/// <summary>
/// ScriptableObject definition for a battery item.
/// Rechargeable: refilled at RechargeStation (Wave 2). OneTime: discarded when empty.
/// </summary>
[CreateAssetMenu(menuName = "Deadend/Items/Battery")]
public class BatteryItemSO : ItemSO
{
    public BatteryType batteryType = BatteryType.Rechargeable;
    [Min(1f)] public float maxCharge = 100f;
}

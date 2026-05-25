/// <summary>
/// Runtime state for a flashlight in the inventory.
/// Light mode state (on/off/dim) lives on the spawned LightSource component, not here —
/// this instance only carries inventory identity and position.
///
/// Wave 5: CyberneticSO augments may add IBatteryDrainer modifiers via this instance.
/// </summary>
public class FlashlightItemInstance : ItemInstance
{
    public FlashlightSO FlashlightDef => (FlashlightSO)data;

    public FlashlightItemInstance(FlashlightSO definition) : base(definition) { }
}

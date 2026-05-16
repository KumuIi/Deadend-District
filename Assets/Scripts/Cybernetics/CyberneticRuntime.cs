/// <summary>
/// Per-equipped-instance mutable runtime for a cybernetic.
/// Created by CyberneticSO.CreateRuntime() — holds subscriptions, cooldowns,
/// and any state that must NOT live on the shared SO asset.
/// </summary>
public abstract class CyberneticRuntime
{
    protected readonly CyberneticController Owner;
    protected CyberneticRuntime(CyberneticController owner) { Owner = owner; }

    public abstract void Equip();
    public abstract void Unequip();
    public virtual  void UseAbility() { }
}

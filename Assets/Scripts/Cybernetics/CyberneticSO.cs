using UnityEngine;

/// <summary>
/// Immutable definition for a cybernetic enhancement.
/// Concrete subclasses implement CreateRuntime() to supply type-specific behavior.
/// The runtime owns all mutable per-equip state.
/// </summary>
public abstract class CyberneticSO : ScriptableObject
{
    public string cyberneticId;
    public string displayName;
    [TextArea] public string description;

    public abstract CyberneticRuntime CreateRuntime(CyberneticController owner);
}

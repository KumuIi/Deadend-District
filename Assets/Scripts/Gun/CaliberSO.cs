using UnityEngine;

/// <summary>
/// Shared caliber identity asset. Assign the same CaliberSO to a WeaponSO,
/// MagazineSO, and AmmunitionSO to make them compatible.
/// Reference-equality replaces fragile string comparisons everywhere.
/// </summary>
[CreateAssetMenu(fileName = "NewCaliber", menuName = "Deadend District/Caliber")]
public class CaliberSO : ScriptableObject
{
    [Tooltip("Human-readable label, e.g. '9x19 Parabellum'. Never used for logic.")]
    public string displayName = "9x19 Parabellum";
}

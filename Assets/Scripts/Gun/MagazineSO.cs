using UnityEngine;

/// <summary>
/// Defines a magazine type: caliber and capacity.
/// Runtime ammo state lives in MagazineInstance, not here.
/// </summary>
[CreateAssetMenu(fileName = "NewMagazine", menuName = "Deadend District/Magazine")]
public class MagazineSO : ItemSO
{
    [Header("=== Magazine ===")]
    [Tooltip("Must reference the same CaliberSO as the weapon and ammo.")]
    public CaliberSO caliber;
    public int capacity = 8;
}

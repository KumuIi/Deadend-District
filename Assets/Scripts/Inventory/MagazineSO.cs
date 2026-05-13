using UnityEngine;

[CreateAssetMenu(fileName = "NewMagazine", menuName = "Deadend District/Magazine")]
public class MagazineSO : ItemSO
{
    [Header("=== Magazine ===")]
    [Tooltip("Must match the weapon's caliber string for compatibility checks")]
    public string caliber  = "9x19";
    public int    capacity = 8;
}

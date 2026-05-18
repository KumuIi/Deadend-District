using UnityEngine;

/// <summary>
/// Weapon category defaults. Assign to WeaponSO._preset in the Inspector,
/// then right-click the WeaponSO and choose "Apply Preset" to deep-copy
/// all values. The WeaponSO becomes self-contained after applying — no runtime
/// dependency on this asset.
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponPreset", menuName = "Deadend District/Weapon Preset")]
public class WeaponPresetSO : ScriptableObject
{
    public string presetName = "New Preset";
    [TextArea(2, 4)]
    public string description;

    public WeaponRecoilData recoil = new WeaponRecoilData();
    public WeaponFeelData   feel   = new WeaponFeelData();
}

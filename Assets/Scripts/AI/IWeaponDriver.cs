using UnityEngine;

public interface IWeaponDriver
{
    bool CanFire     { get; }
    bool NeedsReload { get; }
    int  CurrentAmmo { get; }

    void Initialize(WeaponSO weapon, GameObject owner, Transform muzzle);
    void SetAimTarget(Transform target);
    void ClearAim();
    void FireAt(Vector3 targetPoint, float accuracy);
    void Reload();
    void DetachAndDrop();
}

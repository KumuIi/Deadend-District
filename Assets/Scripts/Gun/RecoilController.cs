using UnityEngine;

/// <summary>
/// Attach to a RecoilNode GameObject placed between CameraRig and [Camera]:
///   CameraRig > RecoilNode (this) > [Camera] > FPSRig > Arms / Guns
///
/// Rotating this node rotates the camera frustum + entire FPSRig as one unit,
/// giving true view recoil without touching CameraController or GunSway.
///
/// GunController reaches this via GetComponentInParent and calls:
///   SetWeaponData()  — on weapon equip
///   AddRecoil()      — on each shot
/// </summary>
public class RecoilController : MonoBehaviour
{
    private WeaponRecoilData _data;
    private Vector3 _targetRecoil;
    private Vector3 _currentRecoil;

    /// <summary>
    /// Swaps active weapon data and resets all recoil state so weapon
    /// switches never inherit leftover kick from the previous gun.
    /// </summary>
    public void SetWeaponData(WeaponRecoilData data)
    {
        _data          = data;
        _targetRecoil  = Vector3.zero;
        _currentRecoil = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    /// <summary>Called by GunController.FireShot() on each round fired.</summary>
    public void AddRecoil(bool isAiming)
    {
        if (_data == null) return;

        float up    = isAiming ? _data.adsKickUp    : _data.kickUp;
        float horiz = isAiming ? _data.adsKickHoriz : _data.kickHoriz;
        float roll  = isAiming ? _data.adsKickRoll  : _data.kickRoll;

        // X kicks upward (negative pitch), Y and Z are random per shot.
        _targetRecoil.x -= up;
        _targetRecoil.y += Random.Range(-horiz, horiz);
        _targetRecoil.z += Random.Range(-roll,  roll);

        _targetRecoil.x = Mathf.Clamp(_targetRecoil.x, -_data.maxVertical, 0f);
        _targetRecoil.y = Mathf.Clamp(_targetRecoil.y, -_data.maxHoriz,    _data.maxHoriz);
        _targetRecoil.z = Mathf.Clamp(_targetRecoil.z, -_data.maxRoll,     _data.maxRoll);
    }

    private void Update()
    {
        if (_data == null) return;

        float dt = Time.deltaTime;

        // Target decays toward zero — controls how fast the aim settles after firing.
        _targetRecoil = Vector3.Lerp(_targetRecoil, Vector3.zero, _data.targetDecaySpeed * dt);

        // Current chases target — the lag between them is what creates the snappy spring feel.
        _currentRecoil = Vector3.Lerp(_currentRecoil, _targetRecoil, _data.currentFollowSpeed * dt);

        transform.localRotation = Quaternion.Euler(_currentRecoil);
    }
}

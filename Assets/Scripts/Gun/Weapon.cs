using UnityEngine;

/// <summary>
/// Root component on every gun prefab. Holds the one shared gun-internal ref
/// (gunPivot) and is the single injection point WeaponManager calls.
///
/// Setup per gun prefab (Inspector):
///   - gunPivot: the GunPivot empty child
///   All other fields (aimSocket, bones, muzzle, etc.) live on GunController.
///
/// Player-level refs (cam, motor, cameraController) come from WeaponManager
/// via Initialize(), which must be called before the object is enabled.
/// </summary>
[RequireComponent(typeof(GunController))]
[RequireComponent(typeof(GunSway))]
public class Weapon : MonoBehaviour
{
    [Header("=== Gun Setup ===")]
    [Tooltip("The GunPivot empty child — shared by GunController and GunSway")]
    public Transform gunPivot;

    private GunController _controller;
    private GunSway       _sway;

    void Awake()
    {
        _controller = GetComponent<GunController>();
        _sway       = GetComponent<GunSway>();
    }

    /// <summary>
    /// Called by WeaponManager while the object is still disabled.
    /// Pushes all player-level refs into the child scripts.
    /// </summary>
    public void Initialize(WeaponManager mgr)
    {
        _controller.Initialize(gunPivot, mgr.PlayerCam);
        _sway.Initialize(gunPivot, mgr.PlayerMotor, mgr.PlayerCam, mgr.CameraController, _controller);
    }
}

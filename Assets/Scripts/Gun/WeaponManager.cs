using UnityEngine;

/// <summary>
/// Holds all player-level references and manages equip/unequip.
/// Guns are pre-placed in the scene hierarchy (disabled). Awake() initializes
/// them all while they're still inactive, so Start() on each gun always runs
/// with valid refs when it's first enabled.
/// </summary>
public class WeaponManager : MonoBehaviour
{
    [Header("=== Player References ===")]
    public Transform        playerCam;
    public CameraController cameraController;
    public PlayerMotor      playerMotor;

    [Header("=== Weapons ===")]
    [Tooltip("Drag in the gun GameObjects already placed under the player — all start disabled")]
    public GunController[] weapons;

    public Transform        PlayerCam        => playerCam;
    public CameraController CameraController => cameraController;
    public PlayerMotor      PlayerMotor      => playerMotor;

    public GunController CurrentWeapon { get; private set; }

    void Awake()
    {
        foreach (GunController gun in weapons)
        {
            if (gun == null) { Debug.LogWarning("WeaponManager: null entry in weapons[]", this); continue; }
            gun.gameObject.SetActive(false);
            gun.Initialize(this);
        }
    }

    void Start()
    {
        if (weapons.Length > 0)
            Equip(0);
    }

    public void Equip(int index)
    {
        if (index < 0 || index >= weapons.Length) return;

        if (CurrentWeapon != null)
            CurrentWeapon.gameObject.SetActive(false);

        CurrentWeapon = weapons[index];
        CurrentWeapon.gameObject.SetActive(true);
    }

    public void Holster()
    {
        if (CurrentWeapon == null) return;
        CurrentWeapon.gameObject.SetActive(false);
        CurrentWeapon = null;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds all player-level references and manages equip/unequip.
///
/// Starter weapons are assigned in _initialWeapons (Inspector).
/// Runtime pickup / drop uses AddWeapon() / RemoveWeapon().
///
/// Awake() initialises all weapons while they are still inactive so
/// every gun's Start() always runs with valid refs when first enabled.
/// </summary>
public class WeaponManager : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("=== Player References ===")]
    public Transform playerCam;
    public CameraController cameraController;
    public PlayerMotor playerMotor;

    [Header("=== Starting Weapons ===")]
    [Tooltip("Drag in gun GameObjects already placed under the player. All start disabled.")]
    [SerializeField] private GunController[] _initialWeapons;

    [Header("=== IK Constraint Targets ===")]
    [Tooltip("The Transform the right-hand IK constraint uses as its target.")]
    public Transform rightHandIKTarget;
    [Tooltip("The Transform the left-hand IK constraint uses as its target.")]
    public Transform leftHandIKTarget;

    // ── Public accessors ───────────────────────────────────────────────────

    public Transform PlayerCam           => playerCam;
    public CameraController CameraController => cameraController;
    public PlayerMotor PlayerMotor       => playerMotor;

    /// <summary>The currently equipped weapon, or null if holstered.</summary>
    public GunController CurrentWeapon { get; private set; }

    /// <summary>Read-only view of all registered weapons (including runtime pickups).</summary>
    public IReadOnlyList<GunController> Weapons => _weapons;

    // ── Private ────────────────────────────────────────────────────────────

    private readonly List<GunController> _weapons = new List<GunController>();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_initialWeapons != null)
        {
            foreach (GunController gun in _initialWeapons)
                RegisterWeapon(gun);
        }
    }

    private void Start()
    {
        if (_weapons.Count > 0)
            Equip(0);
    }

    // ── Weapon registration ────────────────────────────────────────────────

    private void RegisterWeapon(GunController gun)
    {
        if (gun == null)
        {
            Debug.LogWarning("WeaponManager: null entry in weapon list.", this);
            return;
        }
        gun.gameObject.SetActive(false);
        gun.Initialize(this);
        _weapons.Add(gun);
    }

    /// <summary>Adds a weapon at runtime (e.g. picked up from the ground).</summary>
    public void AddWeapon(GunController gun)
    {
        if (_weapons.Contains(gun)) return;
        RegisterWeapon(gun);
    }

    /// <summary>Removes a weapon at runtime (e.g. dropped or discarded).</summary>
    public void RemoveWeapon(GunController gun)
    {
        if (CurrentWeapon == gun) Holster();
        _weapons.Remove(gun);
    }

    // ── Equip / holster ────────────────────────────────────────────────────

    /// <summary>Equips the weapon at the given slot index.</summary>
    public void Equip(int index)
    {
        if (index < 0 || index >= _weapons.Count) return;

        if (CurrentWeapon != null)
            CurrentWeapon.gameObject.SetActive(false);

        CurrentWeapon = _weapons[index];
        CurrentWeapon.gameObject.SetActive(true);

        ApplyIKTargets(CurrentWeapon);
    }

    /// <summary>Disables the current weapon without equipping another.</summary>
    public void Holster()
    {
        if (CurrentWeapon == null) return;
        CurrentWeapon.gameObject.SetActive(false);
        CurrentWeapon = null;
    }

    // ── IK ─────────────────────────────────────────────────────────────────

    private void ApplyIKTargets(GunController gun)
    {
        if (rightHandIKTarget && gun.rightHandGrip)
        {
            rightHandIKTarget.SetParent(gun.rightHandGrip, false);
            rightHandIKTarget.localPosition = Vector3.zero;
            rightHandIKTarget.localRotation = Quaternion.identity;
        }

        if (leftHandIKTarget && gun.leftHandGrip)
        {
            leftHandIKTarget.SetParent(gun.leftHandGrip, false);
            leftHandIKTarget.localPosition = Vector3.zero;
            leftHandIKTarget.localRotation = Quaternion.identity;
        }
    }
}

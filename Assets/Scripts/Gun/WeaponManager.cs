using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

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

    [Header("=== Empty Hands ===")]
    [Tooltip("A hands-only GunController active when nothing is equipped. Assign its WeaponSO with only feel/IK data filled in — no caliber or magazine needed.")]
    public GunController emptyHandsGun;

    [Header("=== IK Constraint Targets ===")]
    [Tooltip("The Transform the right-hand IK constraint uses as its target.")]
    public Transform rightHandIKTarget;
    [Tooltip("The Transform the left-hand IK constraint uses as its target.")]
    public Transform leftHandIKTarget;

    [Header("=== Animation Rigging ===")]
    [Tooltip("Assign the RigBuilder on the player's Animator. It is rebuilt after every weapon switch so IK targets take effect.")]
    public RigBuilder rigBuilder;
    [Tooltip("TwoBoneIKConstraint for the right arm. When assigned, ApplyIKTargets sets its Target directly to the gun's rightHandGrip.")]
    public TwoBoneIKConstraint rightArmConstraint;
    [Tooltip("TwoBoneIKConstraint for the left arm. When assigned, ApplyIKTargets sets its Target directly to the gun's leftHandGrip.")]
    public TwoBoneIKConstraint leftArmConstraint;

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
        // Detach IK targets from any gun grip they may still be parented to
        // from a previous play session (ApplyIKTargets ran and was saved into the scene).
        if (rightHandIKTarget) rightHandIKTarget.SetParent(transform, false);
        if (leftHandIKTarget)  leftHandIKTarget.SetParent(transform, false);

        // Initialise the empty-hands gun (not a regular slot weapon).
        if (emptyHandsGun != null)
        {
            emptyHandsGun.gameObject.SetActive(false);
            emptyHandsGun.Initialize(this);
            emptyHandsGun.inventoryManaged = true; // no auto-reload, no free-mag
        }

        // Build a set of guns that will be properly registered below.
        var initialSet = new HashSet<GunController>();
        if (_initialWeapons != null)
            foreach (var g in _initialWeapons)
                if (g != null) initialSet.Add(g);

        // Deactivate every scene GunController that is NOT in _initialWeapons.
        // emptyHandsGun is excluded — it was already handled above.
        foreach (var gun in FindObjectsOfType<GunController>(true))
            if (!initialSet.Contains(gun) && gun != emptyHandsGun)
                gun.gameObject.SetActive(false);

        if (_initialWeapons != null)
            foreach (GunController gun in _initialWeapons)
                RegisterWeapon(gun);
    }

    private void Start()
    {
        if (_weapons.Count > 0) Equip(0);
        else                    EquipNothing();
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

    /// <summary>Switches to the empty-hands gun (or holsters if none is assigned).</summary>
    public void EquipNothing()
    {
        if (CurrentWeapon != null)
        {
            CurrentWeapon.gameObject.SetActive(false);
            CurrentWeapon = null;
        }

        if (emptyHandsGun == null) return;

        CurrentWeapon = emptyHandsGun;
        CurrentWeapon.gameObject.SetActive(true);
        ApplyIKTargets(CurrentWeapon);
    }

    // ── IK ─────────────────────────────────────────────────────────────────

    private void ApplyIKTargets(GunController gun)
    {
        // Preferred path: set constraint Target directly so the grip transform IS the target.
        if (rightArmConstraint != null && gun.rightHandGrip)
        {
            var d = rightArmConstraint.data;
            d.target = gun.rightHandGrip;
            rightArmConstraint.data = d;
        }
        else if (rightHandIKTarget && gun.rightHandGrip)
        {
            rightHandIKTarget.SetParent(gun.rightHandGrip, false);
            rightHandIKTarget.localPosition = Vector3.zero;
            rightHandIKTarget.localRotation = Quaternion.identity;
        }

        if (leftArmConstraint != null && gun.leftHandGrip)
        {
            var d = leftArmConstraint.data;
            d.target = gun.leftHandGrip;
            leftArmConstraint.data = d;
        }
        else if (leftHandIKTarget && gun.leftHandGrip)
        {
            leftHandIKTarget.SetParent(gun.leftHandGrip, false);
            leftHandIKTarget.localPosition = Vector3.zero;
            leftHandIKTarget.localRotation = Quaternion.identity;
        }

        // Animation Rigging caches constraint data at build time.
        // Must rebuild after changing IK target parents or constraint data at runtime.
        rigBuilder?.Build();
    }
}

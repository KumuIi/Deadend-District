using UnityEngine;

/// <summary>
/// Equipment slot for a hand-held flashlight.
/// Spawns the flashlight prefab on equip and stows/shows it as the active weapon changes.
///
/// Scene setup:
///   1. Add this component to the Player GameObject (same as WeaponManager).
///   2. Assign WeaponManager in the Inspector.
///   3. From the inventory UI call EquipmentController.Instance.EquipToSlot("flashlight", instance).
///
/// Dual-wield rule: the flashlight is visible only when WeaponSO.allowsOffHandItem is true
/// on the currently equipped weapon. When stowed the prefab is disabled (LightSource state preserved).
/// </summary>
public class FlashlightSlot : MonoBehaviour, IEquipmentSlot
{
    [SerializeField] private WeaponManager _weaponManager;

    public string       SlotId       => "flashlight";
    public ItemInstance EquippedItem => _equipped;
    public LightSource  LightSource  => _view?.lightSource;

    private FlashlightItemInstance _equipped;
    private GameObject             _spawnedPrefab;
    private FlashlightView         _view;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        EquipmentController.Instance?.RegisterSlot(this);
        if (_weaponManager != null)
            _weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
    }

    private void OnDisable()
    {
        EquipmentController.Instance?.UnregisterSlot(SlotId);
        if (_weaponManager != null)
            _weaponManager.OnWeaponEquipped -= HandleWeaponEquipped;
    }

    // ── IEquipmentSlot ─────────────────────────────────────────────────────

    public bool TryEquip(ItemInstance item)
    {
        if (item is not FlashlightItemInstance fi) return false;
        Unequip();

        _equipped      = fi;
        _spawnedPrefab = Instantiate(fi.FlashlightDef.flashlightPrefab, _weaponManager.transform);
        _view          = _spawnedPrefab.GetComponentInChildren<FlashlightView>();

        // Show/hide based on current weapon, then override IK and rebuild.
        bool allowed = CurrentWeaponAllowsOffHand();
        _spawnedPrefab.SetActive(allowed);
        if (allowed)
        {
            OverrideLeftIK(_view?.gripTarget);
            _weaponManager.rigBuilder?.Build();
        }
        return true;
    }

    public void Unequip()
    {
        if (_spawnedPrefab != null)
        {
            Destroy(_spawnedPrefab);
            _spawnedPrefab = null;
            _view          = null;
        }
        _equipped = null;
        RestoreWeaponLeftIK();
    }

    // ── Weapon switch callback ─────────────────────────────────────────────

    // Called by WeaponManager BEFORE rigBuilder.Build() — safe to mutate constraint data.
    private void HandleWeaponEquipped(GunController gun)
    {
        if (_equipped == null || _spawnedPrefab == null) return;

        bool allowed = gun.weaponData != null && gun.weaponData.allowsOffHandItem;
        _spawnedPrefab.SetActive(allowed);

        if (allowed)
            OverrideLeftIK(_view?.gripTarget);
        // If not allowed, WeaponManager already restored left IK to gun.leftHandGrip.
    }

    // ── IK helpers ─────────────────────────────────────────────────────────

    private void OverrideLeftIK(Transform target)
    {
        if (_weaponManager.leftArmConstraint == null || target == null) return;
        var d    = _weaponManager.leftArmConstraint.data;
        d.target = target;
        _weaponManager.leftArmConstraint.data = d;
    }

    private void RestoreWeaponLeftIK()
    {
        if (_weaponManager == null) return;
        var gun = _weaponManager.CurrentWeapon;
        if (gun == null || _weaponManager.leftArmConstraint == null) return;

        var d    = _weaponManager.leftArmConstraint.data;
        d.target = gun.leftHandGrip;
        _weaponManager.leftArmConstraint.data = d;
        _weaponManager.rigBuilder?.Build();
    }

    private bool CurrentWeaponAllowsOffHand()
    {
        var gun = _weaponManager?.CurrentWeapon;
        return gun != null && gun.weaponData != null && gun.weaponData.allowsOffHandItem;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_weaponManager == null)
            Debug.LogWarning("[FlashlightSlot] WeaponManager is not assigned.", this);
    }
#endif
}

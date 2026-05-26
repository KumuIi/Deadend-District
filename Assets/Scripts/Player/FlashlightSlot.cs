using System;
using UnityEngine;

/// <summary>
/// Equipment slot for a hand-held flashlight — mirrors the WeaponManager pattern.
/// Pre-placed flashlight GO in the scene is shown/hidden on equip/unequip.
///
/// Scene setup:
///   1. Add this component to the Player GameObject (same as WeaponManager).
///   2. Assign WeaponManager, FlashlightView, and FlashlightGO in the Inspector.
///   3. Call TryEquip(instance) from InventoryUI on right-click Equip.
///
/// Dual-wield rule: flashlight is visible only when WeaponSO.allowsOffHandItem is true
/// on the currently equipped weapon, OR when no weapon is equipped.
/// </summary>
public class FlashlightSlot : MonoBehaviour, IEquipmentSlot
{
    [SerializeField] private WeaponManager  _weaponManager;
    [SerializeField] private FlashlightView _flashlightView;
    [Tooltip("The root GameObject of the pre-placed flashlight model in the scene.")]
    [SerializeField] private GameObject     _flashlightGO;

    public string                 SlotId            => "flashlight";
    public ItemInstance           EquippedItem      => _equipped;
    public FlashlightItemInstance EquippedFlashlight => _equipped;
    public LightSource            LightSource        => _flashlightView?.lightSource;

    public float ChargeNormalized => _equipped != null ? _equipped.ChargeNormalized : 0f;
    public bool  IsDepleted       => _equipped == null || _equipped.IsDepleted;

    // ── Events (mirrors BatterySystem API — observers subscribe here) ───────
    public event Action<float> OnChargeChanged;
    public event Action        OnDepleted;
    public event Action        OnRestored;

    private FlashlightItemInstance _equipped;
    private bool                   _wasDepleted;
    private float                  _lastReportedNormalized = 1f;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_weaponManager != null)
            _weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
    }

    private void OnDisable()
    {
        if (_weaponManager != null)
            _weaponManager.OnWeaponEquipped -= HandleWeaponEquipped;
    }

    private void Update()
    {
        if (_equipped == null || _flashlightView == null) return;

        var light = LightSource;
        if (light == null) return;

        if (!GameInputState.GameplayBlocked && Input.GetKeyDown(KeyCode.T))
        {
            // Only allow turning ON if there is charge; turning OFF is always permitted.
            if (!IsDepleted || light.IsOn)
                light.Toggle();
        }

        if (!light.IsOn) return;

        float drain = light.DrainRate * Time.deltaTime;
        if (drain <= 0f) return;

        _equipped.CurrentCharge = Mathf.Max(0f, _equipped.CurrentCharge - drain);

        float norm = _equipped.ChargeNormalized;
        if (Mathf.Abs(norm - _lastReportedNormalized) > 0.001f)
        {
            _lastReportedNormalized = norm;
            OnChargeChanged?.Invoke(norm);
        }

        bool depleted = _equipped.IsDepleted;
        if (depleted && !_wasDepleted)
        {
            _wasDepleted = true;
            light.ForceOff();
            OnDepleted?.Invoke();
        }
    }

    // ── IEquipmentSlot ─────────────────────────────────────────────────────

    public bool TryEquip(ItemInstance item)
    {
        if (item is not FlashlightItemInstance fi) return false;

        Unequip();

        _equipped               = fi;
        _wasDepleted            = fi.IsDepleted;
        _lastReportedNormalized = fi.ChargeNormalized;

        // If already depleted keep light off
        if (_wasDepleted)
            LightSource?.ForceOff();

        bool allowed = CurrentWeaponAllowsOffHand();
        if (_flashlightGO != null) _flashlightGO.SetActive(allowed);

        if (allowed)
        {
            OverrideLeftIK(_flashlightView?.gripTarget);
            _weaponManager?.rigBuilder?.Build();
        }

        // Sync all observers to the new item's state immediately
        OnChargeChanged?.Invoke(fi.ChargeNormalized);
        if (_wasDepleted)  OnDepleted?.Invoke();
        else               OnRestored?.Invoke();

        return true;
    }

    public void Unequip()
    {
        if (_flashlightGO != null) _flashlightGO.SetActive(false);
        LightSource?.ForceOff();
        _equipped    = null;
        _wasDepleted = false;
        RestoreWeaponLeftIK();
        _lastReportedNormalized = 0f;
        OnChargeChanged?.Invoke(0f); // clear HUD
        OnDepleted?.Invoke();        // player has no light source
    }

    /// <summary>
    /// Called by InventoryUI after a battery is loaded into or ejected from the flashlight.
    /// Syncs HUD and depletion events to the new charge state.
    /// </summary>
    public void OnBatteryLoaded(FlashlightItemInstance flashlight)
    {
        if (flashlight != _equipped) return;

        bool wasDepleted = _wasDepleted;
        _wasDepleted            = _equipped.IsDepleted;
        _lastReportedNormalized = _equipped.ChargeNormalized;

        if (_wasDepleted) LightSource?.ForceOff();

        OnChargeChanged?.Invoke(_equipped.ChargeNormalized);

        if (wasDepleted && !_wasDepleted) OnRestored?.Invoke();
        if (!wasDepleted && _wasDepleted) OnDepleted?.Invoke();
    }

    // ── Weapon switch callback ─────────────────────────────────────────────

    private void HandleWeaponEquipped(GunController gun)
    {
        if (_equipped == null || _flashlightGO == null) return;

        bool allowed = gun.weaponData != null && gun.weaponData.allowsOffHandItem;
        _flashlightGO.SetActive(allowed);

        if (allowed)
            OverrideLeftIK(_flashlightView?.gripTarget);
    }

    // ── IK helpers ─────────────────────────────────────────────────────────

    private void OverrideLeftIK(Transform target)
    {
        if (_weaponManager == null || _weaponManager.leftArmConstraint == null || target == null) return;
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
        if (gun == null) return true; // standalone — no weapon equipped
        return gun.weaponData != null && gun.weaponData.allowsOffHandItem;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_weaponManager == null)
            Debug.LogWarning("[FlashlightSlot] WeaponManager is not assigned.", this);
        if (_flashlightView == null)
            Debug.LogWarning("[FlashlightSlot] FlashlightView is not assigned.", this);
        if (_flashlightGO == null)
            Debug.LogWarning("[FlashlightSlot] FlashlightGO is not assigned.", this);
    }
#endif
}

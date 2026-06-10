using System;
using UnityEngine;

/// <summary>
/// Equipment slot for a hand-held flashlight — mirrors the WeaponManager pattern.
/// Pre-placed flashlight GO in the scene is shown/hidden on equip/unequip.
///
/// Scene setup:
///   1. Add this component to the Player GameObject (same as WeaponManager).
///   2. Assign WeaponManager, FlashlightView, FlashlightGO, and BeamPivot in the Inspector.
///   3. Call TryEquip(instance) from InventoryUI on right-click Equip.
///
/// Dual-wield rule: flashlight is visible only when WeaponSO.allowsOffHandItem is true
/// on the currently equipped weapon, OR when no weapon is equipped.
/// </summary>
public class FlashlightSlot : MonoBehaviour, IEquipmentSlot, IRunLifecycleListener
{
    [SerializeField] private WeaponManager  _weaponManager;
    [SerializeField] private FlashlightView _flashlightView;
    [Tooltip("The root GameObject of the pre-placed flashlight model in the scene.")]
    [SerializeField] private GameObject     _flashlightGO;
    [Tooltip("FlashlightSway on the flashlight GO — receives the reload dip offset each frame.")]
    [SerializeField] private FlashlightSway _flashlightSway;
    [Tooltip("Parent pivot Transform above the flashlight root. Rotated toward the inventory when open.")]
    [SerializeField] private Transform      _beamPivot;
    [Tooltip("How far toward the inventory target the beam pivots. 0 = no redirect, 1 = full aim.")]
    [SerializeField][Range(0f, 1f)] private float _inventoryAimStrength = 0.7f;
    [Tooltip("Degrees per second the pivot rotates toward (and back from) the inventory target.")]
    [SerializeField] private float          _inventoryAimSpeed    = 120f;
    [Tooltip("Extra degrees added on top of the LookAt aim. Y = swing beam right (+) or left (-). X = tilt up (-) or down (+).")]
    [SerializeField] private Vector3        _inventoryAimExtra    = Vector3.zero;
    [Tooltip("Drag in the ReloadDip component from every gun prefab that can have an off-hand flashlight.")]
    [SerializeField] private ReloadDip[]    _reloadDips;

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
    private ReloadDip              _activeReloadDip;

    // ── Inventory aim state ────────────────────────────────────────────────
    private Transform  _inventoryAimTarget;
    private bool       _inventoryAimActive;
    private bool       _inventoryAimReturning;
    private Quaternion _savedPivotLocalRot;

    // Optional per-frame world point the beam follows (the cursor over the inventory).
    // When set, it overrides the fixed _inventoryAimTarget; cleared → falls back to the target.
    private bool       _hasAimPointOverride;
    private Vector3    _aimPointOverride;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (_weaponManager != null)
        {
            _weaponManager.OnWeaponEquipped += HandleWeaponEquipped;
            _activeReloadDip = FindReloadDip(_weaponManager.CurrentWeapon);
        }
        RunManager.Instance?.RegisterListener(this);
    }

    private void OnDisable()
    {
        if (_weaponManager != null)
            _weaponManager.OnWeaponEquipped -= HandleWeaponEquipped;
        CancelInventoryAim();
        RunManager.Instance?.UnregisterListener(this);
    }

    // ── IRunLifecycleListener ──────────────────────────────────────────────

    public void OnRunStarted()    { }
    public void OnRunExtracted()  { }
    public void OnReturnedToHub() { }

    public void OnRunDied() => Unequip();

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

        if (_flashlightSway != null)
        {
            _flashlightSway.DipPositionOffset = _activeReloadDip != null ? _activeReloadDip.FlashlightPositionOffset : Vector3.zero;
            _flashlightSway.DipRotationOffset = _activeReloadDip != null ? _activeReloadDip.FlashlightRotationOffset : Vector3.zero;
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

    private void LateUpdate()
    {
        if (_beamPivot == null) return;
        if (!_inventoryAimActive && !_inventoryAimReturning) return;

        // Natural world rotation — recomputed each frame so it tracks player/camera movement.
        Quaternion naturalWorld = _beamPivot.parent != null
            ? _beamPivot.parent.rotation * _savedPivotLocalRot
            : _savedPivotLocalRot;

        Quaternion targetRot;

        if (_inventoryAimActive && TryGetAimPosition(out Vector3 aimPos))
        {
            Vector3 dir = (aimPos - _beamPivot.position).normalized;
            if (dir == Vector3.zero) return;
            Quaternion aimWorld = Quaternion.LookRotation(dir) * Quaternion.Euler(_inventoryAimExtra);
            targetRot = Quaternion.Slerp(naturalWorld, aimWorld, _inventoryAimStrength);
        }
        else
        {
            // Returning: ease back to natural pose.
            targetRot = naturalWorld;
        }

        _beamPivot.rotation = Quaternion.RotateTowards(
            _beamPivot.rotation, targetRot, _inventoryAimSpeed * Time.deltaTime);

        // Snap-complete the return to avoid floating-point drift.
        if (_inventoryAimReturning && Quaternion.Angle(_beamPivot.rotation, targetRot) < 0.5f)
        {
            _beamPivot.localRotation = _savedPivotLocalRot;
            _inventoryAimReturning   = false;
        }
    }

    // ── Inventory aim ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by InventoryUI when the inventory opens. Eases the beam pivot toward
    /// the inventory target. No-op if already active.
    /// Safe to call mid-return — resumes aim without losing the saved natural rotation.
    /// </summary>
    public void BeginInventoryAim(Transform aimTarget)
    {
        if (_inventoryAimActive) return;
        if (_beamPivot == null) return;

        // If we're mid-return, _savedPivotLocalRot already holds the natural local rotation —
        // keep it. Only save a fresh snapshot when starting from a fully idle state.
        if (!_inventoryAimReturning)
            _savedPivotLocalRot = _beamPivot.localRotation;

        _inventoryAimReturning = false;
        _inventoryAimTarget    = aimTarget;
        _inventoryAimActive    = true;
    }

    /// <summary>
    /// Sets the world point the beam follows while the inventory is open — call each frame with
    /// the cursor's world position over the inventory panel. Overrides the fixed aim target until
    /// <see cref="ClearInventoryAimPoint"/> is called. No-op if inventory aim isn't active.
    /// </summary>
    public void SetInventoryAimPoint(Vector3 worldPoint)
    {
        if (!_inventoryAimActive) return;
        _hasAimPointOverride = true;
        _aimPointOverride    = worldPoint;
    }

    /// <summary>
    /// Stops following the cursor and falls the beam back to the fixed inventory target.
    /// Call when the cursor leaves the inventory panel.
    /// </summary>
    public void ClearInventoryAimPoint() => _hasAimPointOverride = false;

    /// <summary>Resolves the current aim world position — cursor override if set, else the fixed target.</summary>
    private bool TryGetAimPosition(out Vector3 position)
    {
        if (_hasAimPointOverride)        { position = _aimPointOverride;          return true; }
        if (_inventoryAimTarget != null) { position = _inventoryAimTarget.position; return true; }
        position = default;
        return false;
    }

    /// <summary>
    /// Called by InventoryUI when the inventory closes. Eases the pivot back to its
    /// natural orientation over time. Safe to call when already inactive.
    /// </summary>
    public void EndInventoryAim()
    {
        if (!_inventoryAimActive) return;

        _inventoryAimActive    = false;
        _inventoryAimTarget    = null;
        _inventoryAimReturning = _beamPivot != null;
        _hasAimPointOverride   = false;
    }

    /// <summary>Immediate pivot restore — used by Unequip and OnDisable.</summary>
    private void CancelInventoryAim()
    {
        _inventoryAimActive    = false;
        _inventoryAimReturning = false;
        _inventoryAimTarget    = null;
        _hasAimPointOverride   = false;

        if (_beamPivot != null)
            _beamPivot.localRotation = _savedPivotLocalRot;
    }

    // ── IEquipmentSlot ─────────────────────────────────────────────────────

    public bool TryEquip(ItemInstance item)
    {
        if (item is not FlashlightItemInstance fi) return false;

        Unequip();

        _equipped               = fi;
        _wasDepleted            = fi.IsDepleted;
        _lastReportedNormalized = fi.ChargeNormalized;
        _activeReloadDip = FindReloadDip(_weaponManager?.CurrentWeapon);

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
        CancelInventoryAim();
        if (_flashlightGO != null) _flashlightGO.SetActive(false);
        LightSource?.ForceOff();
        _equipped    = null;
        _wasDepleted = false;
        RestoreWeaponLeftIK();
        _lastReportedNormalized = 0f;
        if (_flashlightSway != null)
        {
            _flashlightSway.DipPositionOffset = Vector3.zero;
            _flashlightSway.DipRotationOffset = Vector3.zero;
        }
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
        _activeReloadDip = FindReloadDip(gun);

        if (_equipped == null || _flashlightGO == null) return;

        // null gun = no weapon equipped = standalone flashlight allowed (same as CurrentWeaponAllowsOffHand)
        bool allowed = gun == null || (gun.weaponData != null && gun.weaponData.allowsOffHandItem);
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

    private ReloadDip FindReloadDip(GunController gun)
    {
        if (gun == null || _reloadDips == null) return null;
        foreach (var dip in _reloadDips)
            if (dip != null && dip.Gun == gun) return dip;
        return null;
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
        if (_flashlightSway == null)
            Debug.LogWarning("[FlashlightSlot] FlashlightSway is not assigned — reload dip will not affect the flashlight.", this);
        if (_beamPivot == null)
            Debug.LogWarning("[FlashlightSlot] BeamPivot is not assigned — inventory aim will not work.", this);
    }
#endif
}

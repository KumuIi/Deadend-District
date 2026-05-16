using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Main weapon MonoBehaviour. Reads all tuning from WeaponSO.
/// Player-level refs are injected by WeaponManager.Initialize() before first enable.
///
/// Magazine flow (inventory integration):
///   • On equip:   call InsertMagazine(mag)
///   • On reload:  call EjectMagazine() to get old mag back, then StartReload(newMag)
///   • Each shot:  ConsumeRound() is called automatically inside FireShot()
/// </summary>
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(GunSway))]
public class GunController : MonoBehaviour
{
    // ── Inspector — weapon data ────────────────────────────────────────────

    [Header("=== Weapon Data ===")]
    public WeaponSO weaponData;

    // ── Inspector — gun pivot ──────────────────────────────────────────────

    [Header("=== Gun Pivot ===")]
    [Tooltip("The GunPivot empty child — shared with GunSway.")]
    public Transform gunPivot;

    // ── Inspector — bones ──────────────────────────────────────────────────

    [Header("=== Bone References ===")]
    [Tooltip("Slider / bolt bone.")]
    public Transform topBone;
    [Tooltip("Trigger bone.")]
    public Transform triggerBone;

    // ── Inspector — sockets ────────────────────────────────────────────────

    [Header("=== Sockets ===")]
    [Tooltip("Empty at the barrel tip — raycast origin and FX spawn.")]
    public Transform muzzlePoint;
    [Tooltip("Empty on the right side of the chamber.")]
    public Transform casingEjectPoint;
    [Tooltip("Empty placed at the iron sight / optic centre.")]
    public Transform aimSocket;

    // ── Inspector — IK grip targets ────────────────────────────────────────

    [Header("=== IK Grip Targets ===")]
    [Tooltip("Empty child where the right (primary) hand grips.")]
    public Transform rightHandGrip;
    [Tooltip("Empty child where the left (support) hand grips.")]
    public Transform leftHandGrip;

    // ── Inspector — reload events ──────────────────────────────────────────

    [Header("=== Reload Events ===")]
    [Tooltip("Fired at reloadMagEjectTime seconds — hook up animation, audio, VFX here.")]
    public UnityEvent OnMagEjected;
    [Tooltip("Fired at reloadMagInsertTime seconds — hook up animation, audio, VFX here.")]
    public UnityEvent OnMagInserted;
    [Tooltip("Fired when the full reload sequence finishes.")]
    public UnityEvent OnReloadComplete;

    // ── Public state ───────────────────────────────────────────────────────

    /// <summary>True while ADS input is held.</summary>
    public bool IsAiming { get; private set; }
    /// <summary>0 = hip, 1 = full ADS.</summary>
    public float AdsWeight { get; private set; }
    /// <summary>True while a reload coroutine is running.</summary>
    public bool IsReloading { get; private set; }

    // ── Magazine ───────────────────────────────────────────────────────────

    /// <summary>Rounds currently loaded in the active magazine.</summary>
    public int BulletsRemaining => _currentMagazine?.BulletCount ?? 0;
    /// <summary>Capacity of the active magazine.</summary>
    public int MagazineCapacity => _currentMagazine?.data.capacity ?? 0;
    /// <summary>Next round to fire, or weapon default ammo if no mag is loaded.</summary>
    public AmmunitionSO CurrentAmmo =>
        _currentMagazine?.PeekNextRound() ?? weaponData?.defaultAmmo;

    private MagazineInstance _currentMagazine;

    // ── Private refs ───────────────────────────────────────────────────────

    private Transform _playerCam;
    private PlayerMotor _playerMotor;
    private GunSway _sway;
    private AudioSource _audio;
    private ParticleSystem _muzzleFlash;

    // ── Private state ──────────────────────────────────────────────────────

    private float _nextFireTime;

    private float _boltCurrent, _boltTarget, _boltVelocity;
    private float _triggerCurrent, _triggerTarget, _triggerVelocity;

    private float _adsWeight, _adsVelocity;
    private Vector3 _hipPosition, _adsLocalTarget;

    private Vector3 _boltRestPos;
    private Quaternion _triggerRestRot;

    private int _burstShotsRemaining;
    private float _nextBurstShotTime;

    // ── Static registry ────────────────────────────────────────────────────

    /// <summary>
    /// All scene GunControllers keyed by their WeaponSO, populated during Awake.
    /// Replaces FindObjectsOfType lookups in the inventory equip path.
    /// </summary>
    public static readonly Dictionary<WeaponSO, GunController> Registry
        = new Dictionary<WeaponSO, GunController>();

    // ── Inventory integration ──────────────────────────────────────────────

    /// <summary>
    /// Set to true by the inventory system when this gun is managed through inventory.
    /// Disables the auto-load-from-defaultMagazineType in Start() and the free-reload
    /// fallback in HandleReloadInput(), so ammo state is fully owned by the inventory.
    /// </summary>
    [HideInInspector] public bool inventoryManaged;


    /// <summary>
    /// Invoked when the player presses R and the gun is inventory-managed.
    /// InventoryUI subscribes to this at equip time to perform the magazine search and reload.
    /// </summary>
    public System.Action<GunController> OnReloadRequested;

    // ── Injection ──────────────────────────────────────────────────────────

    /// <summary>Called by WeaponManager.Awake() while this object is disabled.</summary>
    public void Initialize(WeaponManager mgr)
    {
        _playerCam   = mgr.PlayerCam;
        _playerMotor = mgr.PlayerMotor;
        _sway = GetComponent<GunSway>();
        _sway.Initialize(gunPivot, mgr.PlayerMotor, mgr.PlayerCam, mgr.CameraController, this);
    }

    private void OnEnable()
    {
        if (_playerMotor != null && weaponData != null)
            _playerMotor.WeaponWeightMultiplier = WeightToMult(weaponData.weight);
    }

    private void OnDisable()
    {
        if (_playerMotor != null)
            _playerMotor.WeaponWeightMultiplier = 1f;
    }

    private static float WeightToMult(float w) =>
        1f / Mathf.Sqrt(Mathf.Max(0.01f, w));

    // ── Magazine API ───────────────────────────────────────────────────────

    /// <summary>Directly inserts a magazine (call from inventory on equip).</summary>
    public void InsertMagazine(MagazineInstance mag) => _currentMagazine = mag;

    /// <summary>Removes and returns the current magazine (call from inventory before reload).</summary>
    public MagazineInstance EjectMagazine()
    {
        var mag = _currentMagazine;
        _currentMagazine = null;
        return mag;
    }

    /// <summary>
    /// Starts a reload sequence with newMag.
    /// If newMag is null and WeaponSO.defaultMagazineType is set, auto-creates a
    /// full magazine for debug / testing without a real inventory.
    /// </summary>
    public void StartReload(MagazineInstance newMag = null)
    {
        if (IsReloading || !weaponData) return;

        if (newMag == null)
        {
            if (weaponData.defaultMagazineType == null) return;
            newMag = new MagazineInstance(weaponData.defaultMagazineType);
            if (weaponData.defaultAmmo) newMag.FillWith(weaponData.defaultAmmo);
        }

        if (newMag.data.caliber != weaponData.caliber)
        {
            Debug.LogWarning($"GunController: magazine caliber '{newMag.data.caliber.displayName}' " +
                             $"doesn't match weapon caliber '{weaponData.caliber.displayName}'.");
            return;
        }

        StartCoroutine(ReloadCoroutine(newMag));
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (topBone)     _boltRestPos    = topBone.localPosition;
        if (triggerBone) _triggerRestRot = triggerBone.localRotation;
        if (gunPivot)
        {
            _hipPosition   = gunPivot.localPosition;
            _adsLocalTarget = _hipPosition;
        }

        _audio = GetComponent<AudioSource>();

        if (weaponData?.muzzleFlashPrefab && muzzlePoint)
        {
            _muzzleFlash = Instantiate(weaponData.muzzleFlashPrefab,
                muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);
            _muzzleFlash.Stop();
        }

        // Auto-load debug magazine only when NOT managed by the inventory system.
        if (!inventoryManaged && _currentMagazine == null && weaponData?.defaultMagazineType != null)
            StartReload();
    }

    private void Update()
    {
        if (!weaponData) return;

        HandleHoldOpen();
        HandleADS();
        HandleReloadInput();
        HandleFireInput();
        TickBolt();
        TickTrigger();
    }

    private void LateUpdate()
    {
        if (topBone)
            topBone.localPosition = _boltRestPos + new Vector3(-_boltCurrent, 0f, 0f);

        if (triggerBone)
            triggerBone.localRotation = _triggerRestRot * Quaternion.Euler(_triggerCurrent, 0f, 0f);

        ApplyAdsPivot();
    }

    // ── Input handlers ─────────────────────────────────────────────────────

    private void HandleHoldOpen()
    {
        if (!GameInputState.HoldOpenHeld) return;
        _boltTarget = _boltCurrent = -weaponData.boltTravelDistance;
        _boltVelocity = 0f;
    }

    private void HandleADS()
    {
        IsAiming = !GameInputState.GameplayBlocked && GameInputState.AimHeld;
        float adsSmooth = IsAiming ? weaponData.adsInTime : weaponData.adsOutTime;
        _adsWeight = Mathf.SmoothDamp(_adsWeight, IsAiming ? 1f : 0f, ref _adsVelocity, adsSmooth);
        AdsWeight = _adsWeight;
    }

    private void HandleReloadInput()
    {
        if (GameInputState.GameplayBlocked || IsReloading || !GameInputState.ReloadPressed) return;

        if (inventoryManaged)
            OnReloadRequested?.Invoke(this);
        else
            StartReload();
    }

    private void HandleFireInput()
    {
        if (IsReloading || GameInputState.GameplayBlocked) return;

        float rpm = IsAiming
            ? weaponData.fireRate * weaponData.adsFirerateMultiplier
            : weaponData.fireRate;

        switch (weaponData.fireMode)
        {
            case FireMode.FullAuto:
                if (GameInputState.FireHeld && Time.time >= _nextFireTime && CanFire())
                {
                    FireShot();
                    _nextFireTime = Time.time + 60f / rpm;
                }
                break;

            case FireMode.SemiAuto:
                if (GameInputState.FirePressed && Time.time >= _nextFireTime && CanFire())
                {
                    FireShot();
                    _nextFireTime = Time.time + 60f / rpm;
                }
                break;

            case FireMode.Burst:
                if (GameInputState.FirePressed && Time.time >= _nextFireTime
                    && _burstShotsRemaining == 0 && CanFire())
                {
                    _burstShotsRemaining = weaponData.burstCount;
                    _nextBurstShotTime   = Time.time;
                }
                break;
        }

        // Burst drain — independent of fire mode block.
        if (_burstShotsRemaining > 0 && Time.time >= _nextBurstShotTime && CanFire())
        {
            FireShot();
            _burstShotsRemaining--;
            _nextBurstShotTime = Time.time + weaponData.burstShotInterval;
            if (_burstShotsRemaining == 0)
            {
                float finalRpm = IsAiming
                    ? weaponData.fireRate * weaponData.adsFirerateMultiplier
                    : weaponData.fireRate;
                _nextFireTime = Time.time + 60f / finalRpm;
            }
        }
    }

    // ── Bolt / trigger tick ────────────────────────────────────────────────

    private void TickBolt()
    {
        if (GameInputState.HoldOpenHeld) return; // handled in HandleHoldOpen

        float boltSmooth = _boltTarget < -0.0001f ? weaponData.boltBackTime : weaponData.boltForwardTime;
        _boltCurrent = Mathf.SmoothDamp(_boltCurrent, _boltTarget, ref _boltVelocity, boltSmooth);

        if (_boltTarget < 0f && Mathf.Abs(_boltCurrent - _boltTarget) < 0.0001f)
        { _boltTarget = 0f; _boltVelocity = 0f; }
        if (_boltTarget >= 0f && Mathf.Abs(_boltCurrent) < 0.00005f)
        { _boltCurrent = 0f; _boltVelocity = 0f; }
    }

    private void TickTrigger()
    {
        float trigSmooth = _triggerTarget > 0.01f ? weaponData.triggerPullTime : weaponData.triggerReleaseTime;
        _triggerCurrent = Mathf.SmoothDamp(_triggerCurrent, _triggerTarget, ref _triggerVelocity, trigSmooth);

        if (_triggerTarget > 0f && Mathf.Abs(_triggerCurrent - _triggerTarget) < 0.01f)
        { _triggerTarget = 0f; _triggerVelocity = 0f; }
        if (_triggerTarget <= 0f && Mathf.Abs(_triggerCurrent) < 0.001f)
        { _triggerCurrent = 0f; _triggerVelocity = 0f; }
    }

    // ── ADS pivot ──────────────────────────────────────────────────────────

    private void ApplyAdsPivot()
    {
        if (!gunPivot || !_playerCam) return;

        if (aimSocket)
        {
            Vector3 socketToGunPivot = gunPivot.position - aimSocket.position;
            Vector3 adsWorldPos = _playerCam.position + socketToGunPivot;
            _adsLocalTarget = gunPivot.parent
                ? gunPivot.parent.InverseTransformPoint(adsWorldPos)
                : adsWorldPos;
        }

        Vector3 adsShift       = _adsLocalTarget - _hipPosition;
        Vector3 swayContribution = gunPivot.localPosition - _hipPosition;
        gunPivot.localPosition = _hipPosition + swayContribution + adsShift * _adsWeight;
    }

    // ── Reload coroutine ───────────────────────────────────────────────────

    private IEnumerator ReloadCoroutine(MagazineInstance newMag)
    {
        IsReloading = true;

        yield return new WaitForSeconds(weaponData.reloadMagEjectTime);
        OnMagEjected?.Invoke();

        float insertWait = Mathf.Max(0f, weaponData.reloadMagInsertTime - weaponData.reloadMagEjectTime);
        yield return new WaitForSeconds(insertWait);
        _currentMagazine = newMag;
        OnMagInserted?.Invoke();

        float finishWait = Mathf.Max(0f, weaponData.reloadTime - weaponData.reloadMagInsertTime);
        yield return new WaitForSeconds(finishWait);

        IsReloading = false;
        OnReloadComplete?.Invoke();
    }

    // ── Fire ───────────────────────────────────────────────────────────────

    private bool CanFire() => _currentMagazine != null && !_currentMagazine.IsEmpty;

    private void FireShot()
    {
        AmmunitionSO ammo = CurrentAmmo;
        _currentMagazine?.ConsumeRound();

        Vector3 origin = _playerCam
            ? _playerCam.position
            : (muzzlePoint ? muzzlePoint.position : transform.position);
        Vector3 direction = _playerCam
            ? _playerCam.forward
            : (muzzlePoint ? muzzlePoint.forward : transform.forward);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, weaponData.range, weaponData.hitLayers))
        {
            Debug.DrawLine(origin, hit.point, Color.red, 0.3f);

            float damage = ammo != null
                ? ammo.GetDamageAtDistance(hit.distance, weaponData.range)
                : weaponData.baseDamage;

            Debug.Log($"Hit: {hit.collider.name} | Dmg: {damage:F1} | Mag: {BulletsRemaining}/{MagazineCapacity}");

            if (ammo != null && ammo.isExplosive)
                ApplyExplosion(hit.point, ammo);
        }

        if (_muzzleFlash) _muzzleFlash.Play();
        if (weaponData.gunshotClip) _audio.PlayOneShot(weaponData.gunshotClip);

        EjectCasing();

        _boltTarget   = -weaponData.boltTravelDistance;
        _boltVelocity = -weaponData.boltTravelDistance / weaponData.boltBackTime;

        _triggerTarget   = weaponData.triggerRotationAngle;
        _triggerVelocity = weaponData.triggerRotationAngle / weaponData.triggerPullTime;
    }

    private void ApplyExplosion(Vector3 centre, AmmunitionSO ammo)
    {
        Collider[] cols = Physics.OverlapSphere(centre, ammo.explosionRadius, weaponData.hitLayers);
        foreach (Collider col in cols)
            col.attachedRigidbody?.AddExplosionForce(
                ammo.explosionForce, centre, ammo.explosionRadius, 0.5f);
    }

    private void EjectCasing()
    {
        if (!weaponData.casingPrefab || !casingEjectPoint) return;

        GameObject casing = Instantiate(weaponData.casingPrefab,
            casingEjectPoint.position, casingEjectPoint.rotation);

        Rigidbody rb = casing.GetComponent<Rigidbody>();
        if (!rb)
        {
            Debug.LogWarning("GunController: casing prefab missing Rigidbody.", weaponData.casingPrefab);
            Destroy(casing);
            return;
        }

        Vector3 ejectDir = (casingEjectPoint.right
            + casingEjectPoint.up * 0.5f
            + casingEjectPoint.forward * -0.2f).normalized;

        Vector3 spread = new Vector3(
            Random.Range(-weaponData.casingEjectSpread, weaponData.casingEjectSpread) * 0.3f,
            Random.Range(0f, weaponData.casingEjectSpread) * 0.5f,
            Random.Range(-weaponData.casingEjectSpread, weaponData.casingEjectSpread) * 0.2f);

        rb.linearVelocity = (ejectDir * weaponData.casingEjectForce + spread)
            + (_playerCam ? _playerCam.forward * 0.5f : Vector3.zero);

        rb.angularVelocity = new Vector3(
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque),
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque),
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque));

        Destroy(casing, weaponData.casingLifetime);
    }
}

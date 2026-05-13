using System.Collections;
using UnityEngine;

/// <summary>
/// GunController — weapon initialization, ADS pivot, fire modes, bolt/trigger animation,
/// casing ejection, magazine management, and reload.
///
/// All tuning data is read from the assigned WeaponSO.
/// Magazine state is managed through MagazineInstance; swap it from the inventory system
/// by calling InsertMagazine() / EjectMagazine(), then StartReload().
///
/// Gun-internal refs (pivot, bones, sockets, grip targets) are serialized on the prefab.
/// Player-level refs are injected by WeaponManager before the object is enabled.
/// </summary>
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(GunSway))]
public class GunController : MonoBehaviour
{
    [Header("=== Weapon Data ===")]
    public WeaponSO weaponData;

    [Header("=== Gun Pivot ===")]
    [Tooltip("The GunPivot empty child — shared with GunSway")]
    public Transform gunPivot;

    [Header("=== Bone References ===")]
    [Tooltip("Slider / bolt bone")]
    public Transform topBone;
    [Tooltip("Trigger bone")]
    public Transform triggerBone;

    [Header("=== Sockets ===")]
    [Tooltip("Empty at the barrel tip — raycast origin and FX spawn")]
    public Transform muzzlePoint;
    [Tooltip("Empty on the right side of the chamber")]
    public Transform casingEjectPoint;
    [Tooltip("Empty placed at the iron sight / optic centre")]
    public Transform aimSocket;

    [Header("=== IK Grip Targets ===")]
    [Tooltip("Empty child where the right (primary) hand grips")]
    public Transform rightHandGrip;
    [Tooltip("Empty child where the left (support) hand grips")]
    public Transform leftHandGrip;

    [Header("=== Controls ===")]
    public KeyCode reloadKey   = KeyCode.R;
    public KeyCode holdOpenKey = KeyCode.H;

    // ── Public state ──────────────────────────────────────────────────────
    public bool  IsAiming    { get; private set; }
    /// <summary>0 = hip, 1 = full ADS</summary>
    public float AdsWeight   { get; private set; }
    public bool  IsReloading { get; private set; }

    // ── Magazine ──────────────────────────────────────────────────────────
    public int  BulletsRemaining => _currentMagazine?.BulletCount ?? 0;
    public int  MagazineCapacity => _currentMagazine?.data.capacity ?? 0;

    /// <summary>Returns the ammo type of the next round, or the weapon default if no mag is loaded.</summary>
    public AmmunitionSO CurrentAmmo =>
        _currentMagazine?.PeekNextRound() ?? weaponData?.defaultAmmo;

    private MagazineInstance _currentMagazine;

    // ── Injected player refs ───────────────────────────────────────────────
    private Transform _playerCam;
    private GunSway   _sway;

    // ── Private state ─────────────────────────────────────────────────────
    private float          _nextFireTime;
    private AudioSource    _audio;
    private ParticleSystem _muzzleFlash;

    // Bolt
    private float _boltCurrent, _boltTarget, _boltVelocity;

    // Trigger
    private float _triggerCurrent, _triggerTarget, _triggerVelocity;

    // ADS
    private float   _adsWeight, _adsVelocity;
    private Vector3 _hipPosition, _adsLocalTarget;

    // Bone rest poses
    private Vector3    _boltRestPos;
    private Quaternion _triggerRestRot;

    // Burst
    private int   _burstShotsRemaining;
    private float _nextBurstShotTime;

    // ── Injection ─────────────────────────────────────────────────────────

    /// <summary>Called by WeaponManager while the object is disabled.</summary>
    public void Initialize(WeaponManager mgr)
    {
        _playerCam = mgr.PlayerCam;
        _sway      = GetComponent<GunSway>();
        _sway.Initialize(gunPivot, mgr.PlayerMotor, mgr.PlayerCam, mgr.CameraController, this);
    }

    // ── Magazine API (called by inventory / weapon manager) ───────────────

    /// <summary>Directly inserts a magazine (e.g. from inventory on equip).</summary>
    public void InsertMagazine(MagazineInstance mag) => _currentMagazine = mag;

    /// <summary>Removes and returns the current magazine (e.g. to return to inventory).</summary>
    public MagazineInstance EjectMagazine()
    {
        var mag = _currentMagazine;
        _currentMagazine = null;
        return mag;
    }

    /// <summary>
    /// Begins a reload using newMag. If newMag is null and weaponData.defaultMagazineType
    /// is assigned, auto-creates a full magazine (debug/testing convenience).
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
            Debug.LogWarning($"GunController: magazine caliber '{newMag.data.caliber}' " +
                             $"doesn't match weapon caliber '{weaponData.caliber}'.");
            return;
        }

        StartCoroutine(ReloadCoroutine(newMag));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Start()
    {
        if (topBone)     _boltRestPos    = topBone.localPosition;
        if (triggerBone) _triggerRestRot = triggerBone.localRotation;
        if (gunPivot)
        {
            _hipPosition    = gunPivot.localPosition;
            _adsLocalTarget = _hipPosition;
        }

        _audio = GetComponent<AudioSource>();

        if (weaponData?.muzzleFlashPrefab && muzzlePoint)
        {
            _muzzleFlash = Instantiate(weaponData.muzzleFlashPrefab,
                muzzlePoint.position, muzzlePoint.rotation, muzzlePoint);
            _muzzleFlash.Stop();
        }

        // Auto-load a default magazine for testing
        if (_currentMagazine == null && weaponData?.defaultMagazineType)
            StartReload();
    }

    void Update()
    {
        if (!weaponData) return;

        // ── Hold-open debug ───────────────────────────────────────────────
        if (Input.GetKey(holdOpenKey))
        {
            _boltTarget = _boltCurrent = -weaponData.boltTravelDistance;
            _boltVelocity = 0f;
            return;
        }

        // ── ADS ───────────────────────────────────────────────────────────
        IsAiming = !GameInputState.GameplayBlocked && Input.GetButton("Fire2");
        float adsSmooth = IsAiming ? weaponData.adsInTime : weaponData.adsOutTime;
        _adsWeight = Mathf.SmoothDamp(_adsWeight, IsAiming ? 1f : 0f, ref _adsVelocity, adsSmooth);
        AdsWeight  = _adsWeight;

        // ── Reload input ──────────────────────────────────────────────────
        if (!GameInputState.GameplayBlocked && Input.GetKeyDown(reloadKey) && !IsReloading)
            StartReload();

        // ── Fire input ────────────────────────────────────────────────────
        if (!IsReloading && !GameInputState.GameplayBlocked)
        {
            float rpm = IsAiming
                ? weaponData.fireRate * weaponData.adsFirerateMultiplier
                : weaponData.fireRate;

            switch (weaponData.fireMode)
            {
                case FireMode.FullAuto:
                    if (Input.GetButton("Fire1") && Time.time >= _nextFireTime && CanFire())
                    {
                        FireShot();
                        _nextFireTime = Time.time + 60f / rpm;
                    }
                    break;

                case FireMode.SemiAuto:
                    if (Input.GetButtonDown("Fire1") && Time.time >= _nextFireTime && CanFire())
                    {
                        FireShot();
                        _nextFireTime = Time.time + 60f / rpm;
                    }
                    break;

                case FireMode.Burst:
                    if (Input.GetButtonDown("Fire1") && Time.time >= _nextFireTime
                        && _burstShotsRemaining == 0 && CanFire())
                    {
                        _burstShotsRemaining = weaponData.burstCount;
                        _nextBurstShotTime   = Time.time;
                    }
                    break;
            }

            // Burst tick — independent of fire mode block so it drains naturally
            if (_burstShotsRemaining > 0 && Time.time >= _nextBurstShotTime && CanFire())
            {
                FireShot();
                _burstShotsRemaining--;
                _nextBurstShotTime = Time.time + weaponData.burstShotInterval;
                if (_burstShotsRemaining == 0)
                    _nextFireTime = Time.time + 60f / (IsAiming
                        ? weaponData.fireRate * weaponData.adsFirerateMultiplier
                        : weaponData.fireRate);
            }
        }

        // ── Bolt tick ─────────────────────────────────────────────────────
        float boltSmooth = (_boltTarget < -0.0001f) ? weaponData.boltBackTime : weaponData.boltForwardTime;
        _boltCurrent = Mathf.SmoothDamp(_boltCurrent, _boltTarget, ref _boltVelocity, boltSmooth);

        if (_boltTarget < 0f  && Mathf.Abs(_boltCurrent - _boltTarget) < 0.0001f)
            { _boltTarget = 0f; _boltVelocity = 0f; }
        if (_boltTarget >= 0f && Mathf.Abs(_boltCurrent) < 0.00005f)
            { _boltCurrent = 0f; _boltVelocity = 0f; }

        // ── Trigger tick ──────────────────────────────────────────────────
        float trigSmooth = (_triggerTarget > 0.01f) ? weaponData.triggerPullTime : weaponData.triggerReleaseTime;
        _triggerCurrent = Mathf.SmoothDamp(_triggerCurrent, _triggerTarget, ref _triggerVelocity, trigSmooth);

        if (_triggerTarget > 0f  && Mathf.Abs(_triggerCurrent - _triggerTarget) < 0.01f)
            { _triggerTarget = 0f; _triggerVelocity = 0f; }
        if (_triggerTarget <= 0f && Mathf.Abs(_triggerCurrent) < 0.001f)
            { _triggerCurrent = 0f; _triggerVelocity = 0f; }
    }

    void LateUpdate()
    {
        if (topBone)
            topBone.localPosition = _boltRestPos + new Vector3(-_boltCurrent, 0f, 0f);

        if (triggerBone)
            triggerBone.localRotation = _triggerRestRot * Quaternion.Euler(_triggerCurrent, 0f, 0f);

        ApplyAdsPivot();
    }

    // ── ADS pivot ─────────────────────────────────────────────────────────

    void ApplyAdsPivot()
    {
        if (!gunPivot || !_playerCam) return;

        if (aimSocket)
        {
            Vector3 socketToGunPivot = gunPivot.position - aimSocket.position;
            Vector3 adsWorldPos      = _playerCam.position + socketToGunPivot;
            _adsLocalTarget = gunPivot.parent
                ? gunPivot.parent.InverseTransformPoint(adsWorldPos)
                : adsWorldPos;
        }

        Vector3 adsShift         = _adsLocalTarget - _hipPosition;
        Vector3 swayContribution = gunPivot.localPosition - _hipPosition;
        gunPivot.localPosition   = _hipPosition + swayContribution + adsShift * _adsWeight;
    }

    // ── Reload coroutine ──────────────────────────────────────────────────

    IEnumerator ReloadCoroutine(MagazineInstance newMag)
    {
        IsReloading = true;
        yield return new WaitForSeconds(weaponData.reloadTime);
        _currentMagazine = newMag;
        IsReloading = false;
    }

    // ── Fire ──────────────────────────────────────────────────────────────

    bool CanFire() => _currentMagazine != null && !_currentMagazine.IsEmpty;

    void FireShot()
    {
        AmmunitionSO ammo = CurrentAmmo;
        _currentMagazine?.ConsumeRound();

        Vector3 origin    = _playerCam ? _playerCam.position : (muzzlePoint ? muzzlePoint.position  : transform.position);
        Vector3 direction = _playerCam ? _playerCam.forward  : (muzzlePoint ? muzzlePoint.forward   : transform.forward);

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

        if (_muzzleFlash)          _muzzleFlash.Play();
        if (weaponData.gunshotClip) _audio.PlayOneShot(weaponData.gunshotClip);

        EjectCasing();

        _boltTarget   = -weaponData.boltTravelDistance;
        _boltVelocity = -weaponData.boltTravelDistance / weaponData.boltBackTime;

        _triggerTarget   = weaponData.triggerRotationAngle;
        _triggerVelocity = weaponData.triggerRotationAngle / weaponData.triggerPullTime;
    }

    void ApplyExplosion(Vector3 centre, AmmunitionSO ammo)
    {
        Collider[] cols = Physics.OverlapSphere(centre, ammo.explosionRadius, weaponData.hitLayers);
        foreach (Collider col in cols)
        {
            col.attachedRigidbody?.AddExplosionForce(
                ammo.explosionForce, centre, ammo.explosionRadius, 0.5f);
        }
    }

    void EjectCasing()
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
            + casingEjectPoint.up      * 0.5f
            + casingEjectPoint.forward * -0.2f).normalized;

        Vector3 spread = new Vector3(
            Random.Range(-weaponData.casingEjectSpread, weaponData.casingEjectSpread) * 0.3f,
            Random.Range(0f,                            weaponData.casingEjectSpread) * 0.5f,
            Random.Range(-weaponData.casingEjectSpread, weaponData.casingEjectSpread) * 0.2f);

        rb.linearVelocity  = (ejectDir * weaponData.casingEjectForce + spread)
            + (_playerCam ? _playerCam.forward * 0.5f : Vector3.zero);

        rb.angularVelocity = new Vector3(
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque),
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque),
            Random.Range(-weaponData.casingTorque, weaponData.casingTorque));

        Destroy(casing, weaponData.casingLifetime);
    }
}

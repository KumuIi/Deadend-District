using System.Collections;
using UnityEngine;

/// <summary>
/// NPC weapon controller. Owns a WeaponItemInstance built from a WeaponSO,
/// fires hitscan raycasts from muzzle.forward, tracks magazine ammo, reloads,
/// and drops the live instance as a LootItemWorld on death.
///
/// Does NOT use GunController, GunSway, player input, or player camera refs.
/// </summary>
public class EnemyWeaponDriver : MonoBehaviour, IWeaponDriver
{
    [SerializeField] private EnemyAimComponent _aimComponent;
    [SerializeField] private AudioSource       _audioSource;

    [Tooltip("Layer the dropped loot is placed on — must match PlayerInteractor's interaction mask (default 6 = Interactable).")]
    [SerializeField] private int _droppedItemLayer = 6;
    [Tooltip("Impulse force applied to the dropped weapon on death.")]
    [SerializeField] private float _dropThrowForce = 5f;
    [Tooltip("Random spin torque applied to the dropped weapon on death.")]
    [SerializeField] private float _dropSpinForce  = 4f;

    [Tooltip("Multiplies bullet damage before applying to the target. 1 = full weapon damage, 0.5 = half.")]
    [Min(0f)]
    [SerializeField] private float _damageMultiplier = 0.5f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private WeaponSO             _weaponData;
    private GameObject           _owner;
    private Transform            _muzzle;
    private WeaponItemInstance   _weaponInstance;
    private MagazineItemInstance _magInstance;
    private float                _fireCooldown;
    private float                _fireTimer;
    private bool                 _isReloading;

    // ── IWeaponDriver ─────────────────────────────────────────────────────────

    public bool CanFire =>
        _weaponInstance?.LoadedMagazine != null
        && !_weaponInstance.LoadedMagazine.RuntimeMag.IsEmpty
        && !_isReloading
        && _fireTimer <= 0f;

    public bool NeedsReload =>
        _weaponInstance?.LoadedMagazine == null
        || _weaponInstance.LoadedMagazine.RuntimeMag.IsEmpty;

    public int CurrentAmmo =>
        _weaponInstance?.LoadedMagazine?.RuntimeMag.BulletCount ?? 0;

    public void Initialize(WeaponSO weapon, GameObject owner, Transform muzzle)
    {
        if (weapon == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: WeaponSO is null.");
            return;
        }
        if (weapon.defaultMagazineType == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: WeaponSO.defaultMagazineType is null.");
            return;
        }
        if (weapon.defaultAmmo == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: WeaponSO.defaultAmmo is null.");
            return;
        }
        if (muzzle == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: Muzzle transform is null.");
            return;
        }

        _weaponData = weapon;
        _owner      = owner;
        _muzzle     = muzzle;

        _weaponInstance = ItemInstanceFactory.Create(weapon) as WeaponItemInstance;
        if (_weaponInstance == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: ItemInstanceFactory did not produce a WeaponItemInstance.");
            return;
        }

        _magInstance = ItemInstanceFactory.Create(weapon.defaultMagazineType) as MagazineItemInstance;
        if (_magInstance == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: ItemInstanceFactory did not produce a MagazineItemInstance.");
            return;
        }

        _magInstance.RuntimeMag.FillWith(weapon.defaultAmmo);
        _weaponInstance.LoadMagazine(_magInstance);

        _fireCooldown = 60f / Mathf.Max(weapon.fireRate, 1f);
        Debug.Log($"[EnemyWeaponDriver] {name}: Initialized — {CurrentAmmo} rounds loaded (cooldown {_fireCooldown:F3}s).");
    }

    public void SetAimTarget(Transform target) => _aimComponent?.SetTarget(target);
    public void ClearAim()                     => _aimComponent?.ClearTarget();

    public bool FireAt(Vector3 targetPoint, float accuracy)
    {
        if (!CanFire) return false;

        var round = _weaponInstance.LoadedMagazine.RuntimeMag.ConsumeRound();
        if (round == null) return false;

        _fireTimer = _fireCooldown;

        float spreadDeg = (1f - Mathf.Clamp01(accuracy)) * 3f;
        Vector3 spreadDir = Quaternion.Euler(
            UnityEngine.Random.Range(-spreadDeg, spreadDeg),
            UnityEngine.Random.Range(-spreadDeg, spreadDeg),
            0f) * _muzzle.forward;

        Vector3 rayEnd = _muzzle.position + spreadDir * _weaponData.range;
        if (Physics.Raycast(_muzzle.position, spreadDir, out RaycastHit hit, _weaponData.range, _weaponData.hitLayers))
        {
            Debug.DrawLine(_muzzle.position, hit.point, Color.yellow, 0.3f);

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                HitZone.Resolve(hit.collider, out string hitZoneId, out float zoneMultiplier);
                float dmg = round.GetDamageAtDistance(hit.distance, _weaponData.range)
                          * _damageMultiplier * zoneMultiplier;
                damageable.ApplyDamage(new DamageContext
                {
                    Source     = gameObject,
                    Instigator = _owner,
                    HitPoint   = hit.point,
                    HitNormal  = hit.normal,
                    HitZoneId  = hitZoneId,
                    Type       = DamageType.Bullet,
                    BaseDamage = dmg,
                    Impulse    = dmg * 2f,
                });
            }
        }
        else
        {
            Debug.DrawLine(_muzzle.position, rayEnd, Color.yellow, 0.3f);
        }

        if (_weaponData.gunshotClip != null && _audioSource != null)
            _audioSource.PlayOneShot(_weaponData.gunshotClip);

        if (StimulusSystem.Instance != null)
            StimulusSystem.Instance.Broadcast(new Stimulus(
                StimulusType.Sound,
                _muzzle.position,
                _weaponData.range * 0.6f,
                0.9f,
                gameObject,
                _owner));

        return true;
    }

    public void Reload()
    {
        if (_isReloading) return;
        if (_magInstance == null)
        {
            Debug.LogWarning($"[EnemyWeaponDriver] {name}: Reload skipped — mag instance is null.");
            return;
        }
        StartCoroutine(ReloadCoroutine());
    }

    public void DetachAndDrop(Vector3 throwDirection)
    {
        if (_weaponInstance == null) return;

        Transform origin = _muzzle != null ? _muzzle : transform;
        Vector3   dir    = throwDirection.sqrMagnitude > 0.001f ? throwDirection.normalized : origin.forward;

        bool spawned = ItemDropSpawner.TryDrop(
            _weaponInstance,
            origin,
            dir,
            throwForce:         _dropThrowForce,
            spinForce:          _dropSpinForce,
            interactableLayer:  _droppedItemLayer);

        if (spawned)
            Debug.Log($"[EnemyWeaponDriver] {name}: Weapon dropped — {CurrentAmmo} rounds remaining in mag.");
        else
            Debug.LogError($"[EnemyWeaponDriver] {name}: ItemDropSpawner failed to spawn weapon loot.");

        _weaponInstance = null;
    }

    // ── Unity ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_fireTimer > 0f)
            _fireTimer -= Time.deltaTime;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private IEnumerator ReloadCoroutine()
    {
        _isReloading = true;
        Debug.Log($"[EnemyWeaponDriver] {name}: Reloading ({_weaponData.reloadTime}s)...");

        if (_weaponData.reloadClips != null
            && _weaponData.reloadClips.Length > 0
            && _audioSource != null)
            _audioSource.PlayOneShot(_weaponData.reloadClips[0]);

        yield return new WaitForSeconds(_weaponData.reloadTime);

        _magInstance.RuntimeMag.FillWith(_weaponData.defaultAmmo);
        _isReloading = false;
        Debug.Log($"[EnemyWeaponDriver] {name}: Reload complete — {CurrentAmmo} rounds.");
    }
}

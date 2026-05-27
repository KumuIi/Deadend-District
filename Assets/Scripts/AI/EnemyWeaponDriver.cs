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
    [SerializeField] private GameObject        _lootItemWorldPrefab;

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

    public void FireAt(Vector3 targetPoint, float accuracy)
    {
        Debug.Log($"[EnemyWeaponDriver] FireAt called — CanFire: {CanFire}, Ammo: {CurrentAmmo}");
        if (!CanFire) return;

        var round = _weaponInstance.LoadedMagazine.RuntimeMag.ConsumeRound();
        if (round == null)
        {
            Debug.LogWarning($"[EnemyWeaponDriver] {name}: ConsumeRound returned null.");
            return;
        }

        _fireTimer = _fireCooldown;

        // Build spread direction from muzzle forward
        float spreadDeg = (1f - Mathf.Clamp01(accuracy)) * 3f;
        Vector3 spreadDir = Quaternion.Euler(
            UnityEngine.Random.Range(-spreadDeg, spreadDeg),
            UnityEngine.Random.Range(-spreadDeg, spreadDeg),
            0f) * _muzzle.forward;

        Debug.Log($"[EnemyWeaponDriver] Raycast from {_muzzle.position} dir {spreadDir} range {_weaponData.range}");

        if (Physics.Raycast(_muzzle.position, spreadDir, out RaycastHit hit, _weaponData.range, _weaponData.hitLayers))
        {
            Debug.Log($"[EnemyWeaponDriver] Raycast hit: {hit.collider.name} on {hit.collider.transform.root.name}");

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
            {
                float dmg   = round.GetDamageAtDistance(hit.distance, _weaponData.range);
                float dealt = damageable.ApplyDamage(new DamageContext
                {
                    Source     = gameObject,
                    Instigator = _owner,
                    HitPoint   = hit.point,
                    HitNormal  = hit.normal,
                    HitZoneId  = "",
                    Type       = DamageType.Bullet,
                    BaseDamage = dmg,
                    Impulse    = dmg * 2f,
                });
                Debug.Log($"[EnemyWeaponDriver] ApplyDamage — target: {damageable}, dmg: {dealt:F1}");
            }
        }
        else
        {
            Debug.Log("[EnemyWeaponDriver] Raycast — no hit.");
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

    public void DetachAndDrop()
    {
        if (_weaponInstance == null) return;

        if (_lootItemWorldPrefab == null)
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: LootItemWorldPrefab is null — cannot drop weapon.");
            return;
        }

        Vector3 dropPos = _muzzle != null ? _muzzle.position : transform.position;
        var lootGO = Instantiate(_lootItemWorldPrefab, dropPos, Quaternion.identity);
        var loot   = lootGO.GetComponent<LootItemWorld>();

        if (loot != null)
        {
            loot.Initialize(_weaponInstance);
            Debug.Log($"[EnemyWeaponDriver] {name}: Weapon dropped — {CurrentAmmo} rounds remaining in mag.");
        }
        else
        {
            Debug.LogError($"[EnemyWeaponDriver] {name}: LootItemWorldPrefab is missing a LootItemWorld component.");
        }

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

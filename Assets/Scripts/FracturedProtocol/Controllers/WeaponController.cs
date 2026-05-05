#nullable enable
using System;
using UnityEngine;
using FracturedProtocol.Combat.Instances;
using FracturedProtocol.Combat.Items;
using FracturedProtocol.Combat.Stats;

namespace FracturedProtocol.Combat.Controllers
{
    /// <summary>
    /// MonoBehaviour that owns the active weapon and dispatches input to it.
    /// Tracks per-shot bloom and emits SpreadChanged so UI can reflect current spread.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private WeaponSO?    debugWeapon;
        [SerializeField] private WeaponSO?    debugWeapon2;
        [SerializeField] private MagazineSO?  debugMagazine;
        [SerializeField] private Transform?   muzzlePoint;
        [SerializeField] private Animator?    armsAnimator;

        private WeaponInstance?    _weapon;
        private MagazineInstance?  _debugSpareMag;

        // ── Bloom state ───────────────────────────────────────────────────
        private int   _consecutiveShots;
        private float _currentSpread;
        private float _fireTimer;
        private float _shotDecayAccumulator;

        private const float SpreadDecaySpeed = 4f;
        private const float ShotDecayRate    = 3f;

        /// <summary>Fired whenever spread changes (on shot and during decay).</summary>
        public event Action<float>? SpreadChanged;

        /// <summary>The weapon instance currently held.</summary>
        public WeaponInstance? CurrentWeapon => _weapon;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            EquipFromDef(debugWeapon);

            if (debugMagazine != null && _weapon != null)
            {
                _weapon.currentMagazine = CreateMagInstance(debugMagazine);
                _debugSpareMag          = CreateMagInstance(debugMagazine); // full spare for R-key swap
                StatCalculator.Recalculate(_weapon);
            }
        }

        private void Update()
        {
            _fireTimer -= Time.deltaTime;

            if (Input.GetMouseButton(0))
            {
                if (_fireTimer <= 0f) Fire();
            }
            else
            {
                DecaySpread();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) EquipFromDef(debugWeapon);
            if (Input.GetKeyDown(KeyCode.Alpha2)) EquipFromDef(debugWeapon2);

            // Debug reload: swap current mag with spare. Swapping back preserves round count.
            if (Input.GetKeyDown(KeyCode.R) && _debugSpareMag != null)
                _debugSpareMag = Reload(_debugSpareMag);
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Equip a weapon instance. Updates effective stats, resets bloom,
        /// and swaps the animator override controller if one is assigned.
        /// </summary>
        public void Equip(WeaponInstance weapon)
        {
            _weapon = weapon;
            StatCalculator.Recalculate(_weapon);

            _consecutiveShots     = 0;
            _shotDecayAccumulator = 0f;
            _currentSpread        = _weapon.effectiveStats.spread;
            SpreadChanged?.Invoke(_currentSpread);

            if (armsAnimator == null) return;

            if (weapon.def is WeaponSO weaponDef && weaponDef.animatorOverride != null)
                armsAnimator.runtimeAnimatorController = weaponDef.animatorOverride;
            else
                Debug.LogWarning("[WeaponController] Weapon has no AnimatorOverrideController — skipping swap.", this);
        }

        /// <summary>
        /// Swap in a new magazine. Returns the old magazine so the caller
        /// can place it back into inventory (or hold it as a spare).
        /// </summary>
        public MagazineInstance? Reload(MagazineInstance newMag)
        {
            if (_weapon == null) return null;

            MagazineInstance? oldMag = _weapon.currentMagazine;
            _weapon.currentMagazine  = newMag;
            StatCalculator.Recalculate(_weapon);
            armsAnimator?.SetTrigger("Reload");

            int rounds = newMag.currentRounds;
            int cap    = (newMag.def as MagazineSO)?.capacity ?? rounds;
            Debug.Log($"[WeaponController] Reloaded — {rounds}/{cap} rounds.");
            return oldMag;
        }

        // ── Private ───────────────────────────────────────────────────────

        private void EquipFromDef(WeaponSO? def)
        {
            if (def == null) return;
            Equip(new WeaponInstance { itemId = def.ItemId, def = def });
        }

        private static MagazineInstance CreateMagInstance(MagazineSO magDef)
        {
            MagazineInstance inst = new MagazineInstance { itemId = magDef.ItemId, def = magDef };
            if (magDef.compatibleAmmo.Count > 0)
                inst.Load(magDef.compatibleAmmo[0], magDef.capacity);
            return inst;
        }

        private void Fire()
        {
            if (_weapon?.def is not WeaponSO weaponDef) return;

            if (weaponDef.fireBehavior == null)
            {
                Debug.LogWarning("[WeaponController] No FireBehavior assigned on weapon.", this);
                return;
            }

            // ── Magazine check ────────────────────────────────────────────
            if (!(_weapon.currentMagazine?.currentRounds > 0))
            {
                Debug.Log("[WeaponController] Click — magazine empty or not loaded.");
                armsAnimator?.SetTrigger("EmptyClick");
                _fireTimer = 0.25f; // brief cooldown so click doesn't spam every frame
                return;
            }

            // ── Fire rate gating ──────────────────────────────────────────
            float shotsPerSecond = _weapon.effectiveStats.fireRate / 60f;
            _fireTimer            = shotsPerSecond > 0f ? 1f / shotsPerSecond : 0f;
            _shotDecayAccumulator = 0f;

            // ── Ammo consumption ──────────────────────────────────────────
            AmmoSO? ammo = _weapon.currentMagazine!.Consume();

            // ── Bloom ─────────────────────────────────────────────────────
            _consecutiveShots++;
            _currentSpread = _weapon.effectiveStats.spread
                           * weaponDef.bloomCurve.Evaluate(_consecutiveShots);
            SpreadChanged?.Invoke(_currentSpread);

            // ── Raycast — aim direction from camera for proper FPS feel ───
            Camera?   cam    = Camera.main;
            Transform muzzle = muzzlePoint != null ? muzzlePoint : transform;
            Vector3   dir    = cam != null ? cam.transform.forward : muzzle.forward;

            weaponDef.fireBehavior.Fire(_weapon, muzzle.position, dir, ammo);
        }

        private void DecaySpread()
        {
            float baseSpread = _weapon?.effectiveStats.spread ?? 0f;
            _currentSpread   = Mathf.Lerp(_currentSpread, baseSpread, Time.deltaTime * SpreadDecaySpeed);

            if (_consecutiveShots > 0)
            {
                _shotDecayAccumulator += Time.deltaTime * ShotDecayRate;
                while (_shotDecayAccumulator >= 1f && _consecutiveShots > 0)
                {
                    _consecutiveShots--;
                    _shotDecayAccumulator -= 1f;
                }
            }
            else
            {
                _shotDecayAccumulator = 0f;
            }

            SpreadChanged?.Invoke(_currentSpread);
        }
    }
}

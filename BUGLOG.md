# BUGLOG — collected findings (audit 2026-06-11)

Findings from the read-only audit pass. Nothing here has been fixed yet — this is the worklist for the upcoming bug-fix pass. UI scripts are excluded (rewritten during the HUD overhaul).

## HIGH

- [ ] **EnemyBrain.cs:158-160** — Subscribes to `_health.OnDeath`, `_health.OnDamaged`, `_perception.OnPerceptionEvent` in Awake, never unsubscribes → leaked delegates every enemy spawn/destroy cycle. Fix: subscribe OnEnable, unsubscribe OnDisable.
- [ ] **EnemyRagdoll.cs** — Subscribes `_health.OnDeath` in Awake, no unsubscribe anywhere. Same fix as above.
- [x] **StimulusSystem.cs:57** — FIXED 2026-06-12: pooled snapshot lists (Stack pool, try/finally return) — zero steady-state allocation, reentrancy preserved.
- [ ] **WeaponManager.cs:103** — `FindObjectsOfType<GunController>(true)` is obsolete in Unity 6. Fix: `FindObjectsByType<GunController>(FindObjectsInactive.Include, FindObjectsSortMode.None)`. Also note: it deactivates any GunController not in `_initialWeapons` — fragile with additive scenes.
- [ ] **SaveSystem.cs:100** — `File.WriteAllText` unguarded; any IOException (disk full, AV lock) throws unhandled mid-save. Fix: try/catch + LogError + failure result.
- [ ] **TraderSaveAdapter.cs:36** — No null check on the deserialized DTO; malformed save JSON → NPE and trader stock silently lost. Fix: null-guard like InventorySaveAdapter does.
- [ ] **GunController.cs:103** — static `Registry` dictionary never cleared on scene unload → stale entries to destroyed guns. Fix: remove entry in OnDestroy.

## MEDIUM

- [ ] **EnemyBrain.cs:150** — obsolete + expensive `FindObjectOfType<PlayerHealth>()` fallback. Fix: use `RunManager.Instance` path (pattern already used by MonsterAI).
- [x] **PlayerMotor.cs:748-749** — FIXED 2026-06-12: visual child cached lazily in LateUpdate (`_cachedVisual`), re-fetches only if null.
- [ ] **GunController.cs:523-554** — casing `Instantiate` per shot, no pooling. Fix: small ring-buffer pool.
- [ ] **GunController.cs:406-414** — three `new WaitForSeconds` per reload. Fix: cache.
- [ ] **EnemyWeaponDriver.cs:217** — `new WaitForSeconds` per enemy reload. Fix: cache.
- [ ] **EnemyBrain.cs:295/317/655/668** — uncached `WaitForSeconds` in patrol/cover loops. Fix: cache.
- [ ] **PlayerInteractor.cs:93** — `GetComponentInParent<IInteractable>()` every frame while aiming at something. Fix: only re-resolve when hit collider changes.
- [ ] **QuestManager.cs** — `EvaluateAll()` runs on every WSM write. Fix: dirty-flag + evaluate once per frame, or key filtering.
- [ ] **LowBatteryWarning.cs:108-114** — frequent `WaitForSeconds` allocs during flicker. Fix: timer loop with `yield return null`.

## LOW

- [ ] **PlayerHealth.cs:14** — serialized `_currentHealth` is overwritten at runtime; misleading in Inspector. Mark HideInInspector.
- [ ] **RecoilController.cs:76-79, GunSway.cs:283-288** — `Lerp(a, b, speed * dt)` is framerate-dependent. Fix: exponential decay form.
- [ ] **WeaponSwitcher.cs:50-56** — `GetCurrentIndex()` computed twice per scroll. Minor.
- [ ] **EnemySpawnSystem.cs:139** — lambda subscription to `OnDeath` can't be unsubscribed; fine until pooling is introduced.
- [ ] **QuestManager.cs:68-72 / WorldStateTimer.cs:41-42** — redundant double SaveSystem registration (OnEnable + Start). Harmless, confusing.
- [ ] **GunController.cs:147** — `OnReloadRequested` is a plain delegate field (not `event`); a second subscriber would silently replace the first. Intentional today, latent trap.
- [ ] **FlashlightSlot.cs:295** — `OnDepleted` fires on normal unequip too (semantic mismatch for listeners expecting "charge ran out").
- [ ] **EncumbranceSystem.cs:56-61** — OnEnable early-return before Start leaves a small unguarded init window. Fragile, unlikely to bite.

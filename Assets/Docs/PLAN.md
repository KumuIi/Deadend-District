# Deadend District — Master Implementation Plan

## How to Use This Plan

1. Open the wave doc for your current item (links in the checklist below).
2. Read the feature section: **How to Build**, **Leave Space For**, **Watch Out For**.
3. Implement.
4. Run every check listed in `RULEBOOK.md` for that item before ticking it off.
5. Tick the checkbox, move to the next item.
6. At the end of each wave, run the **Wave-End Milestone Check** in the wave doc before starting the next wave.

> **Rule:** Never start a wave until all items in the previous wave are checked. The waves are dependency-ordered — skipping ahead causes rework.

---

## Global Ordered Checklist

### Wave 0 — Contracts  
*Doc: [`waves/wave-0.md`](waves/wave-0.md)*

- [x] **[W0-01]** `IBatteryDrainer` interface
- [x] **[W0-02]** `IDamageable` / `IHitReceiver` interface
- [x] **[W0-03]** `IRunResettable` — extend `ISaveable` with `RunScopeTag`
- [x] **[W0-04]** `IRunLifecycleListener` interface
- [x] **[W0-05]** `PlayerStatModifier` struct + modifier stack on `PlayerMotor` and `PlayerHealth`
- [x] **[W0-06]** `DamageContext` struct
- [x] **[W0-07]** `ILootContainer` interface
- [x] **[W0-08]** `IVisibilityContributor` interface
- [x] **[W0-09]** `IEquipmentSlot` + `EquipmentController` skeleton
- [x] **[W0-10]** `IFactionProvider` / `TeamId` enum
- [x] **[W0-11]** `LootPoolSO` ScriptableObject
- [x] **[W0-12]** `RunScopeTag` enum + scope-aware save methods on `SaveSystem`
- [x] **[W0-13]** `IPoolableSpawnedEntity` interface
- [x] **[W0-14]** WSM key pre-registration workflow — add `WsmKeyRegistrySO` entries for all keys used in Waves 1–2 before writing a single line of system code

---

### Wave 1 — The Pressure  
*Doc: [`waves/wave-1.md`](waves/wave-1.md)*

- [x] **[W1-01]** `EncumbranceSystem`
- [x] **[W1-02]** Stamina integration into `PlayerHealth` (NOT a new MonoBehaviour)
- [x] **[W1-03]** `BatteryItemSO` + `BatteryItemInstance`
- [x] **[W1-04]** `BatterySystem`
- [x] **[W1-05]** `LightSource` component + `ILightSource` interface
- [x] **[W1-06]** `DarknessState` — gameplay writer (WSM) + visual observer (post-process) kept separate
- [x] **[W1-07]** HUD additions — battery bar, stamina bar, weight indicator
- [x] **[W1-08]** `LowBatteryWarning` (SECONDARY pulled early — 1 day, unlocks battery feel testing)
- [x] **[W1-09]** `BaseEnemyAI` abstract class
- [x] **[W1-10]** `GuardAI` (extends `BaseEnemyAI`)
- [x] **[W1-11]** `EnemySpawnPoint` component
- [x] **[W1-12]** NavMesh base bake for test level + layer mask documentation pass

---

### Wave 2 — The Loop  
*Doc: [`waves/wave-2.md`](waves/wave-2.md)*

- [x] **[W2-01]** `SceneTransitionManager`
- [x] **[W2-02]** Hub/Bar scene setup
- [x] **[W2-03]** `RunManager` singleton + run state machine
- [x] **[W2-04]** `ExtractionPoint` (IInteractable)
- [x] **[W2-05]** `Death/FailHandling` in `RunManager`
- [x] **[W2-06]** `SaveSystem` scope-aware operations wired to `RunManager` events
- [x] **[W2-07]** `MenuSystem` — minimal pause + main menu (GameInputState.Block integration)
- [x] **[W2-08]** `StashSystem` + `StashSaveAdapter`
- [x] **[W2-09]** `LootSpawnSystem` + `LootSpawnPoint`
- [x] **[W2-10]** `CurrencySystem` (`CurrencyService` + WSM mirror key `economy.credits`)
- [x] **[W2-11]** `RechargeStation` (IInteractable)
- [x] **[W2-12]** `HotbarSystem` (completes `WeaponSwitcher` stub)
- [x] **[W2-13]** `TraderSystem` (`TraderSO` + `TraderUI` using `ILootContainer`)

---

### Wave 3 — The Threat  
*Doc: [`waves/wave-3.md`](waves/wave-3.md)*

- [x] **[W3-01]** `NoiseProfileSO` + `NoiseEmitter` component
- [x] **[W3-02]** `PlayerVisibility` component + `VisibilitySystem`
- [x] **[W3-03]** `MonsterAI` (extends `BaseEnemyAI`)
- [x] **[W3-04]** `EnemySpawnSystem`
- [x] **[W3-05]** `HitZone` component + `GunController` migration to `DamageContext` — *code-done; needs HitZone components on enemy body-part colliders (see wave-3.md note)*
- [x] **[W3-09]** Weapon/ammo state persistence + ammo economy — *code-done.* **Design note:** no separate `WeaponStateSaveAdapter` was needed — the live `GunController` shares its `MagazineInstance` with the inventory `WeaponItemInstance.LoadedMagazine` (`GunController.InsertMagazine` stores by reference), so persisting the inventory grid captures equipped-weapon ammo for free. There is no separate "chambered round" model in this codebase (rounds live only in the magazine). Implemented: (1) `InventoryGrid.GridSaveEntry` extended with `ammoCount` + `MagState` (mag SO + ordered round SO names) for grid-placed mags AND a weapon's loaded mag; `GetSaveData`/`LoadFromSaveData` capture/restore it — partial mags now survive save/load. (2) `MagazineInstance.Rounds`/`RestoreRounds`. (3) `AmmunitionSO.pricePerRound` + `EffectivePricePerRound` (falls back to `sellValue/stackSize`); `AmmoItemInstance.StackSellValue` = per-round × count. (4) `AmmoItemInstance.AddRounds` (stack to cap) + `Split(amount)` (right-click-split helper). **Needs wiring:** set `pricePerRound` on ammo SOs; hook `Split`/`AddRounds` into the inventory right-click menu; TraderSystem should use `StackSellValue` for ammo instead of flat `sellValue`.
- [x] **[W3-06]** `LadderClimbing` — `Ladder : IInteractable` + `PlayerMotor` ladder mode — *code-done; build ladder prefab (bottom/top points + trigger), firing blocked while climbing*
- [x] **[W3-07]** NavMesh links for **mimic-only** ladder traversal — *code-done (NavAreas + guard areaMask exclusion); needs `LadderClimb` NavMesh area + NavMeshLink per ladder (see wave-3.md)*
- [x] **[W3-08]** `FallDamage` — velocity threshold → `DamageContext` → `IDamageable` — *code-done; add FallDamage to player root, wire PlayerMotor + PlayerHealth*

---

### Wave 4 — Gating & Navigation  
*Doc: [`waves/wave-4.md`](waves/wave-4.md)*

- [ ] **[W4-01]** `KeySO` + `LockedDoor`
- [ ] **[W4-02]** `MapImageSO` + `MapUI` + underground reading mechanic
- [ ] **[W4-03]** `MapBuying` (MapImageSO as TraderSO stock item)
- [ ] **[W4-04]** `StockRestock` (TraderSO + RunManager.OnRunComplete hook)
- [ ] **[W4-05]** `SectorLoading` + `SectorManager` (additive scenes, NavMeshSurface per sector)
- [ ] **[W4-06]** MenuSystem polish — settings screen, save/load screen
- [ ] **[W4-07]** `MidRaidSave` — optional mid-run checkpoint: save player world position + current sector name, restore sector additively on load if save was made during a run *[DECISION PENDING — owner to confirm whether mid-raid saving fits the game's design]*

---

### Wave 5 — Depth  
*Doc: [`waves/wave-5.md`](waves/wave-5.md)*

- [ ] **[W5-01]** `SpatialAudioManager` + AudioMixer setup
- [ ] **[W5-02]** `DialogueSystem` (`DialogueSO` + `DialogueUI`)
- [ ] **[W5-03]** `DocumentCollectible` (`ReadableDocumentSO` + `IInteractable`)
- [ ] **[W5-04]** `JournalSystem` + `JournalUI`
- [ ] **[W5-05]** `AugmentSystem` concrete implementations (`CyberneticSO` subclasses as `IBatteryDrainer` + `PlayerStatModifier`)
- [ ] **[W5-06]** `Melee` (`MeleeWeaponSO` + `MeleeController`)
- [ ] **[W5-07]** `ThrowableSystem` (`FlareItemSO` as `ILightSource`, `DistractionItemSO`)
- [ ] **[W5-08]** `EnemyTypes` (`EnemyTypeSO` + `BaseEnemyAI` variants)
- [ ] **[W5-09]** `HazardZones` (`HazardZoneSO` + trigger + `PlayerStatModifier` resistance)
- [ ] **[W5-10]** `ShortcutSystem` (WSM World-scope flags + `IInteractable` shortcut opener)
- [ ] **[W5-11]** `WallMarking` (decal placement via `IInteractable`)
- [ ] **[W5-12]** `BlockedPaths` (`IInteractable` debris + quest/tool clear condition)
- [ ] **[W5-13]** `KeypadCodeLocks` (code variant of `LockedDoor`)
- [ ] **[W5-14]** `CommitmentDrops` (one-way trigger + WSM flag)
- [ ] **[W5-15]** `MountedLight` + `Headlamp` (`ILightSource` + `IBatteryDrainer` variants)
- [ ] **[W5-16]** `TuningData` (`GameBalanceSO` central config)
- [ ] **[W5-17]** `WeaponMods/Attachments` *[POLISH]*
- [ ] **[W5-18]** `WeaponDurability/Jamming` *[POLISH]*
- [ ] **[W5-19]** `CompassOrLandmarks` *[POLISH]*
- [ ] **[W5-20]** `DistractionMechanic` (throwable AI attention pull) *[POLISH]*
- [ ] **[W5-21]** `Lockpicking` minigame *[POLISH]*
- [ ] **[W5-22]** `Vault/Mantle` *[POLISH]*
- [ ] **[W5-23]** `AmbientAndMusic` *[POLISH]*

---

## Quick-Reference: New Scripts per Wave

| Wave | New Scripts / SOs |
|------|-------------------|
| W0 | IBatteryDrainer, IDamageable, IHitReceiver, IRunResettable, IRunLifecycleListener, PlayerStatModifier, DamageContext, ILootContainer, IVisibilityContributor, IEquipmentSlot, EquipmentController, IFactionProvider, TeamId, LootPoolSO, RunScopeTag, IPoolableSpawnedEntity |
| W1 | EncumbranceSystem, BatteryItemSO, BatteryItemInstance, BatterySystem, ILightSource, LightSource, DarknessStateWriter, DarknessStateVisual, BaseEnemyAI, GuardAI, EnemySpawnPoint |
| W2 | SceneTransitionManager, RunManager, ExtractionPoint, MenuController, MenuSystem, PauseMenu, StashSystem, StashSaveAdapter, LootSpawnPoint, LootSpawnSystem, CurrencyService, RechargeStation, HotbarSystem, TraderSO, TraderSystem, TraderUI |
| W3 | NoiseProfileSO, NoiseEmitter, PlayerVisibility, VisibilitySystem, MonsterAI, EnemySpawnSystem, HitZone, LadderClimbing, Ladder |
| W4 | KeySO, LockedDoor, MapImageSO, MapUI, MapItem, SectorManager, SectorTrigger |
| W5 | DialogueSO, DialogueLine, DialogueUI, ReadableDocumentSO, DocumentPickup, JournalSystem, JournalUI, MeleeWeaponSO, MeleeController, FlareItemSO, DistractionItemSO, ThrowableController, EnemyTypeSO, HazardZoneSO, HazardZone, ShortcutOpener, WallMarker, DebrisPile, KeypadDoor, CommitmentDropTrigger, MountedLight, Headlamp, GameBalanceSO |

---

## WSM Key Registry

Before implementing any system that reads or writes `WorldStateManager`, add the key to `WsmKeyRegistrySO` first.

Pre-planned keys (add these to the registry in Wave 0):

| Key | Type | Category | Written By |
|-----|------|----------|------------|
| `player.in_darkness` | bool | Player | DarknessStateWriter |
| `player.is_dead` | bool | Player | RunManager |
| `run.active` | bool | Run | RunManager |
| `run.extracted` | bool | Run | ExtractionPoint |
| `economy.credits` | int | Economy | CurrencyService |
| `hub.recharge_station.used` | bool | Hub | RechargeStation |
| `door.{id}.unlocked` | bool | Door | LockedDoor |
| `shortcut.{id}.opened` | bool | Shortcut | ShortcutOpener |
| `sector.{id}.visited` | bool | Sector | SectorManager |
| `npc.{id}.dead` | bool | NPC | WorldStateOnDeath |
| `zone.hub.active` | bool | Zone | Hub NoEnemyZone trigger |

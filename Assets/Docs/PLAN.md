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

- [ ] **[W0-01]** `IBatteryDrainer` interface
- [ ] **[W0-02]** `IDamageable` / `IHitReceiver` interface
- [ ] **[W0-03]** `IRunResettable` — extend `ISaveable` with `RunScopeTag`
- [ ] **[W0-04]** `IRunLifecycleListener` interface
- [ ] **[W0-05]** `PlayerStatModifier` struct + modifier stack on `PlayerMotor` and `PlayerHealth`
- [ ] **[W0-06]** `DamageContext` struct
- [ ] **[W0-07]** `ILootContainer` interface
- [ ] **[W0-08]** `IVisibilityContributor` interface
- [ ] **[W0-09]** `IEquipmentSlot` + `EquipmentController` skeleton
- [ ] **[W0-10]** `IFactionProvider` / `TeamId` enum
- [ ] **[W0-11]** `LootPoolSO` ScriptableObject
- [ ] **[W0-12]** `RunScopeTag` enum + scope-aware save methods on `SaveSystem`
- [ ] **[W0-13]** `IPoolableSpawnedEntity` interface
- [ ] **[W0-14]** WSM key pre-registration workflow — add `WsmKeyRegistrySO` entries for all keys used in Waves 1–2 before writing a single line of system code

---

### Wave 1 — The Pressure  
*Doc: [`waves/wave-1.md`](waves/wave-1.md)*

- [ ] **[W1-01]** `EncumbranceSystem`
- [ ] **[W1-02]** Stamina integration into `PlayerHealth` (NOT a new MonoBehaviour)
- [ ] **[W1-03]** `BatteryItemSO` + `BatteryItemInstance`
- [ ] **[W1-04]** `BatterySystem`
- [ ] **[W1-05]** `LightSource` component + `ILightSource` interface
- [ ] **[W1-06]** `DarknessState` — gameplay writer (WSM) + visual observer (post-process) kept separate
- [ ] **[W1-07]** HUD additions — battery bar, stamina bar, weight indicator
- [ ] **[W1-08]** `LowBatteryWarning` (SECONDARY pulled early — 1 day, unlocks battery feel testing)
- [ ] **[W1-09]** `BaseEnemyAI` abstract class
- [ ] **[W1-10]** `GuardAI` (extends `BaseEnemyAI`)
- [ ] **[W1-11]** `EnemySpawnPoint` component
- [ ] **[W1-12]** NavMesh base bake for test level + layer mask documentation pass

---

### Wave 2 — The Loop  
*Doc: [`waves/wave-2.md`](waves/wave-2.md)*

- [ ] **[W2-01]** `SceneTransitionManager`
- [ ] **[W2-02]** Hub/Bar scene setup
- [ ] **[W2-03]** `RunManager` singleton + run state machine
- [ ] **[W2-04]** `ExtractionPoint` (IInteractable)
- [ ] **[W2-05]** `Death/FailHandling` in `RunManager`
- [ ] **[W2-06]** `SaveSystem` scope-aware operations wired to `RunManager` events
- [ ] **[W2-07]** `MenuSystem` — minimal pause + main menu (GameInputState.Block integration)
- [ ] **[W2-08]** `StashSystem` + `StashSaveAdapter`
- [ ] **[W2-09]** `LootSpawnSystem` + `LootSpawnPoint`
- [ ] **[W2-10]** `CurrencySystem` (`CurrencyService` + WSM mirror key `economy.credits`)
- [ ] **[W2-11]** `RechargeStation` (IInteractable)
- [ ] **[W2-12]** `HotbarSystem` (completes `WeaponSwitcher` stub)
- [ ] **[W2-13]** `TraderSystem` (`TraderSO` + `TraderUI` using `ILootContainer`)

---

### Wave 3 — The Threat  
*Doc: [`waves/wave-3.md`](waves/wave-3.md)*

- [ ] **[W3-01]** `NoiseProfileSO` + `NoiseEmitter` component
- [ ] **[W3-02]** `PlayerVisibility` component + `VisibilitySystem`
- [ ] **[W3-03]** `MonsterAI` (extends `BaseEnemyAI`)
- [ ] **[W3-04]** `EnemySpawnSystem`
- [ ] **[W3-05]** `HitZone` component + `GunController` migration to `DamageContext`
- [ ] **[W3-06]** `LadderClimbing` — `Ladder : IInteractable` + `PlayerMotor` ladder mode
- [ ] **[W3-07]** NavMesh off-mesh links for enemy ladder traversal
- [ ] **[W3-08]** `FallDamage` — velocity threshold → `DamageContext` → `IDamageable`

---

### Wave 4 — Gating & Navigation  
*Doc: [`waves/wave-4.md`](waves/wave-4.md)*

- [ ] **[W4-01]** `KeySO` + `LockedDoor`
- [ ] **[W4-02]** `MapImageSO` + `MapUI` + underground reading mechanic
- [ ] **[W4-03]** `MapBuying` (MapImageSO as TraderSO stock item)
- [ ] **[W4-04]** `StockRestock` (TraderSO + RunManager.OnRunComplete hook)
- [ ] **[W4-05]** `SectorLoading` + `SectorManager` (additive scenes, NavMeshSurface per sector)
- [ ] **[W4-06]** MenuSystem polish — settings screen, save/load screen

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

# Wave 2 — The Loop

**What this wave delivers:** The expedition rhythm is playable. Hub → run → extract (or die) → hub. Loot spawns, you can sell it to a trader, buy gear, and store valuables. The run-loop stakes are real.

**Prerequisite:** Wave 1 complete. BatterySystem, GuardAI, and EncumbranceSystem all working.

---

## W2-01 — `SceneTransitionManager`

**File:** `Scripts/Core/SceneManagement/SceneTransitionManager.cs`

**How to build:**
- Singleton MonoBehaviour, `DontDestroyOnLoad`.
- `LoadHub()`: fade out → `SceneManager.LoadScene("Hub", LoadSceneMode.Single)` → fade in.
- `LoadSector(string sectorName)`: fade out → `SceneManager.LoadSceneAsync(sectorName, LoadSceneMode.Additive)` → on complete, call `SaveSystem.RestoreAfterSceneLoad(RunScopeTag.Run)` → fade in.
- `UnloadSector(string sectorName)`: trigger `IPoolableSpawnedEntity.OnDespawned()` on all entities in sector → `SceneManager.UnloadSceneAsync`.
- Exposes `OnSceneTransitionStarted` and `OnSceneTransitionFinished` events.

**Leave space for:** `SectorManager` (Wave 4) will call `LoadSector/UnloadSector` as the player moves through the world. For now, call them directly from `RunManager`.

**Watch out for:** `SceneManager.sceneLoaded` fires before `Start()` on new objects. `RestoreAfterSceneLoad` must queue the restore and flush it after all `Start()` calls. Use a one-frame delayed invoke or the `SceneManager.sceneLoaded` + one `yield return null` pattern.

---

## W2-02 — Hub/Bar Scene Setup

**No new MonoBehaviour for this.** Scene setup pass.

**How to build:**
1. Create `Hub.unity` scene.
2. Place a box collider trigger at the hub perimeter tagged with a `WorldStateTriggerVolume` writing `zone.hub.active = true` on enter, `false` on exit.
3. All `BaseEnemyAI` instances check `WorldStateManager.GetBool("zone.hub.active")` in `OnIdle` — if true, disable self (no enemies in hub).
4. Place placeholder NPC GameObjects at trader, recharge station, and quest giver positions (empty GameObject with Sphere gizmo for now).
5. Light the hub warmly to contrast with dark tunnel sectors.

**Watch out for:** The hub uses `SceneManager.LoadScene Single` (replaces all other scenes). Tunnel sectors use `Additive`. Never load hub as additive — it contains the persistent player rig.

---

## W2-03 — `RunManager`

**File:** `Scripts/Core/RunSystem/RunManager.cs`

**How to build:**
- Singleton MonoBehaviour, `DontDestroyOnLoad`.
- State machine: `RunState` enum { InHub, InRun, Extracting, Dead }.
- `List<IRunLifecycleListener> _listeners` — register/unregister from `OnEnable/OnDisable` of implementing MonoBehaviours.
- `StartRun(string sectorName)`: validate in hub → call `SaveSystem.SaveProfile()` + `SaveSystem.SaveWorld()` → call `SceneTransitionManager.LoadSector(sectorName)` → broadcast `OnRunStarted()` to all listeners → state = InRun.
- `TriggerExtract()`: called by `ExtractionPoint`. State = Extracting → call `SaveSystem.SaveProfile()` + `SaveSystem.SaveRun()` → broadcast `OnRunExtracted()` → `SceneTransitionManager.LoadHub()` → broadcast `OnReturnedToHub()` → `SaveSystem.ClearRun()`.
- `TriggerDeath()`: state = Dead → strip run-scoped inventory (keep stash, profile items) → broadcast `OnRunDied()` → `SceneTransitionManager.LoadHub()` → broadcast `OnReturnedToHub()` → `SaveSystem.ClearRun()`.
- Events: `OnRunStarted`, `OnRunExtracted`, `OnRunDied`, `OnReturnedToHub` — delegate/UnityEvent, mirrors `IRunLifecycleListener`.

**Leave space for:** `StockRestock` (W4) subscribes to `OnReturnedToHub`. `EnemySpawnSystem` (W3) subscribes to `OnRunStarted` to populate sector enemies. `DarknessStateWriter` subscribes to `OnRunStarted` to reset the 2-minute timer.

**Watch out for:** Strip run-scoped inventory in `TriggerDeath` by iterating `InventoryGrid` and removing items whose `ItemInstance` is NOT in the stash. Keep `BatteryItemInstance` of type `Rechargeable` (it's profile-scoped). Remove all `OneTime` batteries.

---

## W2-04 — `ExtractionPoint`

**File:** `Scripts/World/ExtractionPoint.cs`

**How to build:**
- `MonoBehaviour : IInteractable`.
- `CanInteract(GameObject g)`: return `RunManager.Instance.State == RunState.InRun`.
- `GetPrompt(GameObject g)`: return `"Extract"`.
- `Interact(GameObject g)`: call `RunManager.Instance.TriggerExtract()`.
- Optional: trigger zone auto-extract if player stands inside for 3 seconds (hold-to-confirm interaction pattern matching the map reading in Wave 4).

**Watch out for:** The extraction point should be on the `Interactable` physics layer so `PlayerInteractor` finds it. Verify layer mask.

---

## W2-05 — Death / Fail Handling

**Already partially in W2-03 (`RunManager.TriggerDeath`).** This item covers `PlayerHealth` hookup.

**How to build:**
- `PlayerHealth.OnDeath` event already exists. Subscribe in `RunManager.Start()`:  
  `playerHealth.OnDeath += () => TriggerDeath();`
- On death: play death animation/sound → brief delay → call `TriggerDeath()`.
- Screen should fade to black before `TriggerDeath` runs (use `SceneTransitionManager`'s fade).

**Watch out for:** `PlayerHealth.OnDeath` might fire multiple times if damage keeps coming in after death (e.g., fall + explosion). Guard with `if (State == RunState.Dead) return;` in `TriggerDeath`.

---

## W2-06 — `SaveSystem` Scope-Aware Wiring to `RunManager`

**Modify:** `Scripts/Core/SaveSystem/SaveSystem.cs` (scope methods built in W0-12, now wired).

**How to build:**
- `RunManager.OnRunStarted` → `SaveSystem.RestoreAfterSceneLoad(RunScopeTag.Run)`.
- `RunManager.OnReturnedToHub` → `SaveSystem.SaveProfile()`, `SaveSystem.SaveWorld()`.
- `RunManager.OnRunExtracted` → `SaveSystem.SaveRun()` (already called inside TriggerExtract — just verify order).
- `RunManager.TriggerDeath` → `SaveSystem.ClearRun()` after stripping inventory.
- On hub load: `SaveSystem.RestoreAfterSceneLoad(RunScopeTag.Profile)`.

**Watch out for:** Save order matters: always `SaveProfile` before `ClearRun`. Verify by logging save/load calls in order during a test run → death cycle.

---

## W2-07 — `MenuSystem` (Minimal — Pause + Main Menu)

**Files:** `Scripts/UI/MenuSystem/MenuController.cs`, `Scripts/UI/MenuSystem/PauseMenu.cs`, `Scripts/UI/MenuSystem/MainMenu.cs`

**How to build:**
- `MenuController`: manages which menu panel is active. Calls `GameInputState.Block()` on open, `GameInputState.Unblock()` on close. Handles Escape key to open pause.
- `PauseMenu`: Resume, Settings (stub), Save & Quit buttons. "Save & Quit" calls `SaveSystem.SaveProfile()` + `SaveSystem.SaveRun()` + `SceneTransitionManager.LoadHub()`.
- `MainMenu`: New Game, Continue, Quit. "New Game" calls `RunManager.StartRun` with the first sector. "Continue" calls `SaveSystem` to load the last profile save and go to hub.

**Leave space for:** Settings screen (Wave 4 polish) adds audio volume, graphics quality, key rebinding. For now, the settings button can open an empty panel.

**Watch out for:** `Time.timeScale = 0` for pause — verify `BatterySystem.Update` and all timers use `Time.deltaTime` (scaled). If any system uses `Time.unscaledDeltaTime`, it will keep running during pause. Audit now.

---

## W2-08 — `StashSystem` + `StashSaveAdapter`

**Files:** `Scripts/Inventory/StashSystem.cs`, `Scripts/Core/SaveSystem/Adapters/StashSaveAdapter.cs`

**How to build:**
- `StashSystem`: a second `InventoryGrid` instance. Hub-only access (check `zone.hub.active` WSM). Opens alongside player inventory panel when interacting with the stash chest.
- `StashSaveAdapter : ISaveable` with `RunScopeTag.Profile` — stash persists through death.
- `ILootContainer` implementation on `StashSystem` so the same UI panel code works.
- UI: use the existing `InventoryUI` pattern — the stash is just a grid, not a new UI system.

**Watch out for:** Dragging between player inventory and stash: the existing `InventoryDragController` must handle drops onto a second grid. Extend it to accept a `targetGrid` parameter, or make it look up which grid a cell belongs to.

---

## W2-09 — `LootSpawnSystem` + `LootSpawnPoint`

**Files:** `Scripts/World/LootSpawnPoint.cs`, `Scripts/World/LootSpawnSystem.cs`

**How to build:**

`LootSpawnPoint`:
- Fields: `LootPoolSO poolSO`, `float spawnChance [0..1]`, `bool isHardSpawn`, `ItemSO fixedItem` (for hard spawns), `bool hasSpawned` (Temp-scoped — reset per run).
- `TrySpawn()`: if `isHardSpawn`, spawn `fixedItem`. Else: roll `Random.value < spawnChance`, if true → `poolSO.Roll()` → `ItemDropSpawner.TryDrop(item, transform.position, Vector3.up, 0)` (zero throw force = just place it).
- Implements `IRunLifecycleListener`: `OnRunStarted()` resets `hasSpawned = false`.

`LootSpawnSystem`:
- MonoBehaviour in each sector scene, collects all `LootSpawnPoint` in scene on `Start`.
- `OnRunStarted()`: calls `TrySpawn()` on each point.
- `IRunLifecycleListener` registration.

**Leave space for:** Each spawn point references a `LootPoolSO` asset. Designers create pools in the project and drag them onto spawn points. The same `LootPoolSO` can be used on 10 spawn points in 3 different sectors — one edit changes all of them.

---

## W2-10 — `CurrencySystem`

**File:** `Scripts/Core/Economy/CurrencyService.cs`

**How to build:**
```csharp
public static class CurrencyService
{
    private const string Key = "economy.credits";
    public static int GetCredits() => WorldStateManager.Instance.GetInt(Key);
    public static bool CanAfford(int amount) => GetCredits() >= amount;
    public static void Add(int amount) { WorldStateManager.Instance.SetInt(Key, GetCredits() + amount); }
    public static bool Spend(int amount) {
        if (!CanAfford(amount)) return false;
        WorldStateManager.Instance.SetInt(Key, GetCredits() - amount);
        return true;
    }
}
```

`CurrencyService` is a static helper — no MonoBehaviour. WSM holds the value (profile-scoped via `WorldStateSaveAdapter`).

**Watch out for:** `WorldStateSaveAdapter` saves all WSM keys, including `economy.credits`. This is already profile-scoped if you tag `WorldStateSaveAdapter` with `RunScopeTag.Profile`. Verify the tag.

---

## W2-11 — `RechargeStation`

**File:** `Scripts/World/RechargeStation.cs`

**How to build:**
- `MonoBehaviour : IInteractable`.
- `CanInteract(GameObject g)`: return `true` if player has a `BatteryItemInstance` of type `Rechargeable` (check InventoryGrid).
- `GetPrompt`: `"Recharge Battery"`.
- `Interact`: `BatterySystem.Instance.RefillRechargeable()`. Write WSM `hub.recharge_station.used = true`. Play recharge audio.

This is ~30 lines. Quick win.

---

## W2-12 — `HotbarSystem` (completes `WeaponSwitcher` stub)

**File:** Modify `Scripts/Gun/WeaponSwitcher.cs`

**How to build:**
- Enable the `Update()` body (it was intentionally left empty).
- Keys 1–4: `if (Input.GetKeyDown(KeyCode.Alpha1)) EquipAt(0);` etc.
- Scroll wheel: `if (Input.mouseScrollDelta.y != 0) EquipAt((GetCurrentIndex() + sign + count) % count);`
- Check `GameInputState.GameplayBlocked` before any input reads.
- Optionally: `EquipmentController.GetSlot("weapon_primary")` integration for future two-weapon setup.

**Leave space for:** When `MountedLight` (Wave 5) is implemented, equipping a two-handed weapon automatically activates it. The hotbar can fire an `OnWeaponEquipped(WeaponSO)` event that `MountedLight` listens to.

---

## W2-13 — `TraderSystem`

**Files:** `Scripts/Economy/TraderSO.cs`, `Scripts/Economy/TraderSystem.cs`, `Scripts/UI/TraderUI.cs`

**How to build:**

`TraderSO : ScriptableObject`:
```csharp
[Serializable] public struct TraderStockEntry { public ItemSO Item; public int BuyPrice; public int SellPriceMult; public int StockCount; }
public TraderStockEntry[] Stock;
public int RestockIntervalRuns; // 0 = never restocks
public string TraderName;
public Sprite TraderPortrait; // For DialogueSystem later
```

`TraderSystem : MonoBehaviour, IInteractable, ILootContainer, IRunLifecycleListener`:
- `IInteractable.Interact`: calls `GameInputState.Block()`, opens `TraderUI`.
- `ILootContainer` implementation wraps `Stock` entries.
- `IRunLifecycleListener.OnReturnedToHub`: decrement restock counter, restock if 0.

`TraderUI`:
- Two columns: trader stock (left), player inventory (right) using existing `InventoryUI`.
- Buy: `CurrencyService.Spend(buyPrice)` → `ItemInstanceFactory.Create(item)` → `InventoryGrid.TryPlace(instance)`.
- Sell: `InventoryGrid.Remove(instance)` → `CurrencyService.Add(sellPrice)`.
- Close: `GameInputState.Unblock()`.

**Leave space for:** `DialogueSystem` (Wave 5) will add a portrait + greeting line before the shop opens. The `TraderSO.TraderPortrait` field is already there waiting.

**Watch out for:** "Buy price" is what the player pays. "Sell price" is what the player gets. Make sure the spread is clear in code comments and the UI labels.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 2"

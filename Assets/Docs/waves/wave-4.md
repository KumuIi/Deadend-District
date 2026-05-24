# Wave 4 — Gating & Navigation

**What this wave delivers:** The world has structure. Locked doors create meaningful choices. Maps give the explorer an advantage. Quests lead you to keys. Sectors load and unload without breaking NavMesh or saves.

**Prerequisite:** Wave 3 complete. Enemies work, fall damage works, ladders work.

---

## W4-01 — `KeySO` + `LockedDoor`

**Files:** `Scripts/Items/KeySO.cs`, `Scripts/World/LockedDoor.cs`

**How to build:**

`KeySO : ItemSO`:
- Fields: `string targetDoorId` (matches a door's ID field), `bool singleUse`.
- Grid size: 1×1. No model needed initially (use a placeholder color).

`LockedDoor : MonoBehaviour, IInteractable`:
- Field: `string doorId` (set in Inspector, unique per door).
- `CanInteract(g)`: return `!WorldStateManager.GetBool($"door.{doorId}.unlocked")`.
- `GetPrompt(g)`: player has matching key? `"Unlock Door [Key Name]"` : `"Locked"`.
- `Interact(g)`:
  1. Search `InventoryGrid` for `KeySO` where `targetDoorId == doorId`.
  2. If not found: play "locked" audio, return.
  3. Found: if `key.singleUse`, remove from inventory. `WorldStateManager.SetBool($"door.{doorId}.unlocked", true)`. Play unlock animation/sound. Disable collider.
- `Start()`: if WSM already has `door.{doorId}.unlocked = true`, open immediately (handles scene reload state restore).

**Leave space for:** `KeypadCodeLocks` (Wave 5) is a subclass variant — `override Interact` with a `KeypadUI.Open()` call instead of key inventory check.

**Watch out for:** Register `door.{doorId}.unlocked` keys in `WsmKeyRegistrySO` before creating doors in the scene. Use a consistent naming convention. Each door needs a unique `doorId`.

---

## W4-02 — `MapImageSO` + `MapUI` + Underground Reading

**Files:** `Scripts/World/MapImageSO.cs`, `Scripts/Items/MapItem.cs`, `Scripts/UI/MapUI.cs`

**How to build:**

`MapImageSO : ScriptableObject`:
- Fields: `Sprite mapImage`, `string sectorName`, `string displayName`.

`MapItem : ItemSO`:
- References `MapImageSO`. Grid size 1×2.
- When "Examine" is selected in context menu: open `MapUI` and display the map.

`MapUI : MonoBehaviour`:
- `Open(MapImageSO map)`: display the sprite full-screen with zoom (scroll wheel) and pan (middle mouse).
- **Hub reading**: instant access.
- **Underground reading**: require a 3-second hold. During the hold: check `PlayerMotor.IsSprinting || PlayerMotor.IsMoving`. If player moves, cancel the read. Show a progress bar.
- On open: `GameInputState.Block()`. On close: `GameInputState.Unblock()`.

`Context menu integration`: add "Read Map" action to `InventoryContextMenu` for items that have a `MapImageSO`.

**Leave space for:** `MapBuying` (W4-03) adds `MapImageSO` to trader stock. `QuestSystem` can award map fragments as quest rewards — the `MapItem` is just an `ItemSO` like any other.

**Watch out for:** The underground reading hold must survive `GameInputState` checking. The 3-second hold is triggered while the inventory is open (map is clicked in grid). The read is separate from the generic `IInteractable` interaction — it's a context-menu action, not a world raycast.

---

## W4-03 — `MapBuying`

**No new script.** Data setup only.

**How to build:**
- Create `MapItem` `ItemSO` assets for each sector map (one per sector).
- Add them to the appropriate `TraderSO.Stock[]` entries with buy prices.
- The trader then sells maps — player buys, item goes to inventory, player reads it.

---

## W4-04 — `StockRestock`

**No new script.** Add to `TraderSystem`.

**How to build:**
- `TraderSO`: add `int restockIntervalRuns` (0 = never).
- `TraderSystem` implements `IRunLifecycleListener`:
  - Track `int _runsSinceRestock` (run-scoped counter, persisted in `TraderSystem`'s save adapter).
  - `OnReturnedToHub()`: `_runsSinceRestock++`. If `>= restockIntervalRuns`: reset stock counts to `TraderSO` defaults, `_runsSinceRestock = 0`.
- `TraderSaveAdapter : ISaveable` with `RunScopeTag.Profile` — restock counter and current stock levels persist.

**Watch out for:** Stock count decreases when player buys. When restocking, restore to `TraderSO` defaults — do not accumulate. A trader selling 5 flashlights should have 5 after restock, not 5 + remaining from last run.

---

## W4-05 — `SectorLoading` + `SectorManager`

**Files:** `Scripts/Core/SceneManagement/SectorManager.cs`, `Scripts/World/SectorTrigger.cs`

**How to build:**

`SectorTrigger : MonoBehaviour`:
- Trigger collider at sector boundary.
- Fields: `string sectorToLoad`, `string sectorToUnload`.
- `OnTriggerEnter(Collider c)`: if player → `SectorManager.Instance.Transition(sectorToLoad, sectorToUnload)`.

`SectorManager : MonoBehaviour` (singleton):
- Tracks `_loadedSectors: HashSet<string>`.
- `Transition(load, unload)`:
  1. Despawn all `IPoolableSpawnedEntity` in `unload` sector.
  2. `SceneManager.UnloadSceneAsync(unload)`.
  3. `SceneManager.LoadSceneAsync(load, Additive)`.
  4. On load complete: `SaveSystem.RestoreAfterSceneLoad(RunScopeTag.Temp)` for that sector.
  5. Re-bake NavMesh OR use pre-baked `NavMeshSurface` per sector (preferred).

**NavMeshSurface per sector:**
- Each sector scene has its own `NavMeshSurface` component on the root.
- Set `collectGeometry = RenderMeshes` and `useGeometry = PhysicsColliders`.
- Do NOT use global NavMesh bake in this setup — it won't survive additive load/unload.
- `NavMeshSurface.BuildNavMesh()` at scene load if pre-baking isn't feasible. Pre-baking is preferred for performance.

**Leave space for:** Enemy spawn points in each sector will register with `EnemySpawnSystem` on scene load via `OnEnable`. They unregister on sector unload via `IPoolableSpawnedEntity.OnDespawned()`.

**Watch out for:** `NavMeshAgent` will lose its path when its sector scene is unloaded. Despawn enemies before unloading their scene. `EnemySpawnSystem.OnReturnedToHub()` handles this — verify the order: despawn → unload, NOT unload → despawn.

---

## W4-06 — MenuSystem Polish (Settings + Save/Load Screen)

**Modify:** `Scripts/UI/MenuSystem/`

**How to build:**
- `SettingsMenu`: audio volume sliders → write to `AudioMixer` parameter. Graphics quality dropdown → `QualitySettings.SetQualityLevel`. Key rebinding (stub for now — full implementation is POLISH in Wave 5).
- Settings persist via `PlayerPrefs` (this is acceptable for settings — not gameplay state).
- `SaveLoadScreen`: in main menu, shows list of save files (by date) for Continue. Shows last played sector. "Delete Save" button.

**Watch out for:** `Time.timeScale` must be restored on settings close if the settings are accessed from the pause menu. Track whether settings were opened from pause or main menu and restore appropriately.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 4"

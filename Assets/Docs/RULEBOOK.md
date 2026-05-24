# Deadend District — AI Implementation Rulebook

This rulebook defines the checks an AI (or developer) must run after completing each item in `PLAN.md` before marking it done.

---

## How to Use

After implementing a feature:

1. Run the **Universal Checks** (apply to every single item).
2. Run the **Category Checks** for the type of thing you built (interface, SO, MonoBehaviour, UI, AI, etc.).
3. Run the **Wave-End Checks** if you just completed the last item in a wave.
4. Only then tick the checkbox in `PLAN.md`.

If any check fails, fix it before proceeding. Do not move to the next item with a known outstanding issue.

---

## Universal Checks (run after every item)

- [ ] **WSM Keys** — Did this feature read or write any `WorldStateManager` key?  
  If yes: confirm the key exists in `WsmKeyRegistrySO`. If not, add it before committing.

- [ ] **No duplicated authority** — Is there exactly one script that owns each piece of state?  
  (e.g., stamina lives in `PlayerHealth`, not split across `PlayerHealth` + a new `StaminaSystem` MonoBehaviour)

- [ ] **Interfaces used** — Did you use an existing interface instead of a direct type reference where one fits?  
  - Damage → `IDamageable`  
  - Battery consumption → `IBatteryDrainer`  
  - World interaction → `IInteractable`  
  - Container/loot access → `ILootContainer`  
  - Stat modification → `PlayerStatModifier` stack (not direct field mutation)  
  - Noise broadcast → `StimulusSystem.Broadcast` (not a custom event)  
  - Run events → `IRunLifecycleListener` (not polling `RunManager` in `Update`)

- [ ] **No `FindObjectOfType` in production code** — Use injected references, singletons via `Instance`, or event registration.

- [ ] **Save scope tagged** — If you implemented `ISaveable`, did you set `RunScopeTag` correctly?  
  Profile = persists across all runs. Run = resets on death/extract. World = major persistent flags. Temp = resets on sector reload.

- [ ] **DontDestroyOnLoad guard** — If this is a singleton MonoBehaviour, does it destroy duplicate instances on `Awake`?

- [ ] **GameInputState respected** — If this feature takes any input, does it check `GameInputState.GameplayBlocked` before acting?

- [ ] **Layer mask set** — Any raycast or physics query has an explicit layerMask, not `Physics.DefaultRaycastLayers`.

---

## Category Checks

### Interface / Struct (W0 items)

- [ ] Interface is in its own file under `Scripts/Core/` or `Scripts/Contracts/`.
- [ ] No MonoBehaviour dependencies — interfaces must be pure C# contracts.
- [ ] All planned implementors are listed as a comment on the interface (so future devs know the intended users).
- [ ] Struct is readonly where possible; no hidden heap allocation in hot paths.

### ScriptableObject

- [ ] Has `[CreateAssetMenu]` with a clear `menuName`.
- [ ] No runtime mutable state on the SO — all mutable state lives in a paired runtime instance or MonoBehaviour.
- [ ] SO has editor `OnValidate` for obvious bad configurations (nulls, out-of-range values).
- [ ] If it's a loot/data definition, it references `ItemSO` or subclasses — not raw GameObjects.

### MonoBehaviour (gameplay system)

- [ ] Registers with `SaveSystem` in `Start()` if it implements `ISaveable`.
- [ ] Unregisters in `OnDisable`.
- [ ] Registers `IRunLifecycleListener` in `OnEnable` if it needs run event hooks.
- [ ] If it pushes `PlayerStatModifier` entries: removes them in `OnDisable` so disabling the component cleanly reverts the stat.
- [ ] Execution order set via `[DefaultExecutionOrder(n)]` if order relative to `PlayerMotor`, `GunController`, or `CameraController` matters.

### IInteractable implementation

- [ ] `CanInteract(GameObject)` returns false in states where interaction would be wrong (e.g., already unlocked door).
- [ ] `GetPrompt(GameObject)` returns a short, verb-first human-readable string ("Unlock Door", "Take Battery", "Extract").
- [ ] `Interact(GameObject)` is idempotent or guarded — calling it twice should not cause double-effects.
- [ ] The interactable is on the correct physics layer so `PlayerInteractor`'s layerMask hits it.

### IBatteryDrainer implementation

- [ ] Calls `BatterySystem.Instance.RegisterDrainer(this)` in `OnEnable`.
- [ ] Calls `BatterySystem.Instance.UnregisterDrainer(this)` in `OnDisable`.
- [ ] `DrainRate` property returns `0f` when the drainer is inactive (light off, augment disabled, etc.) — do not unregister just to stop draining.

### IDamageable implementation

- [ ] Accepts `DamageContext` — does not have a separate `TakeDamage(float)` path that bypasses context.
- [ ] Returns actual damage dealt (after armour/resistance modifiers) so the attacker can react.
- [ ] Death fires through `RunManager` if the target is the player (never call `SceneManager.LoadScene` directly from a damage handler).

### AI system (BaseEnemyAI subclass)

- [ ] Extends `BaseEnemyAI`, does not implement state logic independently.
- [ ] Overrides only the state methods that differ from the base (virtual, not abstract, so unoverridden states get sensible defaults).
- [ ] Uses `TeamId` / `IFactionProvider` for targeting — does not hardcode "attack tag == Player".
- [ ] NavMeshAgent movement: either agent owns movement OR `agent.updatePosition = false` with manual sync — never both.
- [ ] `IStimulusListener.ListensTo` is tightly filtered — only the stimulus types this enemy actually reacts to.
- [ ] Registers with `IRunLifecycleListener` so it resets correctly on run start/death/extract.

### UI panel

- [ ] Opens and closes via `GameInputState.Block()` / `GameInputState.Unblock()` — cursor and input blocking are automatic.
- [ ] Does not reference game systems directly — reads data through a service or passes callbacks.
- [ ] Can handle being opened before data is ready (null checks, loading states).

### Loot / Economy

- [ ] All loot definitions use `LootPoolSO` — no inline `ItemSO[]` arrays on spawn points.
- [ ] All price reads/writes go through `CurrencyService` — not direct WSM mutation.
- [ ] If this is a `TraderSO` stock entry, it implements `ILootContainer` so any container UI can display it.

### Save / Load

- [ ] `SaveId` is a stable, human-readable string that will not change between versions.
- [ ] `CaptureSaveData()` returns a plain serializable object (no Unity types — no `Vector3`, `Quaternion`, use floats).
- [ ] `RestoreSaveData()` is null-safe and handles missing keys gracefully (old saves may not have new fields).
- [ ] `RunScopeTag` is set correctly — test by dying mid-run and confirming the right data is lost vs kept.

---

## Wave-End Milestone Checks

### After Wave 0

- [ ] All contracts compile with zero warnings.
- [ ] No feature code references the interfaces yet — this wave is definitions only.
- [ ] `WsmKeyRegistrySO` has entries for all keys planned through Wave 2.
- [ ] `PlayerStatModifier` stack has been added to `PlayerMotor` and `PlayerHealth` (no existing behavior changes — stack starts empty, net value = 1.0).

### After Wave 1 — Milestone: "The Squeeze is playable"

- [ ] Player moves and sprints with stamina drain visible on HUD.
- [ ] Encumbrance visibly slows movement when carrying max weight.
- [ ] Flashlight toggles on/off, battery bar drains in real time.
- [ ] Darkness state triggers when battery hits zero: vision loss, WSM key `player.in_darkness` = true.
- [ ] 2-minute darkness timer: after 2 min without light, a hunt stimulus fires (verify in StimulusSystem log).
- [ ] LowBatteryWarning flicker and audio play at ~20% charge.
- [ ] One `GuardAI` enemy: patrols in Idle, investigates a noise stimulus, chases in Combat, returns to post on losing player.
- [ ] GuardAI damages player using `DamageContext` and `IDamageable` — health bar drops.
- [ ] No crashes when enemy is in range of the player in darkness.

### After Wave 2 — Milestone: "The Loop is playable"

- [ ] Hub scene loads, no enemies spawn inside the NoEnemyZone.
- [ ] Player can start a run (scene transition), pick up loot (InventoryUI has items), and extract (ExtractionPoint interaction).
- [ ] On extraction: run-scoped inventory items persist, enemy/loot state resets on re-enter.
- [ ] On death: run-scoped inventory lost, stash and money kept, respawn at hub.
- [ ] RechargeStation refills battery to full.
- [ ] Trader UI opens, displays stock, player can buy an item using `CurrencyService`.
- [ ] Selling an item to the trader adds credits.
- [ ] Stash persists across runs (put item in stash, die, stash item still there).
- [ ] Pause menu opens, blocks all gameplay input, resumes correctly.
- [ ] Hotbar keys 1–4 switch weapons.
- [ ] Saving and reloading from hub restores inventory, money, and stash.

### After Wave 3 — Milestone: "The Threat is real"

- [ ] Gunshot noise emits a stimulus with correct radius (verify louder than footsteps in StimulusSystem).
- [ ] Encumbrance multiplies noise radius (heavy load = louder footsteps trigger AI from further).
- [ ] MonsterAI activates on noise stimulus, chases player aggressively.
- [ ] MonsterAI receives `player.in_darkness` WSM event and hunts player location.
- [ ] Headshot on guard does more damage than body shot (HitZone multipliers working).
- [ ] Player can climb a ladder (mounts, moves vertically, dismounts at top and bottom).
- [ ] Guard enemy uses NavMesh off-mesh link to traverse ladder (or path around it).
- [ ] Falling from sufficient height damages player (verify with short vs long drop).

### After Wave 4 — Milestone: "Exploration has structure"

- [ ] Locked door cannot be opened without the correct key item in inventory.
- [ ] Single-use key is consumed after use; reusable key stays in inventory.
- [ ] Map item can be bought from trader and viewed in MapUI.
- [ ] Underground map reading requires 3s hold + player standing still.
- [ ] Trader stock restocks after the configured number of runs.
- [ ] Hub and run sectors load/unload as additive scenes without NavMesh path breaks.
- [ ] Settings menu persists audio volume and graphics settings across sessions.

### After Wave 5 — Milestone: "The game has depth"

- [ ] NPC dialogue opens, displays trader portrait + text lines, can trigger quest progress via WSM.
- [ ] Readable document pickups open in journal view.
- [ ] Journal shows active quests, collected lore.
- [ ] At least one augment (e.g., night vision) drains battery and modifies player stats.
- [ ] Melee weapon does quiet damage (low sound stimulus radius vs gunfire).
- [ ] Flare throwable lights an area and draws MonsterAI toward it.
- [ ] Hazard zone deals damage over time; augment resistance reduces it.
- [ ] Shortcut, once opened, persists across runs and loads correctly from save.

---

## Common Mistakes to Avoid

| Mistake | Why It Hurts | Correct Approach |
|---------|--------------|------------------|
| New MonoBehaviour owning stamina | Splits authority with `PlayerHealth` | `PlayerHealth` owns energy; encumbrance pushes a drain-rate modifier |
| Direct WSM.SetInt for credits | Bypasses budget checks, no events | Always use `CurrencyService.Spend/Add` |
| `FindObjectOfType` in Awake | Breaks load order | Use singleton `Instance` or serialize the reference |
| Baking a single global NavMesh | Breaks with additive sector loading | One `NavMeshSurface` per sector |
| Mixing NavMeshAgent + Rigidbody movement | Agent and physics fight each other | Pick one; use `agent.updatePosition = false` if Rigidbody needed |
| Hardcoding "Player" tag in AI targeting | Breaks multi-faction/NPC scenarios | Use `IFactionProvider.GetTeamId()` |
| Deleting a WSM key from the registry | Old save files contain it, breaks load | Mark deprecated, never delete |
| Opening a UI panel without `GameInputState.Block()` | Cursor hidden, input leaks to game | Always Block on open, Unblock on close |
| Writing a save adapter without `RunScopeTag` | Data gets wiped or kept incorrectly on death | Set scope tag, test by dying mid-run |
| Calling `SceneManager.LoadScene` directly from a damage handler | Skips save, state corruption | Route through `RunManager.TriggerDeath()` |

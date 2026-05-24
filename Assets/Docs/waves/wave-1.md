# Wave 1 — The Pressure

**What this wave delivers:** The squeeze is playable. Battery drains, darkness hurts, encumbrance slows you down, and one guard enemy threatens you. After this wave you can stand in a test room with a flashlight, feel the tension of dwindling battery, and get chased.

**Prerequisite:** All of Wave 0 is ticked. The `PlayerStatModifier` stack is on `PlayerMotor` and `PlayerHealth`.

---

## W1-01 — `EncumbranceSystem`

**File:** `Scripts/Player/EncumbranceSystem.cs`

**How to build:**
- MonoBehaviour on the Player object.
- Each frame: sum `ItemInstance.data.weight` for every item in `InventoryGrid.GetAllItems()`.
- Compute encumbrance tier: Light (< 60%), Medium (60–85%), Heavy (85–100%), Overloaded (> 100%).
- Push/replace a `PlayerStatModifier` for each affected stat:
  - `StatType.Speed`: Light=1.0, Medium=0.85, Heavy=0.65, Overloaded=0.45
  - `StatType.NoiseMult`: Light=1.0, Medium=1.15, Heavy=1.4, Overloaded=1.8
  - `StatType.StaminaDrain`: Light=1.0, Medium=1.2, Heavy=1.6, Overloaded=2.5
- Use a single modifier Id per stat (`"encumbrance.speed"`, `"encumbrance.noise"`, etc.) and call `stack.Remove(id)` then `stack.Add(newMod)` on each re-evaluation.
- Re-evaluate on `InventoryGrid.OnChanged` event (not every frame — only when inventory changes).

**Leave space for:** `GameBalanceSO` (Wave 5) will replace the hardcoded tier thresholds and multipliers. For now, put the numbers in a `[SerializeField]` on the component or a placeholder `EncumbranceSO`.

**Watch out for:** Do not call `InventoryGrid.GetAllItems()` every frame — it allocates. Subscribe to a change event or cache weight when inventory changes. If `InventoryGrid` does not yet expose an `OnChanged` event, add one now.

---

## W1-02 — Stamina Integration into `PlayerHealth`

**No new MonoBehaviour.** Modify `PlayerHealth.cs` and `PlayerMotor.cs`.

**How to build:**
- `PlayerHealth.UseEnergy(float amount)` already exists. Multiply `amount` by the current `StatModifierStack.Net(StatType.StaminaDrain)` before subtracting.
- Sprint: `PlayerMotor` calls `PlayerHealth.UseEnergy(sprintDrainRate * Time.deltaTime)` each frame while sprinting. If `PlayerHealth.CurrentEnergy <= 0`, push a `PlayerStatModifier` of type `Speed` with value `0.4f` (exhaustion penalty) until energy recovers above 20%.
- Crouch: no drain (or very small drain for ladder climbing — add later).

**Leave space for:** `LadderClimbing` (W3) will call `PlayerHealth.UseEnergy` per meter climbed. `EncumbranceSystem` already scales the drain rate via `StatType.StaminaDrain`.

**Watch out for:** `PlayerHealth` already has `maxEnergy` and auto-regen. Do NOT add a second energy variable. The only change is multiplying the drain input.

---

## W1-03 — `BatteryItemSO` + `BatteryItemInstance`

**Files:** `Scripts/Items/BatteryItemSO.cs`, `Scripts/Items/BatteryItemInstance.cs`

**How to build:**
- `BatteryItemSO : ItemSO` — adds fields: `BatteryType` enum (Rechargeable, OneTime), `float maxCharge`.
- `BatteryItemInstance : ItemInstance` — adds `float CurrentCharge`, `BatteryType Type`.
- Grid size: 1×1 for one-time batteries, 1×2 for rechargeable (designer's call, set on SO).

**Leave space for:** `BatterySystem` holds a reference to the active `BatteryItemInstance`. When the player swaps batteries in inventory (same context-menu action as loading a magazine into a gun), `BatterySystem.SwapBattery(instance)` is called.

**Watch out for:** The SO carries max capacity; the Instance carries current charge. Never store runtime charge on the SO (it's a shared asset).

---

## W1-04 — `BatterySystem`

**File:** `Scripts/Core/Battery/BatterySystem.cs`

**How to build:**
- Singleton MonoBehaviour, `DontDestroyOnLoad`.
- Holds: `BatteryItemInstance ActiveRechargeable`, `BatteryItemInstance ActiveOneTime`.
- `_drainers` list of `IBatteryDrainer` (snapshot before iterating like StimulusSystem).
- `RegisterDrainer/UnregisterDrainer`: add/remove from list.
- `Update`: `float totalDrain = _drainers.Sum(d => d.DrainRate)`. Drain from one-time first; when depleted, drain rechargeable; when rechargeable depleted, `ActiveCharge = 0`, fire `OnBatteryDepleted` event.
- `RefillRechargeable()`: sets `ActiveRechargeable.CurrentCharge = maxCharge` (called by RechargeStation).
- `SwapBattery(BatteryItemInstance)`: replaces active slot. Called by inventory context-menu action (same code path as magazine swap in guns).
- `float ActiveCharge` property: returns `ActiveOneTime?.CurrentCharge ?? ActiveRechargeable?.CurrentCharge ?? 0`.
- Implements `ISaveable` with `RunScopeTag.Run` (battery resets to full rechargeable each run, one-time batteries consumed state tracked by the item instances in inventory save).

**Leave space for:** Any new drainer (headlamp, augment) just calls `RegisterDrainer(this)` on `OnEnable`. `BatterySystem` never needs to know what drained it.

**Watch out for:** Drain order matters: one-time before rechargeable. The player should feel the one-time batteries extending their range, then the rechargeable as their leash.

---

## W1-05 — `ILightSource` + `LightSource` component

**Files:** `Scripts/Core/Battery/ILightSource.cs`, `Scripts/World/LightSource.cs`

**How to build:**
```csharp
public interface ILightSource
{
    bool IsOn { get; }
    float Intensity { get; }
    void Toggle();
    void SetMode(LightMode mode); // Dim, Bright
}
public enum LightMode { Off, Dim, Bright }
```

`LightSource : MonoBehaviour, ILightSource, IBatteryDrainer`:
- Wraps a Unity `Light` component.
- `Toggle()` cycles Off → Dim → Bright → Off (or Off → On, designer choice on SO).
- `DrainRate` returns `_dimDrainRate` or `_brightDrainRate` based on current mode, 0 when off.
- Registers with `BatterySystem` in `OnEnable`, unregisters in `OnDisable`.
- Reads `F` key input only if `GameInputState.GameplayBlocked == false`.

**Leave space for:** `MountedLight` and `Headlamp` (Wave 5) are separate components that implement the same `ILightSource` + `IBatteryDrainer` interfaces. They set drain rates from their own SOs.

**Watch out for:** `LightSource` is on the player flashlight — it's hand-held, so it follows a hand bone or camera child transform. Set up the transform hierarchy so `MountedLight` (follows gun muzzle) and `Headlamp` (follows camera) can be added later without moving the base component.

---

## W1-06 — `DarknessState`

**Files:** `Scripts/Player/DarknessStateWriter.cs`, `Scripts/Player/DarknessStateVisual.cs`

**Keep these two scripts separate.** Gameplay fact ≠ visual presentation.

**`DarknessStateWriter`:**
- Subscribes to `BatterySystem.OnBatteryDepleted` and `BatterySystem.OnChargeRestored`.
- On depleted: `WorldStateManager.SetBool("player.in_darkness", true)`. Start 2-minute coroutine.
- On 2-minute timeout: `StimulusSystem.Broadcast(new Stimulus { Type = StimulusType.Sound, Position = player.position, Radius = 999f, Intensity = 1f })` — this is the hunt trigger. Every MonsterAI that is `IStimulusListener` will receive it.
- On charge restored: `WorldStateManager.SetBool("player.in_darkness", false)`. Cancel coroutine.

**`DarknessStateVisual`:**
- Subscribes to `WorldStateManager.OnStateChanged` and watches `"player.in_darkness"`.
- On true: blend in a post-processing `Vignette` + desaturate `ColorAdjustments` profile via `Volume` weight.
- On false: blend back out.
- Does NOT write any WSM keys. Does NOT know about BatterySystem. It only reads WSM.

**Leave space for:** `MonsterAI` (Wave 3) subscribes to the 999-radius stimulus. `QuestSystem` can reference `player.in_darkness` in quest conditions. HUD can show a darkness warning icon watching the same WSM key.

**Watch out for:** The 2-minute coroutine must be restarted if light returns and goes out again. Track the `Coroutine` reference and `StopCoroutine` before restarting.

---

## W1-07 — HUD Additions

**File:** Modify `Scripts/UI/PlayerHUD.cs`

**How to build:**
- Add a battery bar (fill image, driven by `BatterySystem.ActiveCharge / maxCharge`).
- Add a stamina bar (fill image, driven by `PlayerHealth.CurrentEnergy / maxEnergy`). Already have HP bar pattern — clone it.
- Add a weight indicator: a text label showing `currentWeight / maxCarryWeight`. Color-coded by encumbrance tier (white → yellow → orange → red).
- Subscribe to `BatterySystem`'s charge changed event (add `OnChargeChanged(float newCharge)` event to BatterySystem if not present).

**Watch out for:** `PlayerHUD` already builds its layout in code (no prefab). Follow the same code-driven pattern. Do not introduce a prefab dependency.

---

## W1-08 — `LowBatteryWarning`

**File:** `Scripts/Player/LowBatteryWarning.cs`

**How to build:**
- MonoBehaviour on the player or persistent manager.
- Subscribes to `BatterySystem.OnChargeChanged`.
- When charge drops below 20%: start a coroutine that flickers the `LightSource` (rapid toggle with random short intervals). Play a looping audio clip on the flashlight's `AudioSource`.
- When charge rises above 25% (hysteresis): stop flicker, stop audio.
- The threshold float is `[SerializeField]` — not hardcoded.

**Leave space for:** `GameBalanceSO` (Wave 5) will expose this threshold. For now, a serialized field is fine.

---

## W1-09 — `BaseEnemyAI` abstract class

**File:** `Scripts/AI/BaseEnemyAI.cs`

**How to build:**
```csharp
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AIPerception))]
public abstract class BaseEnemyAI : MonoBehaviour, IRunLifecycleListener, IFactionProvider, IDamageable
{
    protected NavMeshAgent Agent;
    protected AIPerception Perception;

    // Override these in subclasses:
    protected virtual void OnIdle() { }
    protected virtual void OnInvestigate(Vector3 targetPos) { }
    protected virtual void OnAlert(Vector3 targetPos) { }
    protected virtual void OnCombat(Transform target) { }
    protected virtual void OnLostTarget() { }

    // Base handles: state machine dispatch, registration, death
    // IRunLifecycleListener: resets position/state on OnRunStarted()
    // IDamageable: tracks HP, fires OnDeath when 0, writes WSM npc.{id}.dead
    // IFactionProvider: returns TeamId from EnemyTypeSO
}
```

`AIPerception.OnStateChanged` → call the appropriate virtual method. Subclasses only override what differs.

**Leave space for:** `GuardAI` overrides `OnIdle` (patrol route) and `OnCombat` (attack + return to post). `MonsterAI` overrides `OnIdle` (wander), `OnCombat` (charge), `OnLostTarget` (search spiral). ~80% of code lives in `BaseEnemyAI`.

**Watch out for:** Death handling must go through `RunManager` if the dead entity is the player. For enemy deaths: set `npc.{id}.dead` in WSM, then call `Destroy(gameObject)` or return to pool via `IPoolableSpawnedEntity`. Do not call `SceneManager` from here.

---

## W1-10 — `GuardAI`

**File:** `Scripts/AI/GuardAI.cs`

**How to build:**
```csharp
public class GuardAI : BaseEnemyAI
{
    [SerializeField] private Transform[] _patrolPoints;
    [SerializeField] private float _attackRange = 2f;
    [SerializeField] private float _attackDamage = 15f;

    protected override void OnIdle()
    {
        // Advance to next patrol point via NavMeshAgent.SetDestination
    }

    protected override void OnCombat(Transform target)
    {
        // Move to within attack range, call target.GetComponent<IDamageable>()
        // .ApplyDamage(new DamageContext { BaseDamage = _attackDamage, Type = DamageType.Melee, ... })
        // Uses TeamId check: IsHostileTo(target.GetComponent<IFactionProvider>().TeamId)
    }

    protected override void OnLostTarget()
    {
        // Return to patrol route, resume from closest point
    }
}
```

**Leave space for:** `EnemyTypeSO` (Wave 5) will replace `_attackRange` and `_attackDamage` serialized fields. For now, serialize them directly.

**Watch out for:** `NavMeshAgent.SetDestination` is called once per state change, not every frame. Only re-set destination if the target has moved significantly in `OnCombat`. Check `NavMeshAgent.remainingDistance` to know when patrol point is reached.

---

## W1-11 — `EnemySpawnPoint`

**File:** `Scripts/World/EnemySpawnPoint.cs`

**How to build:**
- MonoBehaviour placed in the scene.
- Fields: `GameObject enemyPrefab`, `float respawnDelay`, `string sectorId`.
- `Spawn()`: Instantiate prefab at transform position/rotation. Register with `IPoolableSpawnedEntity` pool.
- `IRunLifecycleListener.OnRunStarted()`: re-enable spawn point, clear "has spawned" flag.
- On enemy death (via `BaseEnemyAI.OnDeath` event): start respawn timer coroutine → call `Spawn()` after delay.

**Leave space for:** `EnemySpawnSystem` (Wave 3) will manage multiple spawn points, control density per depth, and throttle respawns. `EnemySpawnPoint` is the per-point primitive; `EnemySpawnSystem` is the orchestrator.

---

## W1-12 — NavMesh Base Bake + Layer Mask Documentation

**No new script.** Editor setup pass.

**How to build:**
1. Add `NavMeshSurface` component to your test level root (one surface per scene — will become per-sector in Wave 4).
2. Set agent type to Humanoid (or create a custom agent type matching enemy capsule size).
3. Bake. Walk every path the enemy needs to traverse and verify no gaps.
4. Create `Assets/Docs/LayerMasks.md` listing every physics layer in use:
   - Default, TransparentFX, IgnoreRaycast, Player, Enemy, Interactable, Ground, Projectile, UI, InvisibleWall, Inventory (model layer 31).
5. Set `PlayerInteractor._interactionMask` to hit only `Interactable` layer.
6. Set `AIPerception._occlusionMask` to hit `Default + Ground + InvisibleWall`.
7. Set `WeaponWallPushback.collisionMask` to `Default`.

**Watch out for:** Layer mask drift is one of the most painful Unity maintenance issues. Document every mask decision now. Any new physics query must consult `LayerMasks.md` before setting a mask value.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 1"

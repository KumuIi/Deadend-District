# Wave 0 — Contracts

**What this wave delivers:** Pure C# interfaces, structs, and enums. Zero gameplay. Zero visible changes. Every wave after this builds on these definitions — getting them wrong causes cascading rework.

**Rule:** Do not write any feature code in this wave. Define contracts only. Implement nothing.

---

## W0-01 — `IBatteryDrainer`

**File:** `Scripts/Core/Battery/IBatteryDrainer.cs`

**How to build:**
```csharp
public interface IBatteryDrainer
{
    float DrainRate { get; }   // Units per second. Return 0 when inactive, NOT unregister.
    string DrainerName { get; } // For debug HUD display
}
```

**Leave space for:** MountedLight, Headlamp, night-vision augment, other cybernetics — all just implement this. `BatterySystem` never needs to know what they are.

**Watch out for:** Do NOT unregister a drainer just to pause draining. Return `DrainRate = 0f` while inactive. Unregistering and re-registering causes alloc churn and race conditions on scene load.

---

## W0-02 — `IDamageable` / `IHitReceiver`

**File:** `Scripts/Core/Combat/IDamageable.cs`

**How to build:**
```csharp
public interface IDamageable
{
    bool IsAlive { get; }
    float ApplyDamage(DamageContext ctx); // Returns actual damage dealt after modifiers
}
```

**Leave space for:** Player, guards, monsters, destructible props, hazard-triggered objects. All implement `IDamageable`.

**Watch out for:** `ApplyDamage` must return actual damage dealt (after armour, resistance). The caller (GunController, MeleeController) uses the return value to decide if a stagger or kill sound triggers. Do not return the input damage.

---

## W0-03 — `IRunResettable` + `RunScopeTag`

**File:** `Scripts/Core/SaveSystem/IRunResettable.cs` and `RunScopeTag.cs`

**How to build:**
```csharp
public enum RunScopeTag { Profile, Run, World, Temp }

// Extend ISaveable — add this to the existing interface
// RunScopeTag SaveScope { get; }
// OR: make IRunResettable a separate interface ISaveable implementors also implement
```

Scope meanings:
- `Profile` — persists across all runs (stash, money, shortcuts, augments, quest progress)
- `Run` — resets on death or extraction (player inventory, health, battery charge)
- `World` — major world flags (shortcuts opened, major quest flags, sector discoveries)
- `Temp` — resets on sector reload (enemy state, loose loot state)

**Leave space for:** Every `ISaveable` adapter you write in Waves 1–5 will declare its scope here. Getting this wrong now means save corruption later.

**Watch out for:** `World` and `Profile` sound similar but are different. `Profile` is per-player-account. `World` is per-playthrough and could in theory be reset for a "new game." Keep them separate.

---

## W0-04 — `IRunLifecycleListener`

**File:** `Scripts/Core/RunSystem/IRunLifecycleListener.cs`

**How to build:**
```csharp
public interface IRunLifecycleListener
{
    void OnRunStarted();
    void OnRunExtracted();
    void OnRunDied();
    void OnReturnedToHub();
}
```

**Leave space for:** EnemySpawnSystem, LootSpawnSystem, TraderSystem (restock check), BatterySystem (reset one-time battery count), DarknessState (reset 2-min timer), MonsterAI (deactivate). All register with `RunManager` and receive these hooks.

**Watch out for:** Systems must register/unregister in `OnEnable/OnDisable`. `RunManager` holds a `List<IRunLifecycleListener>` — snapshot before iterating to be reentrant-safe (same pattern as `StimulusSystem`).

---

## W0-05 — `PlayerStatModifier` struct + modifier stack

**Files:** `Scripts/Core/Stats/PlayerStatModifier.cs`, `Scripts/Core/Stats/StatModifierStack.cs`

**How to build:**
```csharp
public enum StatType { Speed, NoiseMult, StaminaDrain, VisibilityScore, CarryCapacity, HazardResist }

public struct PlayerStatModifier
{
    public string Id;        // Unique per applier, e.g. "encumbrance.heavy", "augment.exoskeleton"
    public StatType Stat;
    public float Value;      // Multiplier: 0.8f = 20% reduction. Use additive for flat bonuses.
    public bool IsMultiplier; // true = multiply into stack, false = add flat value
}

public class StatModifierStack
{
    private List<PlayerStatModifier> _modifiers = new();
    public void Add(PlayerStatModifier m) { _modifiers.Add(m); }
    public void Remove(string id) { _modifiers.RemoveAll(m => m.Id == id); }
    public float Net(StatType stat); // multiply all multipliers, then add all flat values
}
```

Add a `StatModifierStack` instance to `PlayerMotor` and `PlayerHealth`. They start empty; `Net()` returns 1.0 (no change).

**Leave space for:** EncumbranceSystem (Wave 1), HazardZones (Wave 5), Augments (Wave 5) all push entries. Nobody touches the base fields directly.

**Watch out for:** Remove by `Id`, not by value equality. Two different systems can push the same `StatType` with different Ids and both must apply. Test with two concurrent modifiers.

---

## W0-06 — `DamageContext` struct

**File:** `Scripts/Core/Combat/DamageContext.cs`

**How to build:**
```csharp
public struct DamageContext
{
    public GameObject Source;        // What caused the damage (gun, enemy, hazard)
    public GameObject Instigator;    // Who pulled the trigger (player, guard, monster)
    public Vector3 HitPoint;
    public Vector3 HitNormal;
    public string HitZoneId;         // "head", "torso", "limb", "" = no zone
    public DamageType Type;          // Bullet, Melee, Explosive, Fall, Hazard
    public float BaseDamage;
    public float Impulse;            // Physics push force magnitude
    public float StimulusLoudness;   // 0 = silent, 1 = loud. Filled by damage source.
}

public enum DamageType { Bullet, Melee, Explosive, Fall, Hazard }
```

**Leave space for:** `StimulusLoudness` feeds `StimulusSystem` — gunfire is loud, melee is quiet, hazard is silent. The damage handler can auto-broadcast a noise stimulus from this field.

**Watch out for:** Do NOT use `DamageContext` as a return value — it's an input. The return value of `IDamageable.ApplyDamage` is just `float` (damage dealt).

---

## W0-07 — `ILootContainer`

**File:** `Scripts/Core/Inventory/ILootContainer.cs`

**How to build:**
```csharp
public interface ILootContainer
{
    string ContainerName { get; }
    IReadOnlyList<ItemInstance> Items { get; }
    bool TryAddItem(ItemInstance item);
    bool TryRemoveItem(ItemInstance item);
    bool CanAddItem(ItemInstance item);
}
```

**Leave space for:** `InventoryGrid`, `StashSystem`, `TraderSystem` stock, chest world objects, enemy corpse loot — all implement this. The `TraderUI` and `StashUI` share one panel prefab that takes an `ILootContainer` as its data source.

**Watch out for:** Trader stock has buy/sell prices not in `ILootContainer`. Extend with `ITraderContainer : ILootContainer` for price data rather than adding price to the base interface.

---

## W0-08 — `IVisibilityContributor`

**File:** `Scripts/Core/Stealth/IVisibilityContributor.cs`

**How to build:**
```csharp
public interface IVisibilityContributor
{
    float GetVisibilityFactor(); // [0..1], 0 = invisible contribution, 1 = fully visible
    string ContributorName { get; } // For debug display
}
```

**Leave space for:** Active light sources near player, player movement speed, crouch state, wearing dark clothing (Wave 5 armour), augment stealth cloak. All register as contributors.

---

## W0-09 — `IEquipmentSlot` + `EquipmentController` skeleton

**File:** `Scripts/Core/Equipment/IEquipmentSlot.cs`, `EquipmentController.cs`

**How to build:**
```csharp
public interface IEquipmentSlot
{
    string SlotId { get; }         // "weapon_primary", "weapon_secondary", "flashlight", "headlamp"
    ItemInstance EquippedItem { get; }
    bool TryEquip(ItemInstance item);
    void Unequip();
}
```

`EquipmentController` is a MonoBehaviour holding a `Dictionary<string, IEquipmentSlot>` and methods `GetSlot(slotId)`, `EquipToSlot(slotId, item)`.

**Leave space for:** Hotbar (Wave 2), MountedLight/Headlamp (Wave 5), armour slot (Wave 5). All register slots on `Awake`.

**Watch out for:** `WeaponManager` already manages weapon equip — do NOT replace it. `EquipmentController` wraps `WeaponManager` for weapon slots and adds non-weapon slots alongside.

---

## W0-10 — `IFactionProvider` / `TeamId`

**File:** `Scripts/Core/AI/IFactionProvider.cs`

**How to build:**
```csharp
public enum TeamId { Player, Guard, Monster, Neutral, Trader }

public interface IFactionProvider
{
    TeamId TeamId { get; }
    bool IsHostileTo(TeamId other);
}
```

**Leave space for:** `GuardAI`, `MonsterAI`, `PlayerHealth` all implement `IFactionProvider`. AI targeting checks `IsHostileTo` — not tags. Allows future neutral NPCs and faction relations.

---

## W0-11 — `LootPoolSO`

**File:** `Scripts/Core/Loot/LootPoolSO.cs`

**How to build:**
```csharp
[CreateAssetMenu(menuName = "Loot/Loot Pool")]
public class LootPoolSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry { public ItemSO Item; [Range(0,1)] public float Weight; }
    public Entry[] Entries;

    public ItemSO Roll() // weighted random pick
    {
        float total = 0f;
        foreach (var e in Entries) total += e.Weight;
        float r = Random.value * total;
        foreach (var e in Entries) { r -= e.Weight; if (r <= 0) return e.Item; }
        return Entries[^1].Item;
    }
}
```

**Leave space for:** Trader stock (array of LootPoolSO per day), chest spawns, enemy drops, sector loot — all reference `LootPoolSO` assets. Designers drag in pools in the inspector, no code changes.

---

## W0-12 — `RunScopeTag` + Scope-Aware `SaveSystem` Methods

**Files:** `Scripts/Core/SaveSystem/RunScopeTag.cs` — extend `SaveSystem.cs`

Add to `SaveSystem`:
```csharp
public void SaveProfile();    // Captures + writes RunScopeTag.Profile saveables
public void SaveRun();        // Captures + writes RunScopeTag.Run saveables
public void SaveWorld();      // Captures + writes RunScopeTag.World saveables
public void ClearRun();       // Clears Run-scoped save data from disk
public void RestoreAfterSceneLoad(RunScopeTag scope); // Deferred restore for late-registering objects
```

`RestoreAfterSceneLoad` is needed because `ISaveable` objects register in `Start()`, but a scene load may finish before all objects have run `Start()`. Queue restore calls and flush when `SceneManager.sceneLoaded` fires.

**Watch out for:** The existing `SaveSystem.cs` saves everything in one envelope. Refactor to write separate envelopes per scope so `ClearRun()` only touches run-scoped data without corrupting profile data.

---

## W0-13 — `IPoolableSpawnedEntity`

**File:** `Scripts/Core/Spawning/IPoolableSpawnedEntity.cs`

**How to build:**
```csharp
public interface IPoolableSpawnedEntity
{
    string PoolId { get; }     // Groups objects for sector unload cleanup
    void OnSpawned();
    void OnDespawned();
}
```

**Leave space for:** EnemySpawnSystem, LootSpawnSystem, ThrowableController, ItemDropSpawner all produce entities that need clean teardown when a sector unloads. `SectorManager` calls `OnDespawned()` on everything with this interface before unloading.

---

## W0-14 — WSM Key Pre-Registration

**No new script.** Open `WsmKeyRegistrySO` asset in the inspector.

Add every key listed in the `PLAN.md` WSM Key Registry table for Waves 1 and 2 before writing a single line of system code:

- `player.in_darkness` (bool)
- `player.is_dead` (bool)
- `run.active` (bool)
- `run.extracted` (bool)
- `economy.credits` (int)
- `hub.recharge_station.used` (bool)
- `zone.hub.active` (bool)

Mark category and description for each. Do not add runtime read/write code to any system yet.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 0"

# Quest System & WorldState Writer Toolkit — Dev Reference

Two cooperating layers: **QuestManager** observes world facts, **WorldState writers** produce them.
QuestManager never contains quest-specific logic. Everything that *creates* a fact (detection, pickups, deaths, timers) lives on the world object that owns the behavior.

---

## Core Concept

```
NPC dialogue  ─┐
AI detection  ─┤─ WorldState writers ─→ WorldStateManager (KV store) ─→ QuestManager ─→ QuestStatus
Item pickup   ─┤                                                                          (Inactive/Active/Succeeded/Failed)
Timer expiry  ─┘
```

QuestManager subscribes to `WorldStateManager.OnStateChanged`. When any key changes, it evaluates all active quests. No polling, no per-frame checks.

---

## Quest Lifecycle

```
Inactive ──(activeCondition true)──→ Active ──(any failCondition true)──→ Failed
                                        │
                                        └──(all objectives true)──────────→ Succeeded
```

- **Inactive**: only `activeCondition` is watched. Objectives and fail conditions are ignored.
- **Active**: fail conditions are checked first on every WSM change. If any is true → Failed immediately. Then all objectives checked; if all true → Succeeded.
- **Succeeded / Failed**: terminal. Further WSM changes are ignored unless status is manually reset.

On transition, QuestManager sets a WSM flag:
- `quest.{questId}.active` = true
- `quest.{questId}.succeeded` = true
- `quest.{questId}.failed` = true

---

## QuestSO Fields

| Field | Purpose |
|---|---|
| `questId` | **Stable, never change after saving.** Used as save key. e.g. `"get_blackbox"` |
| `title` | Display name |
| `activeCondition` | Quest activates when this is true. Leave `wsmKey` empty to start Active immediately. |
| `objectives[]` | All must be true → Succeeded |
| `failConditions[]` | Any one true → Failed. Checked before objectives. |

Each condition (`QuestConditionDefinition`) has:

| Field | Values |
|---|---|
| `wsmKey` | The WorldStateManager key to watch |
| `valueType` | Bool / Int / Float / String |
| `comparison` | Equals / NotEquals / GreaterThan / GreaterOrEqual / LessThan / LessOrEqual |
| `expected*` | Fill the field matching your valueType |

**Bool/String:** only Equals and NotEquals are meaningful. Numeric comparisons return false.  
**Float Equals:** uses 0.001 epsilon. Prefer GreaterOrEqual for thresholds.  
**Missing key:** evaluates to false (not default value). A `NotEquals` condition on a key that was never written will NOT pass.

---

## Creating a Quest (no scripting required for most cases)

**Step 1** — Right-click in Project → **Create → Quest → Quest Definition**

**Step 2** — Fill in:
```
questId:         "infiltrate_factory"          ← stable, snake_case
title:           "Infiltrate the Factory"
activeCondition: key="quest.infiltrate_factory.active"  type=Bool  comparison=Equals  expectedBool=true
objectives[0]:   key="zone.factory.entered"             type=Bool  comparison=Equals  expectedBool=true
objectives[1]:   key="item.evidence.collected"          type=Bool  comparison=Equals  expectedBool=true
failConditions[0]: key="quest.infiltrate_factory.detected"  type=Bool  comparison=Equals  expectedBool=true
```

**Step 3** — Drag the asset into `QuestManager` → **Quests** list in the Inspector.

**Step 4** — Place WorldState writer components in the scene to set those keys (see below).

---

## WorldState Writer Toolkit

These components set WSM keys from scene events. They know nothing about quests.

### WorldStateWriter
**The primitive.** All other writers use the same write logic.  
Set `_key`, `_valueType`, `_value`, then call `Write()` from a UnityEvent or code.

```
Options:
  writeOnStart  → writes immediately when scene loads
  onlyOnce      → ignores Write() calls after the first
```

**Wire to a UnityEvent on any component** (button, animation event, dialogue system, etc.)

---

### WorldStateTriggerVolume
**Zone entry → write key.**  
Requires a Collider with `Is Trigger = true` on the same GameObject.

```
_requiredTag   → only fires for objects with this tag (default: "Player")
_onlyOnce      → fire once even if player enters multiple times (default: true)
```

Set `_key = "zone.factory.entered"`, `_boolValue = true`.

---

### WorldStateOnAIState
**AI detection → write key.**  
Subscribe to a specific `AIPerception` reaching a threshold state.

```
_target             → AIPerception to watch
_triggerState       → fire when this state is reached (e.g. Alert)
_fireOnHigherStates → also fire for states above triggerState (Alert fires on Combat too)
_onlyOnce           → fire once (default: true)
```

Example: `_triggerState = Alert` → sets `"quest.infiltrate_factory.detected" = true` the moment any guard spots the player.

---

### WorldStateTimer
**Countdown → write key.**  
ISaveable — remaining time persists across save/load.

```
_duration     → seconds
_startOnAwake → begin counting immediately
_saveId       → MUST BE UNIQUE PER TIMER IN THE SCENE (e.g. "timer.heist_escape")
```

> ⚠️ **Every timer in your scene needs a different `_saveId`.** Two timers with the same id will corrupt each other's save data.

Call `StartTimer()` from a UnityEvent or code when the quest activates.

---

### WorldStateCounter
**Count events → write int key.**  
WSM is the source of truth — no local cache. Safe across save/load automatically.

```
_wsmKey        → e.g. "combat.raiders_killed"
_initialValue  → starting count (written to WSM on Start if key is absent)
_threshold     → fires OnThresholdReached when count first crosses this value
```

Call `Increment()` from a UnityEvent. Wire it to enemy death, item collection, etc.  
`OnThresholdReached` fires only when crossing the threshold (4→5), not on every write above it.

---

### WorldStateOnDeath
**Death event → write key.**  
Currently scoped to `PlayerHealth.OnDeath`. Will be updated when enemies get their own health system.

```
_target   → PlayerHealth to watch. Leave null to use one on the same GameObject.
```

---

## Common Patterns

### NPC gives the quest
```
Dialogue node ends
  → WorldStateWriter on the NPC (or dialogue manager)
  → sets "quest.get_blackbox.active" = true
QuestManager activates the quest automatically.
```

### Fragile item (breaks if player takes damage)
```csharp
// On a MonoBehaviour attached to the item or player:
void Start()
{
    playerHealth.OnDamaged += OnPlayerHit;
}

void OnPlayerHit(float damage)
{
    if (InventoryUI.HasItem(_itemSO))
        WorldStateManager.Instance.SetBool("quest.blackbox.intact", false);
}
```
QuestSO failCondition: `"quest.blackbox.intact"` == false

### Kill count quest
```
WorldStateCounter on each enemy prefab:
  _wsmKey = "combat.bandits_killed"
  → Increment() wired to enemy death event

QuestSO objective: "combat.bandits_killed"  Int  GreaterOrEqual  5
```

### Timed extraction
```
WorldStateTimer: _saveId="timer.extraction"  _duration=120
  → StartTimer() wired to extraction zone entered

QuestSO failCondition: "quest.heist.timer_expired"  Bool  Equals  true
WorldStateTimer's _key = "quest.heist.timer_expired"
```

---

## Things to Watch Out For

**QuestId stability**  
`questId` is used as the save key. Changing it after a save file exists leaves orphaned data and the quest resets to Inactive on load.

**WSM key naming convention**  
Follow the `"category.identifier.property"` pattern from Core README. Be consistent — WSM keys are case-insensitive for reads but `OnStateChanged` fires with the exact string you passed to `Set*()`. QuestManager uses `OrdinalIgnoreCase` comparison, so casing mismatches won't silently break things.

**Activation condition and empty wsmKey**  
If `activeCondition.wsmKey` is empty, the quest starts Active immediately on scene load. This is intentional for quests that begin automatically (tutorial, main story). If your quest should wait for an NPC, always set the key.

**Quests with no objectives**  
A quest with an empty `objectives[]` array will never Succeed (returns false for AllObjectivesMet). This is intentional — prevents accidentally shipping quests that auto-complete. Add at least one objective.

**Timer saveId uniqueness**  
See WorldStateTimer section above. This is the most common setup mistake.

**WorldStateOnAIState and scene-loaded guards**  
If the AIPerception target is on a prefab that loads after this component, `_target` may be null at OnEnable. Assign `_target` in the Inspector, not dynamically, unless you add a null check.

**One-shot writers after scene reload**  
`onlyOnce` and `_fired` flags are runtime-only — not persisted. After a scene reload they reset. This is safe because the WSM key they wrote IS persisted (via WorldStateSaveAdapter), so the quest won't re-trigger. But `OnWritten` UnityEvents will fire again on next Write() if the scene reloads. Make sure OnWritten listeners are idempotent.

**Fail conditions are permanent**  
Once a quest transitions to Failed, it stays Failed. There's no built-in reset. To support retryable quests, you'll need to call `InitRuntime()` on QuestManager (or add a public `ResetQuest(string id)` method) and clear the relevant WSM keys.

---

## Scene Setup Checklist

```
GameSystems GO (DontDestroyOnLoad):
  ✓ WorldStateManager
  ✓ StimulusSystem
  ✓ SaveSystem
  ✓ WorldStateSaveAdapter
  ✓ QuestManager  ← add your QuestSO assets to the Quests list

Player GO:
  ✓ PlayerHealth  (OnDamaged event now available)

Enemy GO (each):
  ✓ AIPerception  (assign _playerTarget + _occlusionMask)
                  (OnStateChanged event available for WorldStateOnAIState)
```

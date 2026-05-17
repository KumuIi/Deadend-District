# Quest System & WorldState Writer Toolkit — Dev Reference

Two cooperating layers: **QuestManager** observes world facts, **WorldState writers** produce them.
QuestManager never contains quest-specific logic. Everything that *creates* a fact (detection, pickups, deaths, timers) lives on the world object that owns the behaviour.

---

## Core Concept

```
NPC dialogue  ─┐
AI detection  ─┤─ WorldState writers ─→ WorldStateManager (KV store) ─→ QuestManager ─→ QuestStatus
Item pickup   ─┤
Timer expiry  ─┘
```

QuestManager subscribes to `WorldStateManager.OnStateChanged`. On any key change it re-evaluates all registered quests. No polling, no per-frame work (expiry tracking is the only Update logic).

---

## Quest Lifecycle

```
Inactive ──(requiredQuests done + activeCondition)──→ Active
                                                          │
                    ┌─────────────────────────────────────┤
                    ↓                                     │
              any globalFailCondition true          outcomes[] present?
                    │                                     │
                    → Failed                     yes      │      no
                                                  ↓       │       ↓
                                          first outcome  all mandatory
                                          condition true  objectives true
                                               │                │
                                        outcome.terminalStatus  → Succeeded
                                     (Succeeded / Failed / Expired)
```

**Statuses:** `Inactive` `Active` `Succeeded` `Failed` `Expired` `Cancelled`

- `Expired` — soft timeout (quest ran out of time, not "failure" in narrative terms)
- `Cancelled` — removed by mutual exclusion (`cancelOnActivate`) or an outcome's `cancelQuests`

On every status transition QuestManager writes a WSM flag:
```
quest.{questId}.active    = true
quest.{questId}.succeeded = true
quest.{questId}.failed    = true
quest.{questId}.expired   = true
quest.{questId}.cancelled = true
```
Repeatable quests clear these flags when they reset.

---

## Creating a Quest

**Right-click Project → Create → Quest → Quest Definition**

The `questId` is auto-generated (stable GUID, hidden in inspector). **Never edit it by hand.**

> ⚠️ **If you duplicate a QuestSO asset (Ctrl+D):** right-click the copy → **Reset Quest ID**. Duplicates share the same GUID and one will be silently skipped by QuestManager.

---

## QuestSO Fields

### Display
| Field | Purpose |
|---|---|
| `title` | Shown in journal / debug logs |
| `description` | Quest summary text |

### Activation
| Field | Purpose |
|---|---|
| `requiredQuests[]` | All must be `Succeeded` before this quest can activate. **Drag SO refs — no strings.** |
| `activeCondition` | Optional extra WSM gate. Leave `wsmKey` empty to activate as soon as requiredQuests are done. |
| `cancelOnActivate[]` | When THIS quest activates, immediately cancel these quests. Use for faction/mutually-exclusive pairs. |

### Simple Path — Objectives
| Field | Purpose |
|---|---|
| `objectives[]` | `QuestObjectiveDefinition` list. All mandatory ones must pass → Succeeded. Leave empty if using Outcomes. |

Each **QuestObjectiveDefinition** has:

| Field | Purpose |
|---|---|
| `description` | Override display text (leave empty to show condition description) |
| `condition` | WSM check that marks it complete |
| `optional` | Doesn't block success; still appears in journal as bonus task |
| `hidden` | HUD hides this objective until it completes or `revealCondition` passes |
| `revealCondition` | WSM check that reveals a hidden objective early (e.g. player finds a clue) |

### Branching Path — Outcomes
| Field | Purpose |
|---|---|
| `outcomes[]` | When non-empty, overrides objectives. Checked in array order — first match fires. |

Each **QuestOutcomeDefinition** has:

| Field | Purpose |
|---|---|
| `label` | Editor label only: "Kill", "Spare", "Escape" |
| `condition` | WSM check for this outcome |
| `terminalStatus` | `Succeeded` / `Failed` / `Expired` |
| `activateQuests[]` | TryActivate these quests (prerequisites still checked) |
| `cancelQuests[]` | Cancel these quests |
| `failQuests[]` | Fail these quests |

### Fail Conditions (simple shortcut)
| Field | Purpose |
|---|---|
| `globalFailConditions[]` | Any one true → immediately `Failed`, no branching. Use for: escort died, player detected, time ran out. If the failure needs to open another questline, use an Outcome with `terminalStatus=Failed` instead. |

### Fail Propagation
| Field | Purpose |
|---|---|
| `failsWithMe[]` | When this quest fails, immediately fail these too. Cycle-safe. |

### Expiration
| Field | Purpose |
|---|---|
| `canExpire` | Enable soft expiry |
| `expirationSeconds` | Seconds of active time before status → `Expired` |

### Repeatable / Contract
| Field | Purpose |
|---|---|
| `isRepeatable` | Quest can reset and run again |
| `resetCondition` | WSM check that triggers the reset (e.g. a new in-game day flag). Does **not** clear gameplay WSM keys like kill counters — reset those via UnityEvents on quest success. |

---

## Quest Authoring Examples

### Simple linear quest
```
QuestSO: "Find the Battery"
  requiredQuests: []
  objectives:
    [0] condition: item.blue_battery.found == true
        description: "Find the blue battery"
```

### Questline chain (bring item back)
```
QuestSO: "Return the Battery"
  requiredQuests: [FindTheBattery_SO]   ← drag, no string
  objectives:
    [0] condition: npc.trader.battery_delivered == true
```

### Stealth quest (fail if detected)
```
QuestSO: "Infiltrate the Hospital"
  objectives:
    [0] condition: hospital.entered == true
    [1] condition: hospital.documents.found == true
  globalFailConditions:
    [0] condition: quest.infiltrate.detected == true
         ↑ written by WorldStateOnAIState on guards
```

### Kill or spare branching
```
QuestSO: "Deal With Viktor"
  outcomes:
    [0] label=Kill   condition: npc.viktor.dead==true    terminalStatus=Succeeded
        activateQuests: [BountyCompleteQuest]
        cancelQuests:   [RedemptionPathQuest]
    [1] label=Spare  condition: npc.viktor.spared==true  terminalStatus=Succeeded
        activateQuests: [RedemptionPathQuest]
        cancelQuests:   [BountyCompleteQuest]
    [2] label=Escape condition: npc.viktor.escaped==true terminalStatus=Failed
        activateQuests: [ChaseToWarehouseQuest]
```

### Escort without target dying
```
QuestSO: "Escort Viktor Out"
  objectives:
    [0] condition: zone.extraction.reached == true
  globalFailConditions:
    [0] condition: npc.viktor.dead == true
         ↑ written by WorldStateOnDeath on Viktor's health component
```

### Repeatable daily contract
```
QuestSO: "Raider Hunt Contract"
  isRepeatable: true
  resetCondition: game.day.changed == true
  objectives:
    [0] condition: combat.raiders_killed >= 5
         ↑ WorldStateCounter on each raider prefab
```

### Expiring timed quest
```
QuestSO: "Defuse the Bomb"
  canExpire: true
  expirationSeconds: 120
  objectives:
    [0] condition: bomb.defused == true
```

### Faction mutual exclusion
```
QuestSO: "Join the Corporation"
  cancelOnActivate: [JoinTheRebels_SO]

QuestSO: "Join the Rebels"
  cancelOnActivate: [JoinTheCorporation_SO]
```

### Hidden secret objective (Dishonored chaos-style)
```
QuestSO: "Clear the Block"
  objectives:
    [0] condition: zone.block.cleared == true
        description: "Clear the block"
    [1] condition: combat.civilians_killed >= 1
        hidden: true
        description: "Killed civilians"   ← revealed only when it completes
```

---

## WSM Key Registry

Instead of typing raw strings into every inspector field, use the **WsmKeyRegistrySO**:

1. **Create once:** `Assets › Create › WSM › Key Registry` → save as `WsmKeyRegistry.asset`
2. **Add your keys** in the asset: set `displayName`, `key` (dot notation), `type`, `category`
3. Every `[WsmKey]` field in the inspector (all writer + condition fields) now shows a **searchable popup**

**Unknown key?** A ⚠ icon appears plus an `+ Add` button — click it to register the key without leaving the inspector.

**Deprecated key?** Tick `deprecated` in the registry — it stays in the dropdown greyed out so old assets still work while new ones can't accidentally pick it.

> The registry is **editor-only**. It has zero runtime cost and zero risk to save data.

---

## WorldState Writer Toolkit

These components write WSM keys from scene events. They have no knowledge of quests.

### WorldStateWriter
**The primitive.** Call `Write()` from a UnityEvent, `writeOnStart`, or code.

| Field | Purpose |
|---|---|
| `_key` | WSM key to write (`[WsmKey]` dropdown) |
| `_valueType` | Bool / Int / Float / String |
| `_value` | Fill the matching value field |
| `writeOnStart` | Write on scene Start |
| `onlyOnce` | Ignore subsequent Write() calls after the first |

---

### WorldStateTriggerVolume
**Zone entry → write key.** Requires a Collider with `Is Trigger = true` on the same GameObject.

| Field | Purpose |
|---|---|
| `_requiredTag` | Only fire for objects with this tag (default: "Player") |
| `_onlyOnce` | Fire only on first entry (default: true) |

Compose with `WorldStateWriter` on the same object — the trigger calls `writer.Write()`.

---

### WorldStateOnAIState
**AI detection → write key.**

| Field | Purpose |
|---|---|
| `_target` | `AIPerception` to watch |
| `_triggerState` | Fire when this state is reached (e.g. Alert) |
| `_fireOnHigherStates` | Also fire for states above trigger (Alert fires on Combat too) |
| `_onlyOnce` | Fire once (default: true) |

Compose with `WorldStateWriter`. Example: any guard going Alert sets `quest.infiltrate.detected = true`.

---

### WorldStateTimer
**Countdown → write key.** ISaveable — remaining time persists across save/load.

| Field | Purpose |
|---|---|
| `_duration` | Seconds |
| `_startOnAwake` | Begin immediately on scene load |
| `_saveId` | **Must be unique per timer in the scene.** e.g. `"timer.heist_escape"` |

> ⚠️ Two timers with the same `_saveId` corrupt each other's save data.

Call `StartTimer()` from a UnityEvent when the quest activates. Compose with `WorldStateWriter`.

---

### WorldStateCounter
**Count events → write int key.**

| Field | Purpose |
|---|---|
| `_wsmKey` | e.g. `"combat.raiders_killed"` (`[WsmKey]` dropdown) |
| `_initialValue` | Starting value (written on Start if key absent) |
| `_threshold` | Fires `OnThresholdReached` when count first crosses this value |

Call `Increment()` from a UnityEvent on enemy death, item pickup, etc.
`OnThresholdReached` fires only on threshold crossing (4→5), not on every write above it.

---

### WorldStateOnDeath
**Death event → write key.** Listens to `PlayerHealth.OnDeath`.

| Field | Purpose |
|---|---|
| `_target` | `PlayerHealth` to watch. Leave null for one on the same GameObject. |

Compose with `WorldStateWriter`.

---

### WorldStateOnAIState
See above under WorldState Writer Toolkit.

---

## Things to Watch Out For

**Duplicating a QuestSO asset**
Ctrl+D copies the hidden GUID. QuestManager detects duplicates at runtime and logs an error. Fix: right-click the new asset → **Reset Quest ID**.

**WSM key naming convention**
Follow `"category.identifier.property"` from the Core README. Keys are case-insensitive for reads but `OnStateChanged` fires with the exact casing you used to `Set*()`. QuestManager uses `OrdinalIgnoreCase`, so mismatched casing won't silently break quests.

**Quests with no objectives and no outcomes**
A quest with empty `objectives[]` and empty `outcomes[]` will never succeed. This is intentional — prevents accidentally shipping auto-completing quests. Add at least one objective or one outcome.

**Activation without an activeCondition**
If `requiredQuests[]` is empty and `activeCondition.wsmKey` is empty, the quest activates immediately on scene load. Intentional for tutorial / auto-start quests. If it should wait for an NPC, set the key.

**Repeatable quests and WSM counters**
`ResetForRepeat()` clears quest runtime state and the quest-graph WSM flags (`quest.{id}.succeeded` etc.) but does **not** reset gameplay counters like `combat.raiders_killed`. Reset those manually via `WorldStateCounter.ResetCount()` wired to `QuestManager.OnQuestSucceeded` or a UnityEvent.

**Timer saveId uniqueness**
Every `WorldStateTimer` in the scene needs a different `_saveId`. The most common setup mistake.

**onlyOnce flags are runtime-only**
`onlyOnce` / `_fired` on writer components are not saved. After a scene reload they reset. This is safe because the WSM key they wrote IS persisted — the quest won't re-trigger. But `OnWritten` UnityEvents will re-fire on the next `Write()`. Keep `OnWritten` listeners idempotent.

**Outcome activateQuests respects prerequisites**
`activateQuests[]` on an outcome calls `TryActivate`, not force-activate. If the target quest has its own `requiredQuests` or `activeCondition` that aren't satisfied yet, it will stay `Inactive`. This is correct behaviour — don't expect outcomes to bypass the quest graph.

---

## Scene Setup Checklist

```
GameSystems GO (DontDestroyOnLoad):
  ✓ WorldStateManager
  ✓ StimulusSystem
  ✓ SaveSystem
  ✓ WorldStateSaveAdapter
  ✓ QuestManager  ← drag QuestSO assets into the Quests list

Player GO:
  ✓ PlayerHealth  (OnDamaged / OnDeath events available)

Enemy GO (each):
  ✓ AIPerception  (assign _playerTarget + _occlusionMask)
                  (OnStateChanged available for WorldStateOnAIState)

Project (once):
  ✓ WsmKeyRegistry.asset  (Assets › Create › WSM › Key Registry)
                           Add all WSM keys here before wiring them in the inspector.
```

# Wave 5 — Depth

**What this wave delivers:** The world has story, variety, and flavor. NPCs talk. Notes tell stories. Augments give you an edge — at a cost. Enemies have personalities. The environment fights back. The game is a game.

**Prerequisite:** Wave 4 complete. The full run-loop, gating, and navigation are working.

> These items can be done in any order within the wave. They build on top of the finished core, not on each other (with a few noted exceptions).

---

## W5-01 — `SpatialAudioManager` + AudioMixer Setup

**File:** `Scripts/Audio/SpatialAudioManager.cs`

**How to build:**
- Create an `AudioMixer` asset with groups: Master, SFX, Ambient, Music, UI.
- `SpatialAudioManager`: singleton, provides `PlayAt(AudioClip, Vector3, AudioMixerGroup, float volume)` — spawns a pooled `AudioSource` at position, plays, returns to pool.
- Add `AudioReverbZone` components in tunnel geometry (large reverb) vs hub (small reverb).
- All existing audio sources (footsteps, gunfire, flashlight flicker) switch to routing through `SpatialAudioManager` or an `AudioMixerGroup`.
- Settings screen sliders control `audioMixer.SetFloat("MasterVolume", value)`.

**Watch out for:** Unity's 3D audio falloff is linear by default, which sounds wrong in enclosed spaces. Switch `AudioRolloffMode.Custom` with a curve that drops off fast (tunnels sound echoey, not distant).

---

## W5-02 — `DialogueSystem`

**Files:** `Scripts/Dialogue/DialogueSO.cs`, `Scripts/Dialogue/DialogueLine.cs`, `Scripts/UI/DialogueUI.cs`

**How to build:**

`DialogueLine`:
```csharp
[Serializable]
public class DialogueLine
{
    public string SpeakerName;
    public Sprite SpeakerPortrait;
    public string Text;
    public WsmKeyEntry[] WritesOnDisplay; // WSM keys to set when this line shows
}
```

`DialogueSO : ScriptableObject`: `DialogueLine[] Lines;`

`DialogueUI`:
- Panel: portrait image (left), speaker name, text (center), "Continue" / choice buttons.
- `Open(DialogueSO dialogue)`: play lines in sequence. `GameInputState.Block()`.
- On each line: write `WritesOnDisplay` keys to WSM. This triggers quest evaluations automatically.
- On last line: `GameInputState.Unblock()`, fire `OnDialogueComplete` event.

NPC `IInteractable` implementation:
- `Interact(g)`: call `DialogueUI.Open(npcDialogueSO)`.
- The same NPC can have different `DialogueSO` depending on WSM state (use `QuestConditionDefinition` to pick).

**Leave space for:** Branching choices (select from two dialogue options) — add `DialogueChoice[]` to `DialogueLine` and branch to a different `DialogueSO` index. The infrastructure supports it; implement when a quest needs it.

---

## W5-03 — `DocumentCollectible`

**Files:** `Scripts/World/DocumentPickup.cs`, `Scripts/Items/ReadableDocumentSO.cs`

**How to build:**

`ReadableDocumentSO : ScriptableObject`:
- `string Title`, `string BodyText`, `Sprite OptionalImage`.
- `WsmKeyEntry[] WritesOnRead` — same pattern as DialogueLine.

`DocumentPickup : MonoBehaviour, IInteractable`:
- References `ReadableDocumentSO`.
- `Interact(g)`: add document to `JournalSystem`, write WSM keys, open journal to this entry.
- On pickup: destroy world object (or leave it if re-readable — designer choice via `bool consumeOnRead`).

---

## W5-04 — `JournalSystem` + `JournalUI`

**Files:** `Scripts/Core/JournalSystem.cs`, `Scripts/UI/JournalUI.cs`

**How to build:**

`JournalSystem : MonoBehaviour, ISaveable`:
- `RunScopeTag.Profile` — journal persists across all runs.
- `List<ReadableDocumentSO> CollectedDocuments`.
- `AddDocument(doc)`: add if not already collected, fire `OnDocumentAdded`.

`JournalUI`:
- Tab-based: Quests (reads from `QuestManager`), Lore (reads from `JournalSystem`).
- Quest tab: lists active quests with objectives and status.
- Lore tab: lists collected documents, click to read full text.
- Already integrated with existing tab-opens menu system (`MenuController`).

---

## W5-05 — `AugmentSystem` Concrete Implementations

**Modify:** `Scripts/Cybernetics/` — `CyberneticController.cs` is the framework, already done. Now implement concrete SOs.

**How to build** (implement these three first, they cover all patterns):

**NightVisionAugment : CyberneticSO**:
- `CreateRuntime()` → `NightVisionRuntime`.
- `NightVisionRuntime : CyberneticRuntime, IBatteryDrainer, IVisibilityContributor`:
  - On equip: register with `BatterySystem`, register with `PlayerVisibility` as contributor (reduces enemy visibility detection of player? No — night vision helps *player* see, not reduces player visibility. Adjust the post-process `ColorAdjustments` and `Bloom` to simulate green tint).
  - `DrainRate`: higher than the flashlight.
  - `GetVisibilityFactor()`: 1.0 (night vision doesn't make you invisible).

**StaminaBoostAugment : CyberneticSO**:
- Pushes `PlayerStatModifier(StatType.StaminaDrain, value: 0.7f, IsMultiplier: true)` on equip, removes on unequip. No battery drain.

**ExoskeletonAugment : CyberneticSO**:
- Pushes `PlayerStatModifier(StatType.CarryCapacity, value: 1.3f)` on equip. Uses battery (exoskeleton joints draw power).

**Leave space for:** Each new augment is one file. The framework handles everything else: slot management, save/load, unequip on hub. Augments that drain battery register as `IBatteryDrainer`. Augments that change stats push `PlayerStatModifier`.

---

## W5-06 — `Melee`

**Files:** `Scripts/Combat/MeleeWeaponSO.cs`, `Scripts/Combat/MeleeController.cs`

**How to build:**

`MeleeWeaponSO : ItemSO`:
- `float damage`, `float attackCooldown`, `float range`, `NoiseProfileSO noiseProfile` (low radius — quiet kills).

`MeleeController : MonoBehaviour`:
- Activated when a `MeleeWeaponSO` is equipped via `EquipmentController`.
- `PerformAttack()`: `Physics.SphereCast` from camera forward for range. On hit: build `DamageContext { Type = DamageType.Melee, BaseDamage = data.damage, StimulusLoudness = 0.1f }`. Call `IDamageable.ApplyDamage`. `NoiseEmitter.Emit(data.noiseProfile)`.
- Input: right mouse button (or dedicated melee key) — check `GameInputState.GameplayBlocked`.

**Watch out for:** Melee and gun share the same input space (mouse buttons). If a gun is equipped, melee should be a secondary action (right-click = melee when no gun ADS is active). Design the input priority now.

---

## W5-07 — `ThrowableSystem`

**Files:** `Scripts/Combat/FlareItemSO.cs`, `Scripts/Combat/DistractionItemSO.cs`, `Scripts/Combat/ThrowableController.cs`

**How to build:**

`FlareItemSO : ItemSO`:
- `float burnDuration`, `float lightRadius`, `float lightIntensity`.
- When thrown: spawn flare prefab with a `LightSource` component (no battery drain — fuse-powered), duration timer, then despawn.
- Flare acts as `IVisibilityContributor` for any player standing in its radius (makes you visible).
- Flare does NOT broadcast a sound stimulus on throw — it's visible light, not noise.

`DistractionItemSO : ItemSO`:
- `NoiseProfileSO landingNoise`. When thrown and lands: `NoiseEmitter.Emit(noiseProfile)` at impact position — not player position. Draws AI to landing spot.

`ThrowableController : MonoBehaviour`:
- Reads throw input (G key or quick-throw).
- Arc preview (optional — Line Renderer showing predicted trajectory using physics prediction).
- On throw: remove item from inventory, spawn world object, apply physics force in throw direction.

---

## W5-08 — `EnemyTypes`

**Files:** `Scripts/AI/EnemyTypeSO.cs` — modify `BaseEnemyAI.cs`

**How to build:**

`EnemyTypeSO : ScriptableObject`:
- `string TypeName`, `float MaxHealth`, `float MoveSpeed`, `float ChargeSpeed`, `float AttackDamage`, `float AttackCooldown`, `float SightRange`, `float HearingMultiplier`.
- `TeamId Team`, `GameObject EnemyPrefab`.

`BaseEnemyAI` reads from its `EnemyTypeSO` reference instead of serialized fields. Existing serialized fields are replaced.

Built-in types to create as SO assets:
- **Guard**: medium health, patrols, only attacks if provoked.
- **Swarmer**: low health, fast, no patrol, charges instantly.
- **Blocker**: high health, slow, heavy melee, telegraphed as "come back later."

**Leave space for:** `Ranged` enemy type needs `GunController` on the enemy — deferred to after enemy combat is stable. When ready, `EnemyTypeSO` gets a `WeaponSO weaponReference` field.

---

## W5-09 — `HazardZones`

**Files:** `Scripts/World/HazardZone.cs`, `Scripts/World/HazardZoneSO.cs`

**How to build:**

`HazardZoneSO : ScriptableObject`:
- `string HazardName` ("Gas", "Radiation"), `float DamagePerSecond`, `DamageType Type`, `bool RequiresGear`, `string RequiredGearTag`.

`HazardZone : MonoBehaviour`:
- Trigger collider.
- `OnTriggerStay`: if player inside → `float resistance = playerStatModifierStack.Net(StatType.HazardResist)` → `ApplyDamage(DPS * (1-resistance) * Time.deltaTime)`.
- Optional: `DarknessStateVisual`-style post-processing overlay (green tint for gas, yellow for radiation).

**Gear resistance**: gas mask `ItemSO` pushes a `PlayerStatModifier(StatType.HazardResist, 0.9f)` while equipped in the correct slot. The HazardZone reads the net modifier — no hardcoded checks.

---

## W5-10 — `ShortcutSystem`

**File:** `Scripts/World/ShortcutOpener.cs`

**How to build:**
- `MonoBehaviour : IInteractable`.
- Field: `string shortcutId` (unique), `GameObject[] ObjectsToActivate` (doors, ladders, bridges that open).
- `CanInteract(g)`: `!WorldStateManager.GetBool($"shortcut.{shortcutId}.opened")`.
- `GetPrompt(g)`: `"Open Shortcut"`.
- `Interact(g)`: `WorldStateManager.SetBool($"shortcut.{shortcutId}.opened", true)` — World-scoped, persists across runs. Enable `ObjectsToActivate`.
- `Start()`: if WSM already true, enable objects immediately.

---

## W5-11 — `WallMarking`

**File:** `Scripts/World/WallMarker.cs`

**How to build:**
- A consumable `ItemSO` (chalk, spray can) in inventory.
- `IInteractable` on the wall surface: `Interact(g)` → place a decal at the raycast hit point.
- Decal uses Unity's `DecalProjector` component. Arrow/X/circle variants.
- Decals are Temp-scoped: cleared on new run.
- Maximum marks per run: configurable cap to avoid performance issues.

---

## W5-12 — `BlockedPaths`

**File:** `Scripts/World/DebrisPile.cs`

**How to build:**
- `MonoBehaviour : IInteractable`.
- `CanInteract(g)`: player has required item (e.g., crowbar `ItemSO` tag) OR a quest condition `WsmCondition` is met.
- `Interact(g)`: remove item if required, play clear animation, write WSM `"path.{id}.cleared"`, disable debris collider + mesh.
- World-scoped: persists after clearing.

---

## W5-13 — `KeypadCodeLocks`

**File:** `Scripts/World/KeypadDoor.cs`

**How to build:**
- Extends `LockedDoor` concept (or separate `IInteractable`).
- `Interact(g)`: open `KeypadUI` — a number pad UI panel.
- Code stored on `KeypadDoor` field (not in WSM — player must find the note with the code).
- On correct code: write `WSM door.{id}.unlocked = true`, close keypad, open door.
- On incorrect: play error sound.

---

## W5-14 — `CommitmentDrops`

**File:** `Scripts/World/CommitmentDropTrigger.cs`

**How to build:**
- Trigger collider at the top of a one-way drop shaft.
- `OnTriggerEnter(player)`: show a confirmation prompt — "This is a one-way drop. Continue?" (use `DialogueUI` or a simple confirm panel).
- On confirm: write WSM `"drop.{id}.used"`, disable the trigger (can't un-drop).
- Fall damage applies normally at the bottom.

---

## W5-15 — `MountedLight` + `Headlamp`

**Files:** `Scripts/World/MountedLight.cs`, `Scripts/World/Headlamp.cs`

**How to build:**

Both implement `ILightSource + IBatteryDrainer`. Both register with `BatterySystem` on `OnEnable`.

`MountedLight`:
- Parented to gun muzzle transform.
- Activated when a two-handed weapon is equipped (listens to `WeaponManager.OnWeaponEquipped`).
- Deactivated on holster.
- Cannot be used simultaneously with handheld flashlight (one-handed gun + flashlight OR two-handed + MountedLight).

`Headlamp`:
- Parented to camera (fixed forward cone).
- Equipped via `EquipmentController` slot `"headlamp"`.
- Higher drain rate than flashlight (hands-free premium).

**Watch out for:** The player should only be able to use one light source type at a time (or the battery drain stacks). Design the rule: handheld flashlight is auto-holstered when headlamp is equipped. `BatterySystem` handles the drain math — it sums all active drainers. If both are registered and active, it just drains faster. Make the UI clear about this.

---

## W5-16 — `TuningData` (`GameBalanceSO`)

**File:** `Scripts/Core/GameBalanceSO.cs`

**How to build:**
- One ScriptableObject with fields for every hardcoded balance value in the game.
- Create a singleton accessor: `GameBalance.Instance` reads from the asset loaded at runtime.
- Replace serialized fields on individual components with `GameBalance.Instance.{fieldName}` reads.

Fields to centralize: battery drain rates (dim/bright/headlamp/augment), encumbrance tier thresholds, stamina drain rates, noise radii per action, trader buy/sell spreads, enemy sight/hearing ranges, fall damage curve, restock intervals.

---

## W5-17 to W5-23 — Polish Items

These items are design-complete but architecturally simple. Each is self-contained and does not create dependencies for other systems.

- **WeaponMods/Attachments**: `WeaponAttachmentSO : ItemSO`, modifies `WeaponSO` stats at equip time via `PlayerStatModifier` pattern applied to weapon stats.
- **WeaponDurability/Jamming**: `float durability` on `WeaponItemInstance`. Decrements per shot. At 0: `GunController.CanFire()` returns false until repaired via trader.
- **CompassOrLandmarks**: A `CompassUI` HUD element showing cardinal direction. Simple `Camera.forward` to compass bearing math.
- **DistractionMechanic**: Already handled by `DistractionItemSO` in W5-07. Polish means tuning AI response radius and adding visual confirmation that AI was drawn.
- **Lockpicking**: `LockpickingUI` minigame. On success: same as key unlock. Probability of failure based on player stat or tool quality.
- **Vault/Mantle**: `PlayerMotor` extension. `SphereCast` forward for ledge, `if (ledgeDetected && jumpInput) StartMantleCoroutine()`. Blends arm IK during mantle.
- **AmbientAndMusic**: `AmbientAudioManager` with sector-specific ambient loops. Music system with `tension`, `safe`, and `danger` states driven by `AIPerception.State` of nearest enemy.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 5"

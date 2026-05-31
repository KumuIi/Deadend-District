# Wave 3 — The Threat

**What this wave delivers:** The threat is real. Enemies hear you, see you based on light, and punish mistakes. Shooting someone draws monsters. Being in the dark makes you hunted. Ladders give you vertical escape routes. Headshots matter. enemies push when they are hiding and hear you reloading.

**Prerequisite:** Wave 2 complete. RunLoop is working. You can do a full run and come back alive.

---

## W3-01 — `NoiseProfileSO` + `NoiseEmitter`

**Files:** `Scripts/Core/Stealth/NoiseProfileSO.cs`, `Scripts/Core/Stealth/NoiseEmitter.cs`

**How to build:**

`NoiseProfileSO : ScriptableObject`:
```csharp
[CreateAssetMenu(menuName = "Stealth/Noise Profile")]
public class NoiseProfileSO : ScriptableObject
{
    public float BaseRadius;  // Meters. Walk=4, Sprint=8, Gunshot=40, Reload=6
    public StimulusType StimulusType; // Usually Sound
    public float Intensity;   // 0..1
}
```

`NoiseEmitter : MonoBehaviour`:
- `Emit(NoiseProfileSO profile)`: applies `EncumbranceSystem.NoiseMult` (from `PlayerStatModifier` Net value) to `profile.BaseRadius`, then calls `StimulusSystem.Instance.Broadcast(stimulus)`.
- Called by: `FootstepAudio` (already emitting stimuli — migrate its broadcast to `NoiseEmitter.Emit`), `GunController` (gunshot), `InventoryUI` (drop item), `LootItemWorld` (pickup).

**Leave space for:** `DistractionMechanic` (Wave 5) — throwable items call `NoiseEmitter.Emit` at their landing position (not on the player). The `NoiseEmitter` doesn't care where the emitter is; the `Stimulus.Position` is set per-call.

**Watch out for:** This is mostly a migration and wrapper — `FootstepAudio` and `GunController` already broadcast stimuli. Migrate those calls to go through `NoiseEmitter.Emit` so encumbrance multiplier is applied consistently. Do not add a parallel noise path.

---

## W3-02 — `PlayerVisibility` + `VisibilitySystem`

**Files:** `Scripts/Core/Stealth/PlayerVisibility.cs`, `Scripts/Core/Stealth/VisibilitySystem.cs`

**How to build:**

`PlayerVisibility : MonoBehaviour`:
- `List<IVisibilityContributor> _contributors` — register/unregister in `OnEnable/OnDisable`.
- `float Score` property: `_contributors.Aggregate(1f, (acc, c) => acc * c.GetVisibilityFactor())` — multiplicative.
- Updated every `0.1s` (not every frame — visibility doesn't need frame-perfect accuracy).

`VisibilitySystem`:
- Not a separate MonoBehaviour. It's the pattern: any system that affects visibility implements `IVisibilityContributor` and registers with the player's `PlayerVisibility` component.

Built-in contributors to implement now:
1. `LightIntensityContributor`: samples nearby `LightSource` intensity using a `Physics.OverlapSphere` for active lights, then computes inverse-square falloff. Returns `[0..1]` where 1 = bright light directly on player.
2. `MovementVisibilityContributor`: reads `PlayerMotor.IsSprinting` → 1.0, walking → 0.7, crouching → 0.4, still → 0.2.
3. `CrouchVisibilityContributor`: (can merge with Movement) — `PlayerMotor.IsCrouching` halves the factor.

Update `AIPerception` to read `PlayerVisibility.Score` for sight detection threshold:
- Replace the current sight check's distance-based logic with: `detectedScore = PlayerVisibility.Score * (1 - normalizedDistance)`. Enter `Investigate` if score > `sightThreshold`.

**Leave space for:** `MountedLight` (Wave 5) creates a visibility contributor because pointing a weapon light at yourself while ADS near a mirror-wall would be a valid edge case. More practically: `HazardZone` smoke (Wave 5) will add a contributor that reduces visibility to zero inside gas clouds.

**Watch out for:** Light sampling is the tricky part. Unity doesn't have a `GetLightIntensityAtPoint` API. Simple approach: for each active `LightSource` in range, compute `intensity / (distance^2)`, sum, clamp to `[0..1]`. This works well for gameplay visibility without GPU reads.

---

## W3-03 — `MonsterAI`

**File:** `Scripts/AI/MonsterAI.cs`

**How to build:**
```csharp
public class MonsterAI : BaseEnemyAI, IStimulusListener
{
    public StimulusType[] ListensTo => new[] { StimulusType.Sound, StimulusType.Light };

    protected override void OnIdle()      { WanderToRandomPoint(); }
    protected override void OnCombat(Transform target) { ChargeTarget(target); }
    protected override void OnLostTarget() { SearchSpiral(_lastKnownPos); }

    public void OnStimulus(in Stimulus s)
    {
        // Any sound: transition to Investigate
        // Hunt trigger (radius=999): immediately Combat toward s.Position
        if (s.Radius >= 999f) ForceCombatAt(s.Position);
    }
}
```

`WanderToRandomPoint()`: pick a random `NavMesh.SamplePosition` within wander radius, `Agent.SetDestination`.  
`ChargeTarget()`: `Agent.speed = _chargeSpeed` (faster than guard), `Agent.SetDestination(target.position)` every `0.3s`.  
`SearchSpiral()`: sample a ring of positions at expanding radius, visit each with short timeout.

**Leave space for:** `EnemyTypeSO` (Wave 5) will define wander radius, charge speed, and attack damage in data. For now, serialize those fields directly on the component.

**Watch out for:** Monster must respond to the 999-radius hunt trigger from `DarknessStateWriter`. The `OnStimulus` handler checks `s.Radius >= 999f` as a sentinel. Consider using a dedicated `StimulusType.HuntTrigger` enum value instead if the sentinel value feels fragile — add the enum value to `Stimulus.cs`.

use the mimic preset its an orb that automatically places leg to closest object (meaning it can climb on walls and movefreely) make it attack by coming close.

give it behaviours like a sensetive creature, on shoot gets pushed backa little and is stunned for a second before charging double as fast for 1 second and returning to normal speed.
additionally the creature will switch height it travels since it can go on walls and stuff. (but make sure it does not exeed out of the walls or roof/floor).
on player notice it should make a sound (only use audio clips) and just make sounds while treversing on random from an array of sounds.(if possible not very loud but not to quite where its to late(you should be able to avoid it by hearing whre it is))
it can attack if close enough by dashin at the player. also make that it feels like some mimic creature where it shakes constantly(weard movement yet moves at the player)

---

## W3-04 — `EnemySpawnSystem`

**File:** `Scripts/World/EnemySpawnSystem.cs`

**How to build:**
- MonoBehaviour per sector scene.
- Collects all `EnemySpawnPoint` in scene on `Start`.
- `IRunLifecycleListener.OnRunStarted()`: spawns initial enemies up to density cap.
- Density cap: `[SerializeField] int maxEnemiesPerSector` — start low for testing.
- `IRunLifecycleListener.OnReturnedToHub()`: despawn all living enemies via `IPoolableSpawnedEntity.OnDespawned()`.
- Respawn: delegates to `EnemySpawnPoint`'s own respawn timer.

**Leave space for:** `EnemyTypeSO` (Wave 5) will let spawn points specify enemy type from a pool, not just a single prefab. For now, each spawn point references one prefab.

---

## W3-05 — `HitZone` + `GunController` migration to `DamageContext`

**Files:** `Scripts/Combat/HitZone.cs`, modify `Scripts/Gun/GunController.cs`

**How to build:**

`HitZone : MonoBehaviour`:
- Attach to child colliders of enemies (head, torso, limb — each gets a separate collider child + HitZone component).
- Fields: `string zoneId` ("head", "torso", "limb"), `float damageMultiplier` (head=2.5, torso=1.0, limb=0.7).
- No logic — just data carrier.

`GunController` migration:
- Current: raycast hit calls some `TakeDamage(float)` path.
- New: on raycast hit, build `DamageContext`:
  ```csharp
  var ctx = new DamageContext {
      Source = gameObject, Instigator = gameObject,
      HitPoint = hit.point, HitNormal = hit.normal,
      HitZoneId = hit.collider.GetComponent<HitZone>()?.zoneId ?? "",
      Type = DamageType.Bullet,
      BaseDamage = _currentAmmo.damage * falloffCurve.Evaluate(hit.distance),
      StimulusLoudness = 1.0f
  };
  var dmg = hit.collider.GetComponentInParent<IDamageable>();
  dmg?.ApplyDamage(ctx);
  ```
- `BaseEnemyAI.ApplyDamage(ctx)` reads `ctx.HitZoneId`, looks up multiplier from its `HitZone` components, applies.

**Leave space for:** `Melee` (Wave 5) fills `DamageContext` with `Type = DamageType.Melee` and `StimulusLoudness = 0.1f` — no other changes needed. `HazardZone` (Wave 5) fills with `Type = DamageType.Hazard` and `StimulusLoudness = 0f`.

**Watch out for:** The child colliders for hit zones must be on a layer that `GunController`'s raycast hits. Add `Enemy` to the hit layer mask. Body-part colliders should not block NavMeshAgent pathing — set them to `IgnoreRaycast` physics layer if they interfere, or use trigger colliders.

---

## W3-06 — `LadderClimbing`

**Files:** `Scripts/World/Ladder.cs`, modify `Scripts/PlayerMovement/PlayerMotor.cs`

**How to build:**

`Ladder : MonoBehaviour, IInteractable`:
- Trigger collider covering the ladder volume.
- `CanInteract(g)`: player is near bottom or top of ladder (within 1m of bottom entry or top entry points).
- `GetPrompt(g)`: `"Climb"` or `"Descend"`.
- `Interact(g)`: call `PlayerMotor.Instance.EnterLadderMode(this)`.

`PlayerMotor` additions:
- `EnterLadderMode(Ladder ladder)`: disable gravity, disable horizontal movement, snap player X/Z to ladder axis, set state `IsOnLadder = true`.
- `ExitLadderMode()`: re-enable gravity, re-enable horizontal, state = grounded.
- Vertical input: `W/S` → `rb.MovePosition(transform.position + Vector3.up * ladderSpeed * Time.deltaTime)`.
- Auto-dismount: if player reaches `ladder.TopPoint` or `ladder.BottomPoint` (Transform references on the Ladder component), call `ExitLadderMode()`.
- Jump input while on ladder: `ExitLadderMode()` + apply a small jump impulse.

**Leave space for:** Stamina drain per meter climbed: `PlayerHealth.UseEnergy(energyPerMeter * verticalInput)` inside the ladder movement code.

**Watch out for:** `GunController` fires input (`GameInputState.FirePressed`) — make sure `IsOnLadder` blocks firing or at least ADS. Add a check in `GunController.CanFire()`: `if (PlayerMotor.Instance.IsOnLadder) return false;`

---

## W3-07 — NavMesh Off-Mesh Links for Enemy Ladder Traversal

**No new scripts.** Editor setup pass.

**How to build:**
- On each `Ladder` GameObject: add a `NavMeshLink` component (Unity's NavMesh Surface package).
- Start point = ladder bottom, End point = ladder top, Width = agent width.
- Set bi-directional if enemies can go up and down.
- `BaseEnemyAI` does not need special code — NavMeshAgent traverses off-mesh links automatically.
- Bake NavMesh surfaces to include the new links.

**Watch out for:** Player ladder climbing is NOT via NavMesh off-mesh links. It's a custom `PlayerMotor` mode. Enemy traversal IS via NavMesh. Keep these separate — they're different movement systems.

---

## W3-08 — `FallDamage`

**File:** Modify `Scripts/PlayerMovement/PlayerMotor.cs` (add fall tracking) and `Scripts/Player/PlayerHealth.cs` is the target.

**How to build:**
- In `PlayerMotor`: track `_fallStartY` when leaving ground. On landing: `float fallDistance = _fallStartY - transform.position.y`.
- If `fallDistance > _minDamageHeight`: build `DamageContext { Type = DamageType.Fall, BaseDamage = fallDamageCurve.Evaluate(fallDistance), StimulusLoudness = 0.2f }`.
- Call `playerHealth.ApplyDamage(ctx)` and `NoiseEmitter.Emit(landingNoiseProfile)` (heavy landing makes noise).
- `fallDamageCurve`: AnimationCurve on `PlayerMovementConfig` SO — no damage below 3m, linear ramp 3–8m, max damage at 8m+.

**Leave space for:** `CommitmentDrops` (Wave 5): same fall damage applies. The drops are just geometry choices — the damage code is already here.

**Watch out for:** `PlayerMotor` already has airborne tracking (`_airborneTime`). Reuse that data — don't add a second airborne tracker. Record `_fallStartY = transform.position.y` on the frame that `IsGrounded` goes false, clear it on landing.

---

## Wave-End Check → See `RULEBOOK.md` "After Wave 3"

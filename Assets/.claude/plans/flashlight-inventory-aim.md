# Plan: Flashlight Inventory Aim

## Task
When the inventory opens, redirect the flashlight beam to illuminate inventory items.
When inventory closes, restore the beam to its natural orientation.
User has already placed the flashlight under a pivot point GO in the scene.

## Acceptance Criteria
- Flashlight light points at inventory items when inventory is open
- FlashlightSway (hand sway/bob/dip) is unaffected — it still applies in local space under the pivot
- Inventory stays dark if no flashlight is equipped or light is off
- Beam restores correctly on inventory close
- Cleanup guards prevent stuck state on Unequip, OnDisable, OnDestroy

## Architecture

### Transform hierarchy (as set up by user)
```
BeamPivot GO        (empty, parent — this is what we rotate)
  └── Flashlight root GO  (FlashlightSway writes localPosition/localRotation here)
       └── LightSource GO  (LightSource + Light components)
       └── HoldPos / gripTarget
```

FlashlightSway writes flashlightRoot.localRotation (local relative to pivot) in LateUpdate.
We write beamPivot.rotation (world space) in FlashlightSlot.LateUpdate.
These are DIFFERENT GameObjects — no execution order conflict.

### Why this is clean
- Rotating the pivot in world space does not affect any localRotation in the hierarchy
- FlashlightSway's spring system runs relative to the pivot in local space — still correct
- No DefaultExecutionOrder needed; pivot rotation and sway local rotation are independent

## Changes

### FlashlightSlot.cs
1. Add SerializeField: `[SerializeField] private Transform _beamPivot`
2. Add private fields:
   - `Transform _inventoryAimTarget`
   - `bool _inventoryAimActive`
   - `Quaternion _savedPivotLocalRot`
3. Add `public void BeginInventoryAim(Transform aimTarget)`:
   - Guard: if _beamPivot == null, return
   - Save `_beamPivot.localRotation` → `_savedPivotLocalRot`
   - Set `_inventoryAimTarget = aimTarget`
   - Set `_inventoryAimActive = true`
4. Add `public void EndInventoryAim()`:
   - Guard: if !_inventoryAimActive, return
   - Set `_inventoryAimActive = false`
   - Restore `_beamPivot.localRotation = _savedPivotLocalRot`
   - Clear `_inventoryAimTarget = null`
5. In existing `LateUpdate()` (or new one): when `_inventoryAimActive && _inventoryAimTarget != null && _beamPivot != null`:
   - `Vector3 dir = (_inventoryAimTarget.position - _beamPivot.position).normalized`
   - If dir != Vector3.zero: `_beamPivot.rotation = Quaternion.LookRotation(dir)`
   - Note: only redirect when light IsOn (so inventory stays dark when light is off)
6. In `Unequip()`: call `EndInventoryAim()` as cleanup guard
7. In `OnDisable()`: call `EndInventoryAim()` as cleanup guard
8. In `OnValidate()`: warn if `_beamPivot == null`

### InventoryUI.cs
1. Add SerializeField under Drop Settings header:
   `[SerializeField] private Transform _inventoryLightTarget`
   Tooltip: "Empty Transform positioned where inventory items sit in world space — flashlight aims here when inventory is open."
2. In `SetOpen(bool open)`:
   - `if (open) flashlightSlot?.BeginInventoryAim(_inventoryLightTarget);`
   - `else flashlightSlot?.EndInventoryAim();`
3. In `OnValidate()` (or Awake warnings): soft warning if `_inventoryLightTarget == null`

## Scene setup (user does manually)
- The BeamPivot GO is already created and the flashlight is under it
- Create empty GameObject "InventoryLightTarget" positioned in the approximate world space
  where inventory items render (roughly 0.5m in front of the camera, centered on the grid)
- Assign BeamPivot to FlashlightSlot._beamPivot
- Assign InventoryLightTarget to InventoryUI._inventoryLightTarget

## Edge cases handled
- Flashlight off: pivot is redirected but LightSource.IsOn == false → inventory stays dark. Correct.
- No flashlight equipped: BeginInventoryAim just saves pivot rot and aims it. Light is off (unequipped). Correct.
- Unequip while inventory open: Unequip() calls EndInventoryAim() → pivot restored. Inventory goes dark. Correct.
- Battery depleted while inventory open: ForceOff() cuts light → inventory goes dark, aim continues (harmless). Correct.
- _inventoryLightTarget not assigned: null guard on `BeginInventoryAim` call (null-conditional). LateUpdate dir check still guards against zero vector.

# FlashlightSway + Battery Removal + LowBatteryWarning Plan

## 1. NEW: Scripts/World/FlashlightSway.cs
Spring-physics sway on the flashlight GO. Two layers: rotation lag + walk bob.

Fields:
- [SerializeField] FlashlightSlot _flashlightSlot  — gate: only runs when equipped
- [SerializeField] PlayerMotor _playerMotor
- [SerializeField] float _stiffness = 8f            — spring pull strength
- [SerializeField] float _damping   = 4f            — lower = more overshoot
- [SerializeField] float _mouseInfluence  = 2f      — how much look input contributes
- [SerializeField] float _velocityInfluence = 1.5f  — how much walk velocity contributes
- [SerializeField] float _maxAngle  = 12f           — clamp on rotation offset
- [SerializeField] float _bobAmplitude = 0.004f     — gentle position bob amplitude
- [SerializeField] float _bobFrequency = 1.8f       — bob cycles per second

State:
- _baseLocalPos, _baseLocalRot captured in Start()
- _currentRot (Vector3), _rotVelocity (Vector3) — spring state
- _currentPos (Vector3), _posVelocity (Vector3) — position lag
- _bobTimer (float)

LateUpdate():
- Gate: if FlashlightSlot.EquippedFlashlight == null, lerp back to base and return
- dt = Mathf.Min(Time.deltaTime, 0.033f)
- Mouse target: new Vector3(-mouseY * _mouseInfluence, mouseX * _mouseInfluence, 0)
- Velocity target: new Vector3(-localVel.x, 0, 0) * _velocityInfluence (camera-local)
- Combined target clamped to _maxAngle
- Spring: _rotVelocity += (target - _currentRot) * _stiffness * dt
           _rotVelocity *= (1f - _damping * dt)
           _currentRot  += _rotVelocity * dt
- Bob: advance _bobTimer when grounded+moving, sine on Y axis
- Apply: transform.localPosition = _baseLocalPos + bobOffset + posLag
          transform.localRotation = _baseLocalRot * Quaternion.Euler(_currentRot)

## 2. MODIFY: Scripts/Inventory/FlashlightItemInstance.cs
Add InsertedBattery tracking (mirrors LoadedMagazine on WeaponItemInstance).

- ADD: public BatteryItemInstance InsertedBattery { get; private set; }
- MODIFY SwapWith(): store battery ref: InsertedBattery = battery (before charge transfer)
- ADD: public BatteryItemInstance EjectBattery()
    — copies CurrentCharge back to InsertedBattery.CurrentCharge
    — sets CurrentCharge = 0f
    — clears InsertedBattery = null
    — returns the battery instance

## 3. MODIFY: Scripts/Inventory/InventoryContextMenu.cs
- ADD: public Action<ItemInstance> OnRemoveBattery;
- In Show(), flashlight branch: if equipped AND fi.InsertedBattery != null
    → add "Remove Battery" entry calling OnRemoveBattery

## 4. MODIFY: Scripts/Inventory/InventoryUI.cs
- Wire: _contextMenu.OnRemoveBattery = ContextMenu_RemoveBattery;
- ADD ContextMenu_RemoveBattery(ItemInstance item):
    — cast to FlashlightItemInstance
    — attempt TryPickup of InsertedBattery BEFORE ejecting (check space)
    — if NoSpace: log warning, return (battery stays in flashlight)
    — if Placed: call fi.EjectBattery(), remove battery from grid first

Wait — EjectBattery transfers charge back to battery then returns it. TryPickup the
returned instance. Flow:
    1. battery = fi.EjectBattery()       — charge transferred, CurrentCharge=0
    2. if TryPickup(battery) == NoSpace: fi.SwapWith(battery)  — re-insert it back

## 5. MODIFY: Scripts/Player/LowBatteryWarning.cs
New behavior: audio plays ONCE on threshold, flicker for 1 second, then SetMode(Dim).

- REMOVE: _audioSource.loop = true
- CHANGE StartWarning():
    — AudioSource.PlayOneShot(_warningClip) instead of Play()
    — Start FlickerRoutine as before (1 second duration, then stop)
    — After flicker ends: LightSource?.SetMode(LightMode.Dim)
- FlickerRoutine becomes timed (WaitForSeconds total budget of 1f):
    — track elapsed, loop flicker until elapsed >= 1f
    — after loop: call LightSource?.SetMode(LightMode.Dim)
    — nullify _flickerRoutine
- StopWarning(): stop coroutine, restore light to IsOn state (don't force dim off)
- OnDepleted: ForceOff (unchanged)

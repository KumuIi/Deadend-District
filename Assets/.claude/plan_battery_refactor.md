# Battery/Flashlight Refactor Plan

## Goal
Replace BatterySystem singleton with weapon-pattern: charge on FlashlightItemInstance,
FlashlightSlot owns drain loop and events, flashlight pre-placed in scene.

## DELETE (2 files)
- Scripts/Core/Battery/BatterySystem.cs + .meta
- Scripts/Core/Battery/ILightSource.cs + .meta

## MODIFY

### 1. Scripts/Inventory/FlashlightSO.cs
- ADD: `[Min(0f)] public float maxCharge = 100f;`
- REMOVE: `public GameObject flashlightPrefab;` and its OnValidate check

### 2. Scripts/Inventory/FlashlightItemInstance.cs
- ADD: `public float CurrentCharge;`
- ADD: `public float MaxCharge => FlashlightDef.maxCharge;`
- ADD: `public float ChargeNormalized => MaxCharge > 0f ? CurrentCharge / MaxCharge : 0f;`
- ADD: `public bool IsDepleted => CurrentCharge <= 0f;`
- ADD: constructor sets `CurrentCharge = definition.maxCharge;`
- ADD: `public void SwapWith(BatteryItemInstance battery)` — transfers min(MaxCharge, battery.CurrentCharge) into CurrentCharge, sets battery.CurrentCharge = 0f

### 3. Scripts/Player/FlashlightSlot.cs (major rewrite)
- REMOVE: runtime prefab instantiation (Instantiate/Destroy)
- ADD: `[SerializeField] private FlashlightView _flashlightView;` — direct scene ref
- ADD: `[SerializeField] private GameObject _flashlightGO;` — the pre-placed GO to show/hide
- KEEP: WeaponManager ref, IEquipmentSlot, OnWeaponEquipped handler, IK logic
- TryEquip: no Instantiate — just store _equipped, show GO if weapon allows, override IK
- Unequip: hide GO, clear _equipped, restore IK
- ADD: Update() drain loop — reads LightSource.DrainRate, subtracts from EquippedItem.CurrentCharge * Time.deltaTime, clamps to 0, forces light off when depleted
- ADD events: `public event Action<float> OnChargeChanged;`  `public event Action OnDepleted;` `public event Action OnRestored;`
- ADD: `public float ChargeNormalized` property
- ADD: `public void SwapBattery(BatteryItemInstance battery)` — calls EquippedItem.SwapWith(battery), fires OnRestored if was depleted

### 4. Scripts/World/LightSource.cs
- REMOVE: ILightSource, IBatteryDrainer interface implementations
- REMOVE: OnEnable/OnDisable BatterySystem registration
- REMOVE: HandleBatteryDepleted
- MOVE: LightMode enum definition INTO this file (was in ILightSource.cs)
- KEEP: Toggle(), SetMode(LightMode), FlickerVisual(bool), DrainRate property, IsOn, CurrentMode
- ADD: `public void ForceOff()` — called by FlashlightSlot when depleted

### 5. Scripts/Player/LowBatteryWarning.cs
- REMOVE: BatterySystem.Instance subscriptions
- ADD: `[SerializeField] private FlashlightSlot _flashlightSlot;` (already has this)
- Subscribe to `_flashlightSlot.OnChargeChanged` and `_flashlightSlot.OnDepleted`

### 6. Scripts/Player/DarknessStateWriter.cs
- REMOVE: BatterySystem.Instance subscriptions
- ADD: `[SerializeField] private FlashlightSlot _flashlightSlot;`
- Subscribe to `_flashlightSlot.OnDepleted` and `_flashlightSlot.OnRestored`

### 7. Scripts/Inventory/InventoryUI.cs
- CHANGE: battery drag interaction — `flashlightSlot.SwapBattery(battery)` instead of `BatterySystem.Instance.SwapBattery(battery)`

### 8. Scripts/Inventory/InventoryTooltip.cs
- CHANGE: FlashlightItemInstance case — read `fi.ChargeNormalized` directly (always show, no IsItemEquipped check needed for charge value)
- REMOVE: BatterySystem.Instance reference

### 9. Scripts/UI/PlayerHUD.cs (HIGH fix)
- REMOVE: `public BatterySystem batterySystem;`
- ADD: `public FlashlightSlot flashlightSlot;`
- CHANGE Update(): `_batteryFill.fillAmount = flashlightSlot != null ? flashlightSlot.ChargeNormalized : 0f;`

### 10. Scripts/Core/SaveSystem/Adapters/BatterySaveAdapter.cs → repurposed as FlashlightSaveAdapter
- RENAME class to FlashlightSaveAdapter, filename stays BatterySaveAdapter.cs
- REMOVE BatterySystem dependency
- ADD: `[SerializeField] private FlashlightSlot _flashlightSlot;`
- CaptureSaveData: saves `_flashlightSlot?.EquippedFlashlight?.CurrentCharge ?? -1f`
- RestoreSaveData: sets `_flashlightSlot.EquippedFlashlight.CurrentCharge = saved` if valid

### 11. Scripts/Player/DarknessStateWriter.cs (initial-state sync fix)
- After subscribing to events: `if (flashlightSlot.IsDepleted) HandleDepleted();`

## NO CHANGES NEEDED
- DarknessStateVisual.cs (reads WorldStateManager only)
- BatteryItemSO.cs (physical battery items — unchanged)
- BatteryItemInstance.cs (physical battery items — unchanged)
- InventoryContextMenu.cs
- FlashlightView.cs (already clean — has LightSource ref and gripTarget)
- ItemInstanceFactory.cs

## Scene Setup (manual, after code compiles)
1. Place flashlight model GO under Guns GO in scene (pre-placed, like guns)
2. Add FlashlightView component to it
3. Assign FlashlightView and flashlight GO to FlashlightSlot in Inspector
4. Remove BatterySystem GO from scene
5. Create FlashlightSO asset via Create > Deadend/Items/Flashlight (no prefab field)
6. Assign correct FlashlightSO to InventoryTester.testItems

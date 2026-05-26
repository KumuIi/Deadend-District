# Reload Dip Animation — Implementation Plan

## Acceptance Criteria
- Pressing R with a valid magazine dips the gun off-screen and plays reload audio
- Dip does NOT fire on: inventory drag-drop mag swaps, Start() auto-load, or any non-R-key StartReload call
- Flashlight (if equipped) dips the same amount
- All dip tuning is per-weapon on WeaponSO

## Files Modified

### 1. Scripts/Gun/WeaponSO.cs
Add under the Reload header:
```
[Header("=== Reload Feel ===")]
public AudioClip reloadClip;
public float reloadDipDepth = -0.8f;
public float reloadDipDownTime = 0.25f;
public float reloadDipReturnTime = 0.2f;
public float reloadDipAudioThreshold = -0.25f;  // plays audio once dip passes this Y offset
```

### 2. Scripts/Gun/GunController.cs
- Add `public event Action OnReloadStarted` and `public event Action OnReloadFinished`
- Change `StartReload(MagazineInstance newMag = null)` → `StartReload(MagazineInstance newMag = null, bool playerInitiated = false)`
- In `HandleReloadInput()` non-inventory path: pass `playerInitiated: true`
- Pass `playerInitiated` into `ReloadCoroutine`
- In `ReloadCoroutine`: fire `OnReloadStarted` after `IsReloading = true` only if `playerInitiated`
- Fire `OnReloadFinished` before `IsReloading = false` only if `playerInitiated`

### 3. Scripts/Inventory/InventoryUI.cs
- In the `OnReloadRequested` callback (line ~476): change `gun.StartReload(bestMag.RuntimeMag)` → `gun.StartReload(bestMag.RuntimeMag, playerInitiated: true)`

### 4. Scripts/Gun/ReloadDip.cs (NEW)
```csharp
[DefaultExecutionOrder(10010)]
public sealed class ReloadDip : MonoBehaviour
{
    [SerializeField] GunController _gun;
    [SerializeField] Transform _gunPivot;

    public float CurrentDipOffset { get; private set; }

    private float _dipVelocity;
    private float _dipTarget;
    private bool _audioPlayed;
    private AudioSource _audio;

    void Awake()  → get AudioSource from _gun
    OnEnable()    → subscribe OnReloadStarted / OnReloadFinished
    OnDisable()   → unsubscribe
    OnReloadStarted → _dipTarget = weaponData.reloadDipDepth; _audioPlayed = false
    OnReloadFinished → _dipTarget = 0f
    LateUpdate    → SmoothDamp CurrentDipOffset → _dipTarget
                 → if !_audioPlayed && CurrentDipOffset <= weaponData.reloadDipAudioThreshold → PlayOneShot(reloadClip), _audioPlayed = true
                 → gunPivot.localPosition += Vector3.up * CurrentDipOffset
}
```

### 5. Scripts/Player/FlashlightSlot.cs
- Add `[SerializeField] ReloadDip _reloadDip`
- In `Update()`, after existing drain logic: if `_flashlightGO.activeSelf && _reloadDip != null`, apply `_reloadDip.CurrentDipOffset` to `_flashlightGO.localPosition` Y (store original pos, add offset)

## Execution Order
WeaponWallPushback LateUpdate (-100) → GunSway LateUpdate (0) → GunController LateUpdate (10000) → ReloadDip LateUpdate (10010)
ReloadDip runs last, additively offsets final gunPivot position.

## What is NOT changed
- Inventory drag-drop (InsertMagazine path) — no StartReload call, no dip
- Start() auto-load debug path — playerInitiated defaults to false, no dip

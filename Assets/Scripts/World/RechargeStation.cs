using UnityEngine;

/// <summary>
/// Hub interactable that refills every rechargeable battery the player carries — loose cells in
/// the inventory grid, batteries inserted in flashlights, and the equipped flashlight. Place on a
/// station GameObject on the Interactable physics layer so PlayerInteractor finds it.
///
/// There is no BatterySystem singleton — batteries are inventory items (BatteryItemInstance),
/// so the station scans the player's InventoryUI grid directly. One-time cells are ignored.
/// </summary>
public class RechargeStation : MonoBehaviour, IInteractable
{
    [Header("=== References ===")]
    [Tooltip("The player's inventory — scanned for rechargeable batteries and flashlights.")]
    [SerializeField] private InventoryUI _playerInventoryUI;
    [Tooltip("Equipped-flashlight slot — used to refresh the HUD after recharging it.")]
    [SerializeField] private FlashlightSlot _flashlightSlot;

    [Header("=== Feedback ===")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _rechargeSound;

    [Tooltip("WSM flag set true the first time the station is used (quests / tutorial hooks).")]
    [SerializeField] private string _usedKey = "hub.recharge_station.used";

    // ── IInteractable ──────────────────────────────────────────────────────

    public bool CanInteract(GameObject interactor) => HasAnythingToRecharge();

    public string GetPrompt(GameObject interactor) => "Recharge Batteries";

    public void Interact(GameObject interactor)
    {
        int recharged = RechargeAll();
        if (recharged == 0) return;

        WorldStateManager.Instance?.SetBool(_usedKey, true);

        if (_audioSource != null && _rechargeSound != null)
            _audioSource.PlayOneShot(_rechargeSound);

        Debug.Log($"[RechargeStation] Recharged {recharged} item(s).");
    }

    // ── Scan ───────────────────────────────────────────────────────────────

    private bool HasAnythingToRecharge()
    {
        if (_playerInventoryUI == null) return false;

        foreach (var item in _playerInventoryUI.Grid.PlacedItems)
        {
            if (item is BatteryItemInstance batt && BatteryNeedsCharge(batt)) return true;
            if (item is FlashlightItemInstance fl && FlashlightNeedsCharge(fl)) return true;
        }
        return false;
    }

    private int RechargeAll()
    {
        if (_playerInventoryUI == null) return 0;

        int count = 0;
        // Snapshot is unnecessary — recharging mutates item state, not the grid collection.
        foreach (var item in _playerInventoryUI.Grid.PlacedItems)
        {
            if (item is BatteryItemInstance batt)
            {
                if (RechargeBattery(batt)) count++;
            }
            else if (item is FlashlightItemInstance fl)
            {
                if (RechargeFlashlight(fl)) count++;
            }
        }
        return count;
    }

    // ── Per-item rules ───────────────────────────────────────────────────────

    private static bool BatteryNeedsCharge(BatteryItemInstance b) =>
        b.BatteryType == BatteryType.Rechargeable && b.CurrentCharge < b.MaxCharge;

    private static bool FlashlightNeedsCharge(FlashlightItemInstance fl)
    {
        var b = fl.InsertedBattery;
        return b != null && b.BatteryType == BatteryType.Rechargeable &&
               (fl.CurrentCharge < fl.MaxCharge || b.CurrentCharge < b.MaxCharge);
    }

    private static bool RechargeBattery(BatteryItemInstance b)
    {
        if (!BatteryNeedsCharge(b)) return false;
        b.CurrentCharge = b.MaxCharge;
        return true;
    }

    private bool RechargeFlashlight(FlashlightItemInstance fl)
    {
        if (!FlashlightNeedsCharge(fl)) return false;

        // Set BOTH the live flashlight charge and the inserted battery so an eject stays consistent.
        fl.InsertedBattery.CurrentCharge = fl.InsertedBattery.MaxCharge;
        fl.CurrentCharge                 = fl.MaxCharge;

        // If this flashlight is currently equipped, refresh the HUD bar + un-deplete the light.
        if (_flashlightSlot != null && _flashlightSlot.EquippedFlashlight == fl)
            _flashlightSlot.OnBatteryLoaded(fl);

        return true;
    }
}

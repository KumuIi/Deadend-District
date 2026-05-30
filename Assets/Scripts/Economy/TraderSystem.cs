using System.Collections.Generic;
using System.Linq; // PlacedItems is IReadOnlyCollection — Contains() is the LINQ extension
using UnityEngine;

/// <summary>
/// A hub trader the player talks to to buy gear and sell loot. Place on an NPC GameObject on
/// the Interactable physics layer (so PlayerInteractor finds it).
///
/// Access is hub-only (gated on the WSM key "zone.hub.active") — mirrors StashSystem. Stock
/// counts live in a runtime array copied from the TraderSO on Awake, so the asset is never
/// mutated. Restock is driven by RunManager: every Nth return to the hub the stock refills.
///
/// All credit movement goes through CurrencyService; all item movement goes through the player's
/// InventoryUI. TraderSystem holds no inventory grid of its own — the "stock" is just data.
/// </summary>
public class TraderSystem : MonoBehaviour, IInteractable, IRunLifecycleListener
{
    [Header("=== Definition ===")]
    [SerializeField] private TraderSO _trader;

    [Header("=== References ===")]
    [Tooltip("The player's inventory — items bought land here, sold items are pulled from here.")]
    [SerializeField] private InventoryUI _playerInventoryUI;
    [Tooltip("The trader UI panel opened on interact.")]
    [SerializeField] private TraderUI _traderUI;

    [Header("=== Access ===")]
    [Tooltip("WSM key that must be true for the trader to be usable. Written by the hub zone trigger.")]
    [SerializeField] private string _hubActiveKey = "zone.hub.active";
    [Tooltip("Bypass the hub gate — useful for testing before the hub zone trigger exists.")]
    [SerializeField] private bool _ignoreHubGate;

    /// <summary>One purchasable offer: a stock entry plus its current remaining count.</summary>
    public readonly struct Offer
    {
        public readonly int    StockIndex;
        public readonly ItemSO Item;
        public readonly int    BuyPrice;
        public readonly int    Remaining;   // -1 = unlimited
        public Offer(int index, ItemSO item, int buyPrice, int remaining)
        {
            StockIndex = index; Item = item; BuyPrice = buyPrice; Remaining = remaining;
        }
        public bool Unlimited => Remaining < 0;
        public bool InStock   => Unlimited || Remaining > 0;
    }

    public TraderSO Definition => _trader;
    public bool     IsOpen { get; private set; }

    private int[] _stockRemaining;   // mirrors _trader.Stock; -1 means unlimited
    private int   _runsUntilRestock;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        InitStock();
    }

    private void OnEnable()  => RunManager.Instance?.RegisterListener(this);
    private void OnDisable() => RunManager.Instance?.UnregisterListener(this);

    private void InitStock()
    {
        if (_trader == null || _trader.Stock == null)
        {
            _stockRemaining = System.Array.Empty<int>();
            return;
        }

        _stockRemaining = new int[_trader.Stock.Length];
        for (int i = 0; i < _stockRemaining.Length; i++)
        {
            int count = _trader.Stock[i].StockCount;
            _stockRemaining[i] = count <= 0 ? -1 : count; // 0 in the asset means "unlimited"
        }
        _runsUntilRestock = _trader.RestockIntervalRuns;
    }

    // ── IInteractable ──────────────────────────────────────────────────────

    public bool CanInteract(GameObject interactor) => _trader != null && CanAccess() && !IsOpen;

    public string GetPrompt(GameObject interactor) =>
        _trader != null ? $"Talk to {_trader.TraderName}" : "Trader";

    public void Interact(GameObject interactor) => Open();

    private bool CanAccess()
    {
        if (_ignoreHubGate) return true;
        var wsm = WorldStateManager.Instance;
        return wsm != null && wsm.GetBool(_hubActiveKey);
    }

    // ── Open / Close ───────────────────────────────────────────────────────

    public void Open()
    {
        if (IsOpen) return;
        if (!CanAccess()) { Debug.Log("[TraderSystem] Traders are only available in the hub."); return; }
        if (_traderUI == null) { Debug.LogError("[TraderSystem] TraderUI not assigned.", this); return; }

        IsOpen = true;
        _traderUI.Open(this);
    }

    /// <summary>Called by TraderUI's close button (and on the UI being force-closed).</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _traderUI?.NotifyClosed();
    }

    // ── Stock query ────────────────────────────────────────────────────────

    /// <summary>Current purchasable offers (sold-out limited entries are omitted).</summary>
    public List<Offer> GetOffers()
    {
        var offers = new List<Offer>();
        if (_trader?.Stock == null) return offers;

        for (int i = 0; i < _trader.Stock.Length; i++)
        {
            var entry = _trader.Stock[i];
            if (entry.Item == null) continue;

            int remaining = _stockRemaining[i];
            if (remaining == 0) continue; // limited entry, sold out this cycle

            offers.Add(new Offer(i, entry.Item, entry.BuyPrice, remaining));
        }
        return offers;
    }

    // ── Buy ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Buys one of the stock entry at <paramref name="stockIndex"/>. Returns false (charging
    /// nothing) if out of stock, unaffordable, or there is no inventory space. We place the item
    /// first — only on a successful placement do we charge — so a failed buy never loses credits.
    /// </summary>
    public bool TryBuy(int stockIndex)
    {
        if (_trader?.Stock == null || stockIndex < 0 || stockIndex >= _trader.Stock.Length) return false;
        if (_playerInventoryUI == null) { Debug.LogError("[TraderSystem] No player InventoryUI assigned.", this); return false; }

        var entry = _trader.Stock[stockIndex];
        if (entry.Item == null) return false;

        int remaining = _stockRemaining[stockIndex];
        if (remaining == 0) return false;                       // sold out
        if (!CurrencyService.CanAfford(entry.BuyPrice)) return false;

        var instance = ItemInstanceFactory.Create(entry.Item);
        if (instance == null) return false;

        // Place first — if there's no room, nothing is charged.
        if (_playerInventoryUI.TryPickup(instance) != PickupResult.Placed) return false;

        CurrencyService.Spend(entry.BuyPrice);
        if (remaining > 0) _stockRemaining[stockIndex] = remaining - 1; // unlimited (-1) stays put
        return true;
    }

    // ── Sell ───────────────────────────────────────────────────────────────

    /// <summary>What the trader pays for <paramref name="item"/>. 0 = won't buy it.</summary>
    public int GetSellPrice(ItemInstance item)
    {
        if (_trader == null || item?.data == null) return 0;

        int baseValue = item.data.baseValue;
        if (baseValue <= 0) return 0;

        if (!_trader.BuysAnything && !StocksItem(item.data)) return 0;

        return Mathf.Max(1, Mathf.RoundToInt(baseValue * _trader.SellFraction));
    }

    /// <summary>Sells <paramref name="item"/> from the player inventory for its sell price.</summary>
    public bool TrySell(ItemInstance item)
    {
        if (item == null || _playerInventoryUI == null) return false;
        if (!_playerInventoryUI.Grid.PlacedItems.Contains(item)) return false;

        int price = GetSellPrice(item);
        if (price <= 0) return false;

        // RemoveItemAndDetach (not RemoveItem) so selling the ACTIVE weapon/flashlight also
        // unequips the live gun/light — otherwise it stays usable after the item is gone.
        _playerInventoryUI.RemoveItemAndDetach(item);
        CurrencyService.Add(price);
        return true;
    }

    private bool StocksItem(ItemSO item)
    {
        foreach (var e in _trader.Stock)
            if (e.Item == item) return true;
        return false;
    }

    // ── IRunLifecycleListener (restock) ────────────────────────────────────

    public void OnRunStarted()  { }
    public void OnRunExtracted() { }
    public void OnRunDied()      { }

    public void OnReturnedToHub()
    {
        if (_trader == null || _trader.RestockIntervalRuns <= 0) return; // never restocks

        _runsUntilRestock--;
        if (_runsUntilRestock > 0) return;

        InitStock(); // refill counts and reset the timer
        Debug.Log($"[TraderSystem] '{_trader.TraderName}' restocked.");

        // If the player is staring at the shop when it restocks, refresh the view.
        if (IsOpen) _traderUI?.Refresh();
    }
}

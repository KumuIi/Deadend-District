using System;
using System.Collections.Generic;
using System.Linq; // PlacedItems is IReadOnlyCollection — Contains() is the LINQ extension
using UnityEngine;

/// <summary>
/// A hub trader the player talks to to buy gear and sell loot. Place on an NPC GameObject on
/// the Interactable physics layer so PlayerInteractor finds it.
///
/// UI is the existing InventoryUI grid — no bespoke trader UI. The trader's stock is shown in a
/// SECOND, VIEW-ONLY InventoryUI grid (configure it with all mutation gates off: _allowDrag,
/// _allowRotate, _allowStandardItemActions, _allowCrossGridHandoff, _allowEquip all unchecked,
/// no WeaponManager, no InventoryInputHandler, no save adapter). Buying and selling happen via
/// the right-click context menu, injected through the economy-agnostic provider hooks:
///   • player grid  → "Sell (N cr)"  + tooltip sell price
///   • stock grid   → "Buy (N cr)"   + tooltip buy price / stock count
///
/// Cross-grid DRAG between the two is blocked (unlike the stash) by the stock grid's
/// _allowCrossGridHandoff = false. Buying never transfers the displayed instance — it creates a
/// FRESH item into the player grid. Stock counts live in a runtime int[] (the asset is never
/// mutated); the stock grid is just a view rebuilt from it. Restock refills on every Nth hub return.
/// </summary>
public class TraderSystem : MonoBehaviour, IInteractable, IRunLifecycleListener
{
    [Header("=== Definition ===")]
    [SerializeField] private TraderSO _trader;

    [Header("=== Grids ===")]
    [Tooltip("The player's own inventory — sell from here; bought items land here.")]
    [SerializeField] private InventoryUI _playerInventoryUI;
    [Tooltip("View-only second grid that displays this trader's stock (buy from here).")]
    [SerializeField] private InventoryUI _stockUI;

    [Header("=== Access ===")]
    [Tooltip("WSM key that must be true for the trader to be usable. Written by the hub zone trigger.")]
    [SerializeField] private string _hubActiveKey = "zone.hub.active";
    [Tooltip("Bypass the hub gate — useful for testing before the hub zone trigger exists.")]
    [SerializeField] private bool _ignoreHubGate;

    public TraderSO Definition => _trader;
    public bool     IsOpen { get; private set; }

    private int[] _stockRemaining;                                       // mirrors _trader.Stock; -1 = unlimited
    private int   _runsUntilRestock;
    private bool  _openedPlayerPanel;                                    // did WE open the player grid?
    private readonly Dictionary<ItemInstance, int> _stockIndexByView = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake() => InitStock();

    private void OnEnable()  => RunManager.Instance?.RegisterListener(this);
    private void OnDisable() => RunManager.Instance?.UnregisterListener(this);

    private void Update()
    {
        // If the player closed their inventory (Tab/Escape) while trading, close the trader too
        // so the panels stay in sync and the GameInputState block count stays balanced.
        if (IsOpen && _playerInventoryUI != null && !_playerInventoryUI.IsOpen)
            Close();
    }

    private void InitStock()
    {
        if (_trader == null || _trader.Stock == null) { _stockRemaining = Array.Empty<int>(); return; }

        _stockRemaining = new int[_trader.Stock.Length];
        for (int i = 0; i < _stockRemaining.Length; i++)
        {
            int count = _trader.Stock[i].StockCount;
            _stockRemaining[i] = count <= 0 ? -1 : count; // 0 in the asset = unlimited
        }
        _runsUntilRestock = _trader.RestockIntervalRuns;
    }

    // ── IInteractable ──────────────────────────────────────────────────────

    public bool   CanInteract(GameObject interactor) => _trader != null && CanAccess() && !IsOpen;
    public string GetPrompt(GameObject interactor)    => _trader != null ? $"Talk to {_trader.TraderName}" : "Trader";
    public void   Interact(GameObject interactor)     => Open();

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
        if (_stockUI == null || _playerInventoryUI == null)
        {
            Debug.LogError("[TraderSystem] Player InventoryUI and/or stock InventoryUI not assigned.", this);
            return;
        }

        // Open both grids. Only open the player grid if it wasn't already open (e.g. via Tab),
        // so closing the trader later doesn't close a panel the player opened themselves.
        _openedPlayerPanel = !_playerInventoryUI.IsOpen;
        if (_openedPlayerPanel) _playerInventoryUI.SetOpen(true);
        if (!_stockUI.IsOpen)   _stockUI.SetOpen(true);

        BuildStockView();

        // Wire the economy hooks (cleared again on Close).
        _playerInventoryUI.SetContextExtraEntries(SellEntriesFor);
        _playerInventoryUI.SetTooltipExtraLine(SellTooltipFor);
        _stockUI.SetContextExtraEntries(BuyEntriesFor);
        _stockUI.SetTooltipExtraLine(BuyTooltipFor);

        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        // Unwire hooks first so a panel closing can't fire a stale Buy/Sell. Also hide any open
        // menu — the player grid may stay open (if they opened it themselves), and a Sell entry
        // already on screen would keep its captured action after the trade ends.
        if (_playerInventoryUI != null)
        {
            _playerInventoryUI.SetContextExtraEntries(null);
            _playerInventoryUI.SetTooltipExtraLine(null);
            _playerInventoryUI.HideContextMenu();
        }
        if (_stockUI != null)
        {
            _stockUI.SetContextExtraEntries(null);
            _stockUI.SetTooltipExtraLine(null);
            _stockUI.HideContextMenu();
        }

        ClearStockView();

        if (_stockUI != null && _stockUI.IsOpen) _stockUI.SetOpen(false);
        if (_openedPlayerPanel && _playerInventoryUI != null && _playerInventoryUI.IsOpen)
            _playerInventoryUI.SetOpen(false);
        _openedPlayerPanel = false;
    }

    // ── Stock view ─────────────────────────────────────────────────────────

    /// <summary>Fills the stock grid with one display instance per in-stock entry.</summary>
    private void BuildStockView()
    {
        ClearStockView();
        if (_trader?.Stock == null) return;

        for (int i = 0; i < _trader.Stock.Length; i++)
        {
            var entry = _trader.Stock[i];
            if (entry.Item == null) continue;
            if (_stockRemaining[i] == 0) continue; // sold out this cycle

            var inst = ItemInstanceFactory.Create(entry.Item);
            if (inst == null) continue;

            if (_stockUI.TryPickup(inst) != PickupResult.Placed)
            {
                Debug.LogWarning($"[TraderSystem] '{_trader.TraderName}' stock grid is full — " +
                                 $"could not display '{entry.Item.itemName}'. Enlarge the stock grid.", this);
                continue;
            }
            _stockIndexByView[inst] = i;
        }
    }

    private void ClearStockView()
    {
        _stockIndexByView.Clear();
        _stockUI?.ClearAll();
    }

    // ── Buy (from stock grid) ────────────────────────────────────────────────

    private List<(string label, Action action)> BuyEntriesFor(ItemInstance view)
    {
        if (!_stockIndexByView.TryGetValue(view, out int idx)) return null;
        int price = _trader.Stock[idx].BuyPrice;
        return new List<(string, Action)> { ($"Buy ({price} cr)", () => BuyByView(view)) };
    }

    private string BuyTooltipFor(ItemInstance view)
    {
        if (!_stockIndexByView.TryGetValue(view, out int idx)) return null;
        int    price   = _trader.Stock[idx].BuyPrice;
        int    rem     = _stockRemaining[idx];
        string stock   = rem < 0 ? "" : $"  (Stock: x{rem})";
        return $"<b>Buy: {price} cr</b>{stock}\nCredits: {CurrencyService.GetCredits()}";
    }

    private void BuyByView(ItemInstance view)
    {
        if (!_stockIndexByView.TryGetValue(view, out int idx)) return;
        if (!TryBuy(idx)) return; // unaffordable / no inventory space — nothing changed

        // Remove the display instance once the entry is exhausted.
        if (_stockRemaining[idx] == 0)
        {
            _stockUI.RemoveItem(view);
            _stockIndexByView.Remove(view);
        }
    }

    /// <summary>
    /// Buys one of stock entry <paramref name="stockIndex"/>. Places a FRESH instance into the
    /// player grid first — only on success do we charge — so a full inventory never loses credits.
    /// </summary>
    public bool TryBuy(int stockIndex)
    {
        if (_trader?.Stock == null || stockIndex < 0 || stockIndex >= _trader.Stock.Length) return false;
        if (_playerInventoryUI == null) return false;

        var entry = _trader.Stock[stockIndex];
        if (entry.Item == null) return false;

        int remaining = _stockRemaining[stockIndex];
        if (remaining == 0) return false;                          // sold out
        if (!CurrencyService.CanAfford(entry.BuyPrice)) return false;

        var instance = ItemInstanceFactory.Create(entry.Item);
        if (instance == null) return false;

        if (_playerInventoryUI.TryPickup(instance) != PickupResult.Placed) return false; // no room → no charge

        CurrencyService.Spend(entry.BuyPrice);
        if (remaining > 0) _stockRemaining[stockIndex] = remaining - 1; // unlimited (-1) stays put
        return true;
    }

    // ── Sell (from player grid) ──────────────────────────────────────────────

    private List<(string label, Action action)> SellEntriesFor(ItemInstance item)
    {
        int price = GetSellPrice(item);
        if (price <= 0) return null; // not sellable → no Sell entry
        return new List<(string, Action)> { ($"Sell ({price} cr)", () => TrySell(item)) };
    }

    private string SellTooltipFor(ItemInstance item)
    {
        int price = GetSellPrice(item);
        if (price <= 0) return $"<color=#999>Not sellable here</color>\nCredits: {CurrencyService.GetCredits()}";
        return $"<b>Sell: {price} cr</b>\nCredits: {CurrencyService.GetCredits()}";
    }

    /// <summary>What the trader pays for <paramref name="item"/>. 0 = won't buy it.</summary>
    public int GetSellPrice(ItemInstance item)
    {
        if (_trader == null || item?.data == null) return 0;

        int value = item.data.sellValue;
        if (value <= 0) return 0;
        if (!_trader.BuysAnything && !StocksItem(item.data)) return 0;

        return Mathf.Max(1, Mathf.RoundToInt(value * _trader.SellFraction));
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

    // ── Save state API (used by TraderSaveAdapter) ─────────────────────────

    public TraderStockSaveData CaptureStockState() =>
        new TraderStockSaveData
        {
            stockRemaining   = (int[])_stockRemaining.Clone(),
            runsUntilRestock = _runsUntilRestock,
        };

    public void RestoreStockState(TraderStockSaveData data)
    {
        if (data?.stockRemaining == null) return;

        // Copy only the overlap — SO may have grown/shrunk since the save was written.
        int len = Mathf.Min(data.stockRemaining.Length, _stockRemaining.Length);
        for (int i = 0; i < len; i++) _stockRemaining[i] = data.stockRemaining[i];
        _runsUntilRestock = data.runsUntilRestock;

        if (IsOpen) BuildStockView(); // refresh live grid if trader is open during hot-reload
    }

    // ── IRunLifecycleListener (restock) ────────────────────────────────────

    public void OnRunStarted()   { }
    public void OnRunExtracted() { }
    public void OnRunDied()      { }

    public void OnReturnedToHub()
    {
        if (_trader == null || _trader.RestockIntervalRuns <= 0) return; // never restocks

        _runsUntilRestock--;
        if (_runsUntilRestock > 0) return;

        InitStock(); // refill counts and reset the timer
        Debug.Log($"[TraderSystem] '{_trader.TraderName}' restocked.");

        if (IsOpen) BuildStockView(); // refresh if the player is mid-shop
    }
}

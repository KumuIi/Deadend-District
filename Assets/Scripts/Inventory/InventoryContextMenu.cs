using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Per-invocation configuration for a context menu. Built by the requesting InventoryUI
/// and handed to <see cref="InventoryContextMenu.Show"/>. Because a single shared menu now
/// serves every panel (player, stash, trader), the callbacks can't live on the menu — they
/// differ per right-click, so they travel with the request instead.
/// </summary>
public sealed class ContextMenuRequest
{
    public ItemInstance Item;
    public Vector2      ScreenPos;

    /// <summary>When false, the Equip/Unequip entry is omitted (e.g. the stash).</summary>
    public bool AllowEquip = true;

    /// <summary>When false, the standard mutation entries (Remove Magazine/Battery, Drop) are omitted.</summary>
    public bool AllowItemActions = true;

    public Action<ItemInstance>      OnEquip;
    public Action<ItemInstance>      OnUnequip;
    public Action<ItemInstance>      OnRemoveMagazine;
    public Action<ItemInstance>      OnRemoveBattery;
    public Action<ItemInstance>      OnDrop;
    public Action<AmmoItemInstance, int> OnSplitAmmo;

    /// <summary>Return true if the given item is currently the equipped weapon/flashlight.</summary>
    public Func<ItemInstance, bool> IsItemEquipped;

    /// <summary>Optional hook to append context-agnostic extra entries (e.g. a trader's Buy/Sell).</summary>
    public Func<ItemInstance, List<(string label, Action action)>> ExtraEntriesProvider;
}

/// <summary>
/// Right-click context menu for inventory items. Pure rendering engine — owns no per-panel
/// state; every Show() call supplies its own <see cref="ContextMenuRequest"/>.
///
/// Restyled to match the angular orange/toxic-green HUD:
///   - Ink skewed (6) panel.
///   - Each action row: transparent background, OffWhite bold-italic label, small Orange "▸" marker.
///   - Row hover: background → HudKit.Orange, label → near-black, row slides x+4; exit reverts.
///   - Destructive actions (Drop) use HudKit.Danger label color.
///   - Pop-in on Show: scale-y 0.85→1 + fade (0.1 s, SetLink).
///
/// Built on a top-most Screen Space – Overlay canvas (see InventoryContextMenuService) so it
/// always wins the GraphicRaycaster against any panel canvas and renders above the 3D item
/// meshes. A single instance is shared by all panels.
///
/// Dismissal uses a fullscreen transparent background Button so that both the dismiss and the
/// item-button clicks live inside the EventSystem — avoiding the timing bug where
/// Input.GetMouseButtonDown closes the panel before Button.onClick fires.
/// </summary>
public sealed class InventoryContextMenu
{
    private const float ButtonH    = 48f;   // 1.5× base
    private const float ButtonW    = 255f;  // 1.5× base
    private const float MarkerW    = 20f;
    private const float LabelLeft  = 14f;
    private const float FontSize   = 20f;   // 1.5× base (≈19.5)
    private const float PopDur     = 0.10f;

    private readonly GameObject    _panel;
    private readonly RectTransform _panelRT;
    private readonly CanvasGroup   _panelCG;
    private readonly GameObject    _bgDismiss;
    private readonly Canvas        _canvas;

    public bool IsOpen => _panel.activeSelf;

    public InventoryContextMenu(Canvas canvas)
    {
        _canvas = canvas;

        // ── Fullscreen dismiss layer (behind the panel) ────────────────────
        _bgDismiss = new GameObject("ContextMenuBG",
            typeof(RectTransform), typeof(Image), typeof(Button));
        _bgDismiss.transform.SetParent(canvas.transform, false);

        var bgRT       = _bgDismiss.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Invisible but raycast-blocking dismiss background
        _bgDismiss.GetComponent<Image>().color         = new Color(0f, 0f, 0f, 0f);
        _bgDismiss.GetComponent<Image>().raycastTarget = true;
        _bgDismiss.GetComponent<Button>().onClick.AddListener(Hide);
        _bgDismiss.SetActive(false);

        // ── Menu panel (above the dismiss layer) ──────────────────────────
        // Force-assign palette color here in case stale serialized color leaks from an older prefab.
        _panel = new GameObject("ContextMenuPanel",
            typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _panel.transform.SetParent(canvas.transform, false);

        _panelRT       = _panel.GetComponent<RectTransform>();
        _panelRT.pivot = new Vector2(1f, 1f);

        // Ink panel background
        var panelImg   = _panel.GetComponent<Image>();
        panelImg.color = new Color(HudKit.Ink.r, HudKit.Ink.g, HudKit.Ink.b, 0.97f);
        panelImg.sprite = HudKit.White;
        HudKit.Skew(panelImg, 6f);

        _panelCG = _panel.GetComponent<CanvasGroup>();
        _panelCG.blocksRaycasts = true;

        _panel.SetActive(false);
    }

    public void Show(ContextMenuRequest req)
    {
        if (req == null || req.Item == null) { Hide(); return; }
        ItemInstance item = req.Item;

        // Rebuild buttons for this item type
        for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_panel.transform.GetChild(i).gameObject);

        var entries = new List<(string label, Action action, bool destructive)>();

        if (item is WeaponItemInstance wi)
        {
            if (req.AllowEquip)
            {
                bool equipped = req.IsItemEquipped?.Invoke(item) ?? false;
                if (equipped)
                    entries.Add(("Unequip",         () => { req.OnUnequip?.Invoke(item);        Hide(); }, false));
                else
                    entries.Add(("Equip",           () => { req.OnEquip?.Invoke(item);          Hide(); }, false));
            }

            if (req.AllowItemActions && wi.LoadedMagazine != null)
                entries.Add(("Remove Magazine", () => { req.OnRemoveMagazine?.Invoke(item); Hide(); }, false));
        }
        else if (item is FlashlightItemInstance fi)
        {
            if (req.AllowEquip)
            {
                bool equipped = req.IsItemEquipped?.Invoke(item) ?? false;
                if (equipped)
                    entries.Add(("Unequip", () => { req.OnUnequip?.Invoke(item); Hide(); }, false));
                else
                    entries.Add(("Equip",   () => { req.OnEquip?.Invoke(item);   Hide(); }, false));
            }

            if (req.AllowItemActions && fi.InsertedBattery != null)
                entries.Add(("Remove Battery", () => { req.OnRemoveBattery?.Invoke(item); Hide(); }, false));
        }
        else if (item is AmmoItemInstance ammo && req.AllowItemActions)
        {
            // Split halves the stack; Take 10 peels a fixed 10 off. Both leave ≥1 round behind,
            // so they only appear when there's enough to split (Split.cs enforces the same rule).
            if (ammo.CurrentCount > 1)
                entries.Add(("Split",   () => { req.OnSplitAmmo?.Invoke(ammo, ammo.CurrentCount / 2); Hide(); }, false));
            if (ammo.CurrentCount > 10)
                entries.Add(("Take 10", () => { req.OnSplitAmmo?.Invoke(ammo, 10); Hide(); }, false));
        }

        if (req.AllowItemActions)
            entries.Add(("Drop", () => { req.OnDrop?.Invoke(item); Hide(); }, true)); // destructive

        // Injected, economy-agnostic entries (Buy/Sell). Hide() after each so a refresh that
        // destroys this item's view can't leave a dangling menu.
        var extra = req.ExtraEntriesProvider?.Invoke(item);
        if (extra != null)
            foreach (var (label, action) in extra)
                entries.Add((label, () => { action?.Invoke(); Hide(); }, false));

        // Nothing to show (e.g. a view-only grid item the trader won't buy) — don't pop an empty menu.
        if (entries.Count == 0) { Hide(); return; }

        _panelRT.sizeDelta = new Vector2(ButtonW, ButtonH * entries.Count);

        for (int i = 0; i < entries.Count; i++)
            AddRow(entries[i].label, i, entries[i].action, entries[i].destructive);

        CanvasUtils.MoveToScreenPoint(_panelRT, _canvas, req.ScreenPos);

        // Dismiss BG first in Z, panel on top
        _bgDismiss.SetActive(true);
        _bgDismiss.transform.SetAsLastSibling();

        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();

        // ── Pop-in tween: scale-y 0.85 → 1 + fade 0 → 1 ─────────────────
        _panelCG.alpha = 0f;
        _panelRT.localScale = new Vector3(1f, 0.85f, 1f);

        _panelCG.DOFade(1f, PopDur)
            .SetEase(Ease.OutCubic)
            .SetLink(_panel);
        _panelRT.DOScaleY(1f, PopDur)
            .SetEase(Ease.OutBack)
            .SetLink(_panel);
    }

    public void Hide()
    {
        _panel.SetActive(false);
        _bgDismiss.SetActive(false);
    }

    // ── Row construction ────────────────────────────────────────────────────

    private void AddRow(string label, int index, Action onClick, bool destructive)
    {
        // Row GO — fully interactive (raycastTarget on Image + Button)
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_panel.transform, false);

        var rt              = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.sizeDelta        = new Vector2(0f, ButtonH);
        rt.anchoredPosition = new Vector2(0f, -index * ButtonH);

        // Flat transparent background — hover fills it via RowHover
        var rowImg        = go.GetComponent<Image>();
        rowImg.color      = new Color(0f, 0f, 0f, 0f);
        rowImg.sprite     = HudKit.White;
        rowImg.raycastTarget = true;   // must stay true — button clicks depend on it

        // Keep Button colors neutral; all hover visuals are driven by RowHover
        var btn             = go.GetComponent<Button>();
        var colors          = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.fadeDuration     = 0f;
        btn.colors        = colors;
        btn.targetGraphic = rowImg;
        btn.onClick.AddListener(() => onClick());

        // ── Orange marker "▸" (left edge) ─────────────────────────────────
        var markerGO = new GameObject("Marker", typeof(RectTransform), typeof(TextMeshProUGUI));
        markerGO.transform.SetParent(go.transform, false);
        var marker           = markerGO.GetComponent<TextMeshProUGUI>();
        marker.text          = "▸";
        marker.fontSize      = FontSize;
        marker.color         = HudKit.Orange;
        marker.fontStyle     = FontStyles.Bold;
        marker.alignment     = TextAlignmentOptions.MidlineLeft;
        marker.raycastTarget = false;
        var markerRT         = marker.rectTransform;
        markerRT.anchorMin   = new Vector2(0f, 0f);
        markerRT.anchorMax   = new Vector2(0f, 1f);
        markerRT.pivot       = new Vector2(0f, 0.5f);
        markerRT.offsetMin   = new Vector2(LabelLeft, 0f);
        markerRT.offsetMax   = new Vector2(LabelLeft, 0f);
        markerRT.sizeDelta   = new Vector2(MarkerW, 0f);

        // ── Label ──────────────────────────────────────────────────────────
        // Destructive actions (Drop) rendered in Danger red; others in OffWhite.
        Color labelColor = destructive ? HudKit.Danger : HudKit.OffWhite;

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(go.transform, false);
        var labelTmp           = labelGO.GetComponent<TextMeshProUGUI>();
        labelTmp.text          = label;
        labelTmp.fontSize      = FontSize;
        labelTmp.color         = labelColor;
        labelTmp.fontStyle     = FontStyles.Bold | FontStyles.Italic;
        labelTmp.alignment     = TextAlignmentOptions.MidlineLeft;
        labelTmp.raycastTarget = false;
        var labelRT            = labelTmp.rectTransform;
        labelRT.anchorMin      = Vector2.zero;
        labelRT.anchorMax      = Vector2.one;
        labelRT.offsetMin      = new Vector2(LabelLeft + MarkerW + 4f, 0f);
        labelRT.offsetMax      = Vector2.zero;

        // ── Hover effect ───────────────────────────────────────────────────
        var hover = go.AddComponent<RowHover>();
        hover.Configure(rowImg, labelTmp, marker, rt);
    }

    // ── Hover helper component (private, mirrors DialogueUI.ChoiceHover) ────

    /// <summary>
    /// Applied to every context-menu row. On pointer enter: background → HudKit.Orange,
    /// label → near-black, row slides x+4. On pointer exit: reverts all.
    /// </summary>
    private sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image            _bg;
        private TextMeshProUGUI  _label;
        private TextMeshProUGUI  _marker;
        private RectTransform    _rt;

        private Color _bgDefault;
        private Color _labelDefault;
        private Color _markerDefault;
        private float _defaultX;

        private static readonly Color s_nearBlack = new Color(0.08f, 0.07f, 0.06f, 1f);

        public void Configure(Image bg, TextMeshProUGUI label, TextMeshProUGUI marker, RectTransform rt)
        {
            _bg     = bg;
            _label  = label;
            _marker = marker;
            _rt     = rt;

            _bgDefault     = bg.color;
            _labelDefault  = label.color;
            _markerDefault = marker.color;
            _defaultX      = rt.anchoredPosition.x;
        }

        public void OnPointerEnter(PointerEventData _)
        {
            if (_bg     != null) _bg.DOColor(HudKit.Orange, 0.1f).SetLink(_bg.gameObject);
            if (_label  != null) _label.DOColor(s_nearBlack, 0.1f).SetLink(_label.gameObject);
            if (_marker != null) _marker.DOColor(s_nearBlack, 0.1f).SetLink(_marker.gameObject);
            if (_rt     != null) _rt.DOAnchorPosX(_defaultX + 4f, 0.08f)
                .SetEase(Ease.OutQuad)
                .SetLink(_rt.gameObject);
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (_bg     != null) _bg.DOColor(_bgDefault, 0.15f).SetLink(_bg.gameObject);
            if (_label  != null) _label.DOColor(_labelDefault, 0.15f).SetLink(_label.gameObject);
            if (_marker != null) _marker.DOColor(_markerDefault, 0.15f).SetLink(_marker.gameObject);
            if (_rt     != null) _rt.DOAnchorPosX(_defaultX, 0.1f)
                .SetEase(Ease.OutQuad)
                .SetLink(_rt.gameObject);
        }
    }
}

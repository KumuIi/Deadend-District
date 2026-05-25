using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-click context menu for inventory items.
/// Parented directly to the root Canvas so it is never tilted.
///
/// Dismissal uses a fullscreen transparent background Button so that
/// both the dismiss and the item-button clicks live inside the EventSystem —
/// avoiding the timing bug where Input.GetMouseButtonDown closes the panel
/// before Button.onClick fires.
/// </summary>
public sealed class InventoryContextMenu
{
    private const float ButtonH = 32f;
    private const float ButtonW = 170f;

    private readonly GameObject    _panel;
    private readonly RectTransform _panelRT;
    private readonly GameObject    _bgDismiss;
    private readonly Canvas        _canvas;

    public bool IsOpen => _panel.activeSelf;

    public Action<ItemInstance> OnEquip;
    public Action<ItemInstance> OnUnequip;
    public Action<ItemInstance> OnRemoveMagazine;
    public Action<ItemInstance> OnDrop;

    /// <summary>Return true if the given item is currently the equipped weapon. Used to toggle Equip/Unequip.</summary>
    public Func<ItemInstance, bool> IsItemEquipped;

    public InventoryContextMenu(Canvas canvas)
    {
        _canvas = canvas;

        // ── Fullscreen dismiss layer (behind the panel) ────────────────────
        _bgDismiss = new GameObject("ContextMenuBG",
            typeof(RectTransform), typeof(Image), typeof(Button));
        _bgDismiss.transform.SetParent(canvas.transform, false);

        var bgRT          = _bgDismiss.GetComponent<RectTransform>();
        bgRT.anchorMin    = Vector2.zero;
        bgRT.anchorMax    = Vector2.one;
        bgRT.offsetMin    = Vector2.zero;
        bgRT.offsetMax    = Vector2.zero;

        _bgDismiss.GetComponent<Image>().color        = new Color(0f, 0f, 0f, 0f);
        _bgDismiss.GetComponent<Image>().raycastTarget = true;
        _bgDismiss.GetComponent<Button>().onClick.AddListener(Hide);
        _bgDismiss.SetActive(false);

        // ── Menu panel (above the dismiss layer) ──────────────────────────
        _panel = new GameObject("ContextMenuPanel",
            typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _panel.transform.SetParent(canvas.transform, false);

        _panelRT       = _panel.GetComponent<RectTransform>();
        _panelRT.pivot = new Vector2(1f, 1f);

        _panel.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.11f, 0.97f);
        _panel.GetComponent<CanvasGroup>().blocksRaycasts = true;

        _panel.SetActive(false);
    }

    public void Show(ItemInstance item, Vector2 screenPos)
    {
        // Rebuild buttons for this item type
        for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_panel.transform.GetChild(i).gameObject);

        var entries = new List<(string label, Action action)>();

        if (item is WeaponItemInstance wi)
        {
            bool equipped = IsItemEquipped?.Invoke(item) ?? false;
            if (equipped)
                entries.Add(("Unequip",         () => { OnUnequip?.Invoke(item);        Hide(); }));
            else
                entries.Add(("Equip",           () => { OnEquip?.Invoke(item);          Hide(); }));

            if (wi.LoadedMagazine != null)
                entries.Add(("Remove Magazine", () => { OnRemoveMagazine?.Invoke(item); Hide(); }));
        }
        else if (item is FlashlightItemInstance)
        {
            bool equipped = IsItemEquipped?.Invoke(item) ?? false;
            if (equipped)
                entries.Add(("Unequip", () => { OnUnequip?.Invoke(item); Hide(); }));
            else
                entries.Add(("Equip",   () => { OnEquip?.Invoke(item);   Hide(); }));
        }
        entries.Add(("Drop", () => { OnDrop?.Invoke(item); Hide(); }));

        _panelRT.sizeDelta = new Vector2(ButtonW, ButtonH * entries.Count);

        for (int i = 0; i < entries.Count; i++)
            AddButton(entries[i].label, i, entries[i].action);

        CanvasUtils.MoveToScreenPoint(_panelRT, _canvas, screenPos);

        // Dismiss BG first in Z, panel on top
        _bgDismiss.SetActive(true);
        _bgDismiss.transform.SetAsLastSibling();

        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Hide()
    {
        _panel.SetActive(false);
        _bgDismiss.SetActive(false);
    }

    private void AddButton(string label, int index, Action onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_panel.transform, false);

        var rt              = go.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.sizeDelta        = new Vector2(0f, ButtonH);
        rt.anchoredPosition = new Vector2(0f, -index * ButtonH);

        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        var btn             = go.GetComponent<Button>();
        var colors          = btn.colors;
        colors.normalColor      = new Color(0f, 0f, 0f, 0f);
        colors.highlightedColor = new Color(0.22f, 0.22f, 0.30f, 0.90f);
        colors.pressedColor     = new Color(0.12f, 0.48f, 0.12f, 0.90f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        var trt       = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12f, 0f);
        trt.offsetMax = Vector2.zero;

        var t           = textGO.GetComponent<TextMeshProUGUI>();
        t.text          = label;
        t.fontSize      = 13;
        t.color         = new Color(0.90f, 0.90f, 0.90f, 1f);
        t.alignment     = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
    }
}

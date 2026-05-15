using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cursor-following tooltip that shows item stats on hover.
/// No background panel — text-only with a drop shadow for legibility.
/// Parented to the root Canvas so it is never tilted.
/// </summary>
public sealed class InventoryTooltip
{
    private readonly RectTransform _rt;
    private readonly Text          _text;
    private readonly Canvas        _canvas;

    public InventoryTooltip(Canvas canvas)
    {
        _canvas = canvas;

        var go = new GameObject("InventoryTooltip",
            typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);

        _rt           = go.GetComponent<RectTransform>();
        _rt.anchorMin = new Vector2(0.5f, 0.5f);   // explicit: relative to canvas centre
        _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(0f, 1f);        // top-left follows cursor

        go.GetComponent<CanvasGroup>().blocksRaycasts = false;

        // Text with drop shadow
        var textGO = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
        textGO.transform.SetParent(go.transform, false);

        var trt          = textGO.GetComponent<RectTransform>();
        trt.anchorMin    = Vector2.zero;
        trt.anchorMax    = Vector2.one;
        trt.offsetMin    = Vector2.zero;
        trt.offsetMax    = Vector2.zero;
        // Let the Text component drive the size by fitting its own content
        trt.sizeDelta    = new Vector2(220f, 0f);

        _text                  = textGO.GetComponent<Text>();
        _text.font             = GetFont();
        _text.fontSize         = 13;
        _text.lineSpacing      = 1.4f;
        _text.color            = Color.white;
        _text.alignment        = TextAnchor.UpperLeft;
        _text.supportRichText  = true;
        _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        _text.verticalOverflow   = VerticalWrapMode.Overflow;
        _text.raycastTarget    = false;

        var shadow                = textGO.GetComponent<Shadow>();
        shadow.effectColor        = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance     = new Vector2(1f, -1f);
        shadow.useGraphicAlpha    = true;

        go.SetActive(false);
    }

    public void Show(ItemInstance item, Vector2 screenPos)
    {
        _text.text = BuildText(item);
        MoveToScreen(screenPos);
        _rt.gameObject.SetActive(true);
        _rt.transform.SetAsLastSibling();
    }

    public void UpdatePosition(Vector2 screenPos) => MoveToScreen(screenPos);

    public void Hide() => _rt.gameObject.SetActive(false);

    private void MoveToScreen(Vector2 screenPos)
    {
        Camera cam = null;
        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(), screenPos, cam, out Vector2 local);
        // Tiny offset so the tip of the cursor doesn't sit directly under the first line
        _rt.anchoredPosition = local + new Vector2(12f, -8f);
    }

    private string BuildText(ItemInstance item)
    {
        switch (item)
        {
            case WeaponItemInstance wi:
            {
                string cal  = wi.WeaponDef.caliber ? wi.WeaponDef.caliber.displayName : "—";
                string ammo = wi.LoadedMagazine != null
                    ? $"{wi.LoadedMagazine.RuntimeMag.BulletCount} / {wi.LoadedMagazine.MagDef.capacity}"
                    : "No magazine";
                string mode = FireModeLabel(wi.WeaponDef.fireMode);
                return $"<b>{wi.WeaponDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Ammo: {ammo}\n" +
                       $"Fire type: {mode}";
            }
            case MagazineItemInstance mi:
            {
                string cal = mi.MagDef.caliber ? mi.MagDef.caliber.displayName : "—";
                return $"<b>{mi.MagDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Ammo: {mi.RuntimeMag.BulletCount} / {mi.MagDef.capacity}";
            }
            case AmmoItemInstance ai:
            {
                string cal = ai.AmmoDef.caliber ? ai.AmmoDef.caliber.displayName : "—";
                return $"<b>{ai.AmmoDef.itemName}</b>\n" +
                       $"Caliber: {cal}\n" +
                       $"Count: {ai.CurrentCount}";
            }
            default:
                return $"<b>{item.data.itemName}</b>";
        }
    }

    private static string FireModeLabel(FireMode mode) => mode switch
    {
        FireMode.FullAuto => "Full-Auto",
        FireMode.SemiAuto => "Semi-Auto",
        FireMode.Burst    => "Burst",
        _                 => mode.ToString(),
    };

    private static Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}

using TMPro;
using UnityEngine;

/// <summary>
/// Cursor-following tooltip that shows item stats on hover.
/// No background panel — text-only with a drop shadow for legibility.
/// Parented to the root Canvas so it is never tilted.
/// </summary>
public sealed class InventoryTooltip
{
    private readonly RectTransform   _rt;
    private readonly TextMeshProUGUI _text;
    private readonly Canvas          _canvas;

    public InventoryTooltip(Canvas canvas)
    {
        _canvas = canvas;

        var go = new GameObject("InventoryTooltip",
            typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);

        _rt           = go.GetComponent<RectTransform>();
        _rt.anchorMin = new Vector2(0.5f, 0.5f);
        _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(0f, 1f);        // top-left follows cursor

        go.GetComponent<CanvasGroup>().blocksRaycasts = false;

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        var trt       = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        trt.sizeDelta = new Vector2(220f, 0f);

        _text                    = textGO.GetComponent<TextMeshProUGUI>();
        _text.font               = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        _text.fontSize           = 13;
        _text.color              = Color.white;
        _text.alignment          = TextAlignmentOptions.TopLeft;
        _text.richText           = true;
        _text.enableWordWrapping = true;
        _text.overflowMode       = TextOverflowModes.Overflow;
        _text.raycastTarget      = false;

        go.SetActive(false);
    }

    public void Show(ItemInstance item, Vector2 screenPos)
    {
        _rt.gameObject.SetActive(true);   // activate before positioning so anchor is applied immediately
        _text.text = BuildText(item);
        MoveToScreen(screenPos);
        _rt.transform.SetAsLastSibling();
    }

    public void UpdatePosition(Vector2 screenPos) => MoveToScreen(screenPos);

    public void Hide() => _rt.gameObject.SetActive(false);

    private void MoveToScreen(Vector2 screenPos)
    {
        var canvasRT = (RectTransform)_canvas.transform;
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : (_canvas.worldCamera != null ? _canvas.worldCamera : Camera.main);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRT, screenPos, cam, out Vector2 local))
            return;

        Rect r = canvasRT.rect;
        _rt.anchorMin = new Vector2(
            Mathf.InverseLerp(r.xMin, r.xMax, local.x),
            Mathf.InverseLerp(r.yMin, r.yMax, local.y));
        _rt.anchorMax        = _rt.anchorMin;
        _rt.anchoredPosition = Vector2.zero;
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
}

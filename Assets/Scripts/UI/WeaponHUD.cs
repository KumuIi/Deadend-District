using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right HUD panel showing current weapon name and loaded ammo count.
///
/// Setup:
///   1. Add this component to ANY child GameObject inside a Canvas.
///   2. Assign WeaponManager in the Inspector.
///
/// The script creates its own RectTransform panel as a direct child of the nearest
/// Canvas so positioning is always relative to the screen edge, regardless of
/// where in the hierarchy this component lives.
/// </summary>
public sealed class WeaponHUD : MonoBehaviour
{
    [Header("=== References ===")]
    public WeaponManager weaponManager;

    [Header("=== Position ===")]
    public float paddingRight  = 20f;
    public float paddingBottom = 20f;

    [Header("=== Style ===")]
    public int   ammoFontSize = 22;
    public int   nameFontSize = 15;
    public Color ammoColor    = new Color(1.00f, 0.85f, 0.35f, 1f);
    public Color nameColor    = new Color(0.88f, 0.88f, 0.88f, 1f);

    private Text _ammoText;
    private Text _nameText;

    private void Awake()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[WeaponHUD] Must be inside a Canvas."); return; }

        Font font = GetFont();

        // Create a self-contained panel directly under the Canvas
        var panel = new GameObject("WeaponHUDPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas.transform, false);

        var rt              = panel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);   // bottom-right anchor
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);   // pivot at bottom-right
        rt.sizeDelta        = new Vector2(220f, 62f);
        rt.anchoredPosition = new Vector2(-paddingRight, paddingBottom);

        // Ammo count — top half, larger
        _ammoText = NewText("AmmoCount", panel.transform, font, ammoFontSize, ammoColor);
        var ammoRT       = _ammoText.GetComponent<RectTransform>();
        ammoRT.anchorMin = new Vector2(0f, 0.45f);
        ammoRT.anchorMax = Vector2.one;
        ammoRT.offsetMin = Vector2.zero;
        ammoRT.offsetMax = Vector2.zero;

        // Weapon name — bottom half, smaller
        _nameText = NewText("WeaponName", panel.transform, font, nameFontSize, nameColor);
        var nameRT       = _nameText.GetComponent<RectTransform>();
        nameRT.anchorMin = Vector2.zero;
        nameRT.anchorMax = new Vector2(1f, 0.45f);
        nameRT.offsetMin = Vector2.zero;
        nameRT.offsetMax = Vector2.zero;
    }

    private void Update()
    {
        if (weaponManager == null) return;

        var gun = weaponManager.CurrentWeapon;
        if (gun == null) { _ammoText.text = "—"; _nameText.text = ""; return; }

        int bullets  = gun.BulletsRemaining;
        int capacity = gun.MagazineCapacity;
        _ammoText.text = capacity > 0 ? $"{bullets} / {capacity}" : "—";
        _nameText.text = gun.weaponData != null ? gun.weaponData.itemName : gun.name;
    }

    private static Text NewText(string goName, Transform parent, Font font, int size, Color color)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t             = go.GetComponent<Text>();
        t.font            = font;
        t.fontSize        = size;
        t.color           = color;
        t.alignment       = TextAnchor.MiddleRight;
        t.supportRichText = false;
        t.raycastTarget   = false;
        return t;
    }

    private static Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}

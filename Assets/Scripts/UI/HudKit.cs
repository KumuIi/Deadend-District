using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shared Persona-style UI kit for Deadend District.
/// Palette: ORANGE (primary) / ACID GREEN (secondary) on near-black ink panels.
/// All factory helpers set raycastTarget = false on every Graphic created here.
/// Textures are generated once and cached; guard against domain reload via null checks.
/// </summary>
public static class HudKit
{
    // ── Palette ────────────────────────────────────────────────────────────

    /// <summary>Primary accent orange ~#FF7A1A.</summary>
    public static readonly Color Orange   = new Color(1.00f, 0.478f, 0.102f, 1f);
    /// <summary>Bright flash orange ~#FFB347.</summary>
    public static readonly Color OrangeHot = new Color(1.00f, 0.702f, 0.278f, 1f);
    /// <summary>Toxic sewer green accent ~#5B8A19.</summary>
    public static readonly Color Green    = new Color(0.357f, 0.541f, 0.098f, 1f);
    /// <summary>Murky deep green ~#28410D.</summary>
    public static readonly Color GreenDeep = new Color(0.157f, 0.255f, 0.051f, 1f);
    /// <summary>Near-black panel with faint green tint, alpha ~0.92.</summary>
    public static readonly Color Ink      = new Color(0.055f, 0.075f, 0.055f, 0.92f);
    /// <summary>Lighter panel, alpha ~0.80.</summary>
    public static readonly Color InkSoft  = new Color(0.075f, 0.100f, 0.075f, 0.80f);
    /// <summary>Off-white for primary text ~#F2F0E6.</summary>
    public static readonly Color OffWhite = new Color(0.949f, 0.941f, 0.902f, 1f);
    /// <summary>Low-health danger red-orange ~#FF3A20.</summary>
    public static readonly Color Danger   = new Color(1.00f, 0.227f, 0.125f, 1f);

    // ── Sprites ────────────────────────────────────────────────────────────

    private static Sprite _white;
    private static Sprite _glow;
    private static Sprite _stripes;

    /// <summary>1×1 white sprite backed by Texture2D.whiteTexture.</summary>
    public static Sprite White
    {
        get
        {
            if (_white == null)
            {
                var tex = Texture2D.whiteTexture;
                _white = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
                _white.name = "HudKit_White";
            }
            return _white;
        }
    }

    /// <summary>64×64 radial soft-glow (white, alpha falls off smoothly), FullRect.</summary>
    public static Sprite Glow
    {
        get
        {
            if (_glow == null) _glow = BuildGlowSprite();
            return _glow;
        }
    }

    /// <summary>64×64 tileable 45° diagonal stripe pattern (white on transparent), wrapMode Repeat.</summary>
    public static Sprite Stripes
    {
        get
        {
            if (_stripes == null) _stripes = BuildStripesSprite();
            return _stripes;
        }
    }

    // ── Factory helpers ────────────────────────────────────────────────────

    /// <summary>Creates a new child RectTransform under <paramref name="parent"/>.</summary>
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Creates a child Image.
    /// sprite defaults to HudKit.White when null.
    /// raycastTarget is always false.
    /// </summary>
    public static Image Img(Transform parent, string name, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img           = go.GetComponent<Image>();
        img.color         = color;
        img.sprite        = sprite != null ? sprite : White;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// Creates a child TextMeshProUGUI.
    /// raycastTarget is always false.
    /// </summary>
    public static TextMeshProUGUI Text(
        Transform parent,
        string name,
        float size,
        Color color,
        TextAlignmentOptions align = TextAlignmentOptions.Left,
        FontStyles style           = FontStyles.Bold | FontStyles.Italic)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp                  = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize             = size;
        tmp.color                = color;
        tmp.alignment            = align;
        tmp.fontStyle            = style;
        tmp.raycastTarget        = false;
        tmp.textWrappingMode     = TextWrappingModes.NoWrap;
        tmp.overflowMode         = TextOverflowModes.Overflow;
        return tmp;
    }

    /// <summary>Adds (or retrieves existing) SkewEffect on the graphic's GameObject and sets skewPixels.</summary>
    public static SkewEffect Skew(Graphic g, float skewPixels = 10f)
    {
        var fx = g.GetComponent<SkewEffect>();
        if (fx == null) fx = g.gameObject.AddComponent<SkewEffect>();
        fx.SkewPixels = skewPixels;
        return fx;
    }

    // ── Texture generation ─────────────────────────────────────────────────

    private static Sprite BuildGlowSprite()
    {
        const int size = 64;
        var tex   = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name  = "HudKit_Glow";
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx   = (x + 0.5f - half) / half;
            float dy   = (y + 0.5f - half) / half;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float a    = Mathf.Clamp01(1f - dist);
            a          = a * a; // smooth falloff
            byte b     = (byte)(a * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, b);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true); // non-readable after upload
        return Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
    }

    private static Sprite BuildStripesSprite()
    {
        const int size  = 64;
        const int width = 4; // stripe width in pixels
        var tex   = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name  = "HudKit_Stripes";
        tex.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 45° diagonal: (x + y) mod stripe period
            int period = width * 2;
            bool on = ((x + y) % period) < width;
            byte a  = on ? (byte)180 : (byte)0;
            pixels[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
    }
}

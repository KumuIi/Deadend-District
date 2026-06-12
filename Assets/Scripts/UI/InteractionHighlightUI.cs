using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Draws screen-space corner brackets around the focused interactable object and
/// shows the interaction prompt below the brackets.
///
/// Creates its own Screen Space Overlay canvas at scene root so coordinate conversion
/// is trivial. The canvas uses ScaleWithScreenSize (800×600, match-width) to match the
/// HUD canvas, so all reference-unit sizes look consistent. Because the scaler changes
/// the mapping from physical pixels to canvas units, every screen-pixel value coming
/// from Camera.WorldToScreenPoint must be divided by _canvas.scaleFactor before being
/// written to anchoredPosition.
///
/// Setup:
///   1. Add this component to any GameObject in the scene (e.g. Player root or HUD GO).
///   2. Assign PlayerInteractor and Camera in the Inspector.
///   3. Tune bracket size, thickness, padding, and color to taste.
/// </summary>
[DefaultExecutionOrder(101)] // must run after PlayerInteractor (order 100)
public sealed class InteractionHighlightUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInteractor _interactor;
    [SerializeField] private Camera           _camera;

    [Header("Bracket style")]
    [SerializeField] private float _bracketLength    = 18f;
    [SerializeField] private float _bracketThickness = 3f;
    [SerializeField] private float _padding          = 14f;
    [SerializeField] private Color _bracketColor     = new Color(1.00f, 0.478f, 0.102f, 1f); // HudKit.Orange

    [Header("Prompt style")]
    [SerializeField] private float _promptFontSize   = 14f;
    [SerializeField] private float _promptOffsetY    = 10f; // canvas units below bracket rect

    [Header("Animation")]
    [SerializeField] private float _fadeSpeed        = 10f;

    // ── Runtime ────────────────────────────────────────────────────────────

    private Canvas          _canvas;
    private GameObject      _canvasGO;
    private CanvasGroup     _group;
    private RectTransform[] _hBars; // 4 horizontal bars (one per corner)
    private RectTransform[] _vBars; // 4 vertical bars

    // Prompt chip
    private GameObject       _promptChipGO;
    private Image            _promptChip;
    private Image            _promptUnderline;
    private TextMeshProUGUI  _prompt;

    // Cached per-target references (rebuilt only when target changes)
    private IInteractable _lastTarget;
    private Renderer[]    _cachedRenderers;
    private Collider      _cachedCollider;
    private bool          _hasBoundsSource;

    // Prompt text caching (only re-set TMP text when value changes)
    private string _lastPromptText;

    // Expand punch tween for new-target animation
    private float _expand = 1f;
    private Tween _expandTween;

    // Pre-allocated to avoid per-frame GC pressure on the HUD path
    private readonly Vector3[] _worldCorners    = new Vector3[8];
    private readonly Vector2[] _cornerScreenPos = new Vector2[4];

    // Corner order: 0=BL, 1=BR, 2=TL, 3=TR (matches corners[] in PositionBrackets)
    private static readonly Vector2[] CornerPivotH = { new(0,0), new(1,0), new(0,1), new(1,1) };
    private static readonly Vector2[] CornerPivotV = { new(0,0), new(1,0), new(0,1), new(1,1) };

    // ── Unity ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Force palette/metrics — prefab carries pre-overhaul serialized values.
        _bracketColor     = HudKit.Orange;
        _bracketThickness = 2f;
        _bracketLength    = 12f;
        _promptFontSize   = 9f;

        BuildCanvas();
        BuildBrackets();
        BuildPrompt();
        _group.alpha = 0f;
    }

    private void OnEnable()
    {
        // Guard null for pre-Awake enable (e.g. prefab instantiation order edge cases)
        if (_canvasGO != null) _canvasGO.SetActive(true);
    }

    private void OnDisable()
    {
        if (_canvasGO != null) _canvasGO.SetActive(false);
    }

    private void OnDestroy()
    {
        _expandTween?.Kill();
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    private void LateUpdate()
    {
        // Hide during dialogue or when gameplay is blocked (e.g. inventory open)
        if (GameInputState.GameplayBlocked || DialogueUI.IsOpen)
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, _fadeSpeed * Time.deltaTime);
            return;
        }

        var target = _interactor != null ? _interactor.Current : null;

        if (target != _lastTarget)
        {
            _lastTarget = target;
            RebuildBoundsSource(target);

            if (target != null)
            {
                // Punch expand on new target
                _expandTween?.Kill();
                _expand = 1.6f;
                _expandTween = DOTween.To(() => _expand, x => _expand = x, 1f, 0.25f)
                    .SetEase(Ease.OutCubic)
                    .SetLink(gameObject);
            }
            else
            {
                _expandTween?.Kill();
                _expand = 1f;
            }
        }

        bool visible = false;
        if (target != null && _hasBoundsSource)
        {
            visible = PositionBrackets();
            if (visible)
            {
                string prompt = _interactor.CurrentPrompt;
                if (prompt != _lastPromptText)
                {
                    _lastPromptText = prompt;
                    string displayStr = "<color=#FF7A1A>[F]</color> " + prompt;
                    _prompt.text    = displayStr;
                    // Resize chip to match text (GetPreferredValues handles rich text tags)
                    var preferred   = _prompt.GetPreferredValues(displayStr,
                        Mathf.Infinity, Mathf.Infinity);
                    _promptChip.rectTransform.sizeDelta = new Vector2(preferred.x + 16f, 22f);
                    _prompt.rectTransform.sizeDelta     = new Vector2(preferred.x + 16f, 22f);
                }
            }
        }

        float targetAlpha = visible ? 1f : 0f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, _fadeSpeed * Time.deltaTime);
    }

    // ── Bounds source ──────────────────────────────────────────────────────

    private void RebuildBoundsSource(IInteractable target)
    {
        _hasBoundsSource = false;
        _cachedRenderers = null;
        _cachedCollider  = null;

        if (target == null) return;
        if (target is not Component comp) return;

        // Cache both — ComputeBounds falls back to collider if all renderers are disabled
        _cachedRenderers = comp.GetComponentsInChildren<Renderer>();
        _cachedCollider  = comp.GetComponentInChildren<Collider>();
        _hasBoundsSource = _cachedRenderers.Length > 0 || _cachedCollider != null;
    }

    private bool ComputeBounds(out Bounds bounds)
    {
        bounds = default;

        if (_cachedRenderers != null)
        {
            Bounds? b = null;
            foreach (var r in _cachedRenderers)
            {
                if (!r.enabled) continue;
                if (b == null) b = r.bounds;
                else { var tmp = b.Value; tmp.Encapsulate(r.bounds); b = tmp; }
            }
            if (b.HasValue) { bounds = b.Value; return true; }
        }

        if (_cachedCollider != null)
        {
            bounds = _cachedCollider.bounds;
            return true;
        }

        return false;
    }

    // ── Bracket positioning ────────────────────────────────────────────────

    // Returns false when brackets should be hidden (camera missing, behind near
    // plane, or no valid bounds). LateUpdate uses the return value to drive alpha.
    //
    // IMPORTANT — scaleFactor mapping:
    // Camera.WorldToScreenPoint returns PHYSICAL pixels (e.g. 0..1920 at 1080p).
    // With a ScaleWithScreenSize scaler (800×600, match-width), the canvas is
    // internally 800 ref-units wide at any resolution, so scaleFactor = Screen.width / 800.
    // At 1920×1080 that's 2.4. anchoredPosition is in CANVAS (reference) units, not pixels.
    // Therefore every pixel coordinate must be divided by _canvas.scaleFactor.
    // Screen-extent clamp limits likewise use Screen.width/height divided by scaleFactor,
    // which equals the canvas width/height in reference units (e.g. 800 and 450 at 16:9).
    private bool PositionBrackets()
    {
        if (_camera == null) return false;
        if (!ComputeBounds(out var b)) return false;

        float sf = _canvas.scaleFactor; // physical pixels → canvas ref units

        // Project all 8 AABB corners to screen space (reuse pre-allocated array)
        _worldCorners[0] = new Vector3(b.min.x, b.min.y, b.min.z);
        _worldCorners[1] = new Vector3(b.max.x, b.min.y, b.min.z);
        _worldCorners[2] = new Vector3(b.min.x, b.max.y, b.min.z);
        _worldCorners[3] = new Vector3(b.max.x, b.max.y, b.min.z);
        _worldCorners[4] = new Vector3(b.min.x, b.min.y, b.max.z);
        _worldCorners[5] = new Vector3(b.max.x, b.min.y, b.max.z);
        _worldCorners[6] = new Vector3(b.min.x, b.max.y, b.max.z);
        _worldCorners[7] = new Vector3(b.max.x, b.max.y, b.max.z);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        float nearClip = _camera.nearClipPlane;

        foreach (var wc in _worldCorners)
        {
            var sc = _camera.WorldToScreenPoint(wc);
            if (sc.z < nearClip) return false; // any corner inside/behind near clip → hide entirely
            // Convert from physical pixels to canvas reference units
            float cx = sc.x / sf;
            float cy = sc.y / sf;
            if (cx < minX) minX = cx;
            if (cy < minY) minY = cy;
            if (cx > maxX) maxX = cx;
            if (cy > maxY) maxY = cy;
        }

        // Canvas extents in reference units (for clamping)
        float canvasW = Screen.width  / sf;
        float canvasH = Screen.height / sf;

        // Apply padding (with expand punch multiplier) and clamp to canvas bounds
        float paddingNow = _padding * _expand;
        minX = Mathf.Clamp(minX - paddingNow, 0f, canvasW);
        minY = Mathf.Clamp(minY - paddingNow, 0f, canvasH);
        maxX = Mathf.Clamp(maxX + paddingNow, 0f, canvasW);
        maxY = Mathf.Clamp(maxY + paddingNow, 0f, canvasH);

        // Enforce minimum bracket separation so they don't invert; clamp again after
        float minSize = _bracketLength * 2.5f;
        if (maxX - minX < minSize)
        {
            float mid = (minX + maxX) * 0.5f;
            minX = Mathf.Max(0f, mid - minSize * 0.5f);
            maxX = Mathf.Min(canvasW, mid + minSize * 0.5f);
        }
        if (maxY - minY < minSize)
        {
            float mid = (minY + maxY) * 0.5f;
            minY = Mathf.Max(0f, mid - minSize * 0.5f);
            maxY = Mathf.Min(canvasH, mid + minSize * 0.5f);
        }

        // Corner canvas positions (ref units): 0=BL, 1=BR, 2=TL, 3=TR
        _cornerScreenPos[0] = new Vector2(minX, minY);
        _cornerScreenPos[1] = new Vector2(maxX, minY);
        _cornerScreenPos[2] = new Vector2(minX, maxY);
        _cornerScreenPos[3] = new Vector2(maxX, maxY);

        for (int i = 0; i < 4; i++)
        {
            var h = _hBars[i];
            h.pivot            = CornerPivotH[i];
            h.anchoredPosition = _cornerScreenPos[i];
            h.sizeDelta        = new Vector2(_bracketLength, _bracketThickness);

            var v = _vBars[i];
            v.pivot            = CornerPivotV[i];
            v.anchoredPosition = _cornerScreenPos[i];
            v.sizeDelta        = new Vector2(_bracketThickness, _bracketLength);
        }

        // Prompt chip — centered horizontally, below the bracket rect.
        // Pivot is top-center, so anchoredPosition is the top of the text box.
        float promptY = minY - _promptOffsetY;
        if (promptY < 0f) promptY = maxY + _promptOffsetY + _promptChip.rectTransform.sizeDelta.y;
        var chipAnchor = new Vector2((minX + maxX) * 0.5f, promptY);
        _promptChip.rectTransform.anchoredPosition  = chipAnchor;
        _prompt.rectTransform.anchoredPosition      = chipAnchor;
        if (_promptUnderline != null)
        {
            var ulRT             = _promptUnderline.rectTransform;
            // Place underline 1.5 ref units below chip bottom (chip height = 22 ref units)
            ulRT.anchoredPosition = new Vector2(chipAnchor.x, chipAnchor.y - 22f);
        }

        return true;
    }

    // ── Build helpers ──────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _canvasGO = new GameObject("InteractionHighlightCanvas");
        _canvasGO.transform.SetParent(null); // scene root — avoids nested canvas inheritance

        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50; // above inventory (which uses a Camera canvas)

        // Match the HUD canvas scaler so reference units are consistent and text/brackets
        // are the same visual size as HUD elements at any resolution.
        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 600f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0f; // match width

        _group = _canvasGO.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;
    }

    private void BuildBrackets()
    {
        _hBars = new RectTransform[4];
        _vBars = new RectTransform[4];

        string[] names = { "BL", "BR", "TL", "TR" };
        Color glowColor = new Color(_bracketColor.r, _bracketColor.g, _bracketColor.b, 0.18f);

        for (int i = 0; i < 4; i++)
        {
            _hBars[i] = CreateBar($"Bracket_{names[i]}_H", glowColor);
            _vBars[i] = CreateBar($"Bracket_{names[i]}_V", glowColor);
        }
    }

    /// <summary>Creates a bracket bar image with a soft glow child behind it.</summary>
    private RectTransform CreateBar(string barName, Color glowColor)
    {
        var go  = new GameObject(barName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_canvas.transform, false);

        var img = go.GetComponent<Image>();
        img.color         = _bracketColor;
        img.raycastTarget = false;
        img.sprite        = HudKit.White;

        var rt       = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; // bottom-left anchor → canvas ref units = anchoredPosition
        rt.anchorMax = Vector2.zero;

        // Glow layer — slightly oversized child behind the bar
        var glowGO  = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(go.transform, false);
        glowGO.transform.SetAsFirstSibling();

        var glowImg           = glowGO.GetComponent<Image>();
        glowImg.sprite        = HudKit.Glow;
        glowImg.color         = glowColor;
        glowImg.raycastTarget = false;

        var glowRT        = glowGO.GetComponent<RectTransform>();
        glowRT.anchorMin  = new Vector2(0f, 0f);
        glowRT.anchorMax  = new Vector2(1f, 1f);
        glowRT.offsetMin  = new Vector2(-6f, -6f);
        glowRT.offsetMax  = new Vector2( 6f,  6f);

        return rt;
    }

    private void BuildPrompt()
    {
        // Chip container — bottom-left anchor (matches bracket bar convention: canvas ref units)
        _promptChipGO = new GameObject("InteractionPromptChip", typeof(RectTransform));
        _promptChipGO.transform.SetParent(_canvas.transform, false);

        var chipRT       = _promptChipGO.GetComponent<RectTransform>();
        chipRT.anchorMin = Vector2.zero;
        chipRT.anchorMax = Vector2.zero;
        chipRT.pivot     = new Vector2(0.5f, 1f); // top-center pivot — sits below brackets

        // Ink skewed chip background — font 9, chip skew 4
        _promptChip = HudKit.Img(_promptChipGO.transform, "Chip", HudKit.Ink);
        var cRT          = _promptChip.rectTransform;
        cRT.anchorMin    = Vector2.zero;
        cRT.anchorMax    = Vector2.one;
        cRT.offsetMin    = Vector2.zero;
        cRT.offsetMax    = Vector2.zero;
        HudKit.Skew(_promptChip, 4f);

        // Green underline accent — 1.5 ref units high, below chip
        _promptUnderline         = HudKit.Img(_canvas.transform, "PromptUnderline", HudKit.Green);
        var ulRT                 = _promptUnderline.rectTransform;
        ulRT.anchorMin           = Vector2.zero;
        ulRT.anchorMax           = Vector2.zero;
        ulRT.pivot               = new Vector2(0.5f, 1f);
        ulRT.sizeDelta           = new Vector2(100f, 1.5f);
        ulRT.anchoredPosition    = Vector2.zero;

        // Text — bold-italic OffWhite, font 9
        _prompt = HudKit.Text(_promptChipGO.transform, "PromptText", 9f,
            HudKit.OffWhite, TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic);
        _prompt.richText = true; // required for <color=#FF7A1A>[F]</color> key prefix
        var tRT          = _prompt.rectTransform;
        tRT.anchorMin    = Vector2.zero;
        tRT.anchorMax    = Vector2.one;
        tRT.offsetMin    = new Vector2(4f, 0f);
        tRT.offsetMax    = new Vector2(-4f, 0f);
        _prompt.textWrappingMode = TextWrappingModes.NoWrap;

        // Set initial chip size (22 ref units tall to match smaller font)
        _promptChipGO.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 22f);
        _promptChip.rectTransform.sizeDelta  = new Vector2(100f, 22f);
        _prompt.rectTransform.sizeDelta      = new Vector2(100f, 22f);
    }
}

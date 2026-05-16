using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws screen-space corner brackets around the focused interactable object and
/// shows the interaction prompt below the brackets.
///
/// Creates its own Screen Space Overlay canvas at scene root so coordinate conversion
/// is trivial: Camera.WorldToScreenPoint() pixels map directly to anchoredPosition on
/// a bottom-left-anchored RectTransform inside an Overlay canvas.
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
    [SerializeField] private float _bracketThickness = 2.5f;
    [SerializeField] private float _padding          = 14f;
    [SerializeField] private Color _bracketColor     = new Color(0.9f, 0.9f, 0.9f, 1f);

    [Header("Prompt style")]
    [SerializeField] private float _promptFontSize   = 14f;
    [SerializeField] private float _promptOffsetY    = 10f; // pixels below bracket rect

    [Header("Animation")]
    [SerializeField] private float _fadeSpeed        = 10f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private Canvas          _canvas;
    private GameObject      _canvasGO;
    private CanvasGroup     _group;
    private RectTransform[] _hBars; // 4 horizontal bars (one per corner)
    private RectTransform[] _vBars; // 4 vertical bars
    private TextMeshProUGUI _prompt;

    // Cached per-target references (rebuilt only when target changes)
    private IInteractable _lastTarget;
    private Renderer[]    _cachedRenderers;
    private Collider      _cachedCollider;
    private bool          _hasBoundsSource;

    // Pre-allocated to avoid per-frame GC pressure on the HUD path
    private readonly Vector3[] _worldCorners    = new Vector3[8];
    private readonly Vector2[] _cornerScreenPos = new Vector2[4];

    // Corner order: 0=BL, 1=BR, 2=TL, 3=TR (matches corners[] in PositionBrackets)
    // Both H and V pivots are identical: bars extend inward from their corner anchor.
    private static readonly Vector2[] CornerPivotH = { new(0,0), new(1,0), new(0,1), new(1,1) };
    private static readonly Vector2[] CornerPivotV = { new(0,0), new(1,0), new(0,1), new(1,1) };

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildCanvas();
        BuildBrackets();
        BuildPrompt();
        _group.alpha = 0f;
    }

    private void OnDestroy()
    {
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    private void LateUpdate()
    {
        var target = _interactor != null ? _interactor.Current : null;

        if (target != _lastTarget)
        {
            _lastTarget = target;
            RebuildBoundsSource(target);
        }

        bool visible = false;
        if (target != null && _hasBoundsSource)
        {
            visible = PositionBrackets();
            if (visible) _prompt.text = _interactor.CurrentPrompt;
        }

        float targetAlpha = visible ? 1f : 0f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, _fadeSpeed * Time.deltaTime);
    }

    // ── Bounds source ─────────────────────────────────────────────────────────

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

    // ── Bracket positioning ───────────────────────────────────────────────────

    // Returns false when brackets should be hidden (camera missing, behind near
    // plane, or no valid bounds). LateUpdate uses the return value to drive alpha.
    private bool PositionBrackets()
    {
        if (_camera == null) return false;
        if (!ComputeBounds(out var b)) return false;

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
            if (sc.x < minX) minX = sc.x;
            if (sc.y < minY) minY = sc.y;
            if (sc.x > maxX) maxX = sc.x;
            if (sc.y > maxY) maxY = sc.y;
        }

        // Apply padding and clamp to screen
        float sw = Screen.width, sh = Screen.height;
        minX = Mathf.Clamp(minX - _padding, 0f, sw);
        minY = Mathf.Clamp(minY - _padding, 0f, sh);
        maxX = Mathf.Clamp(maxX + _padding, 0f, sw);
        maxY = Mathf.Clamp(maxY + _padding, 0f, sh);

        // Enforce minimum bracket separation so they don't invert; clamp again after
        float minSize = _bracketLength * 2.5f;
        if (maxX - minX < minSize)
        {
            float mid = (minX + maxX) * 0.5f;
            minX = Mathf.Max(0f, mid - minSize * 0.5f);
            maxX = Mathf.Min(sw, mid + minSize * 0.5f);
        }
        if (maxY - minY < minSize)
        {
            float mid = (minY + maxY) * 0.5f;
            minY = Mathf.Max(0f, mid - minSize * 0.5f);
            maxY = Mathf.Min(sh, mid + minSize * 0.5f);
        }

        // Corner screen positions: 0=BL, 1=BR, 2=TL, 3=TR (reuse pre-allocated array)
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

        // Prompt text — centered horizontally, below the bracket rect.
        // Pivot is top-center, so anchoredPosition is the top of the text box.
        // When bottom-edge-clamped, flip above: top = maxY + offset + textHeight.
        float promptY = minY - _promptOffsetY;
        if (promptY < 0f) promptY = maxY + _promptOffsetY + _prompt.rectTransform.sizeDelta.y;
        _prompt.rectTransform.anchoredPosition = new Vector2((minX + maxX) * 0.5f, promptY);

        return true;
    }

    // ── Build helpers ─────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _canvasGO = new GameObject("InteractionHighlightCanvas");
        _canvasGO.transform.SetParent(null); // scene root — avoids nested canvas inheritance

        _canvas = _canvasGO.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50; // above inventory (which uses a Camera canvas)

        _canvasGO.AddComponent<CanvasScaler>(); // default settings fine for Overlay

        _group = _canvasGO.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;
    }

    private void BuildBrackets()
    {
        _hBars = new RectTransform[4];
        _vBars = new RectTransform[4];

        string[] names = { "BL", "BR", "TL", "TR" };
        for (int i = 0; i < 4; i++)
        {
            _hBars[i] = CreateBar($"Bracket_{names[i]}_H");
            _vBars[i] = CreateBar($"Bracket_{names[i]}_V");
        }
    }

    private RectTransform CreateBar(string barName)
    {
        var go  = new GameObject(barName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_canvas.transform, false);

        var img = go.GetComponent<Image>();
        img.color         = _bracketColor;
        img.raycastTarget = false;

        var rt       = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; // bottom-left anchor → screen pixels = anchoredPosition
        rt.anchorMax = Vector2.zero;
        return rt;
    }

    private void BuildPrompt()
    {
        var go = new GameObject("InteractionPrompt", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);

        _prompt = go.AddComponent<TextMeshProUGUI>();
        _prompt.fontSize      = _promptFontSize;
        _prompt.color         = _bracketColor;
        _prompt.alignment     = TMPro.TextAlignmentOptions.Center;
        _prompt.raycastTarget = false;

        var rt       = _prompt.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 1f); // top-center pivot so it sits below the brackets
        rt.sizeDelta = new Vector2(300f, 40f);
    }
}

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Extraction "high score" popup. The score for a run is the total sell value of the loot the
/// player FOUND during that run — i.e. items in the player inventory at extraction whose instance
/// wasn't there when the run started (gear brought in from the hub doesn't count).
///
/// Flow (driven by RunManager lifecycle, no polling):
///   • OnRunStarted   → snapshot the InstanceIds currently in the player grid (the gear taken in).
///   • OnRunExtracted → sum data.sellValue of every placed item NOT in that snapshot = run loot value.
///                      Compare to the stored best for the active save slot; flag a new record.
///   • OnReturnedToHub→ show the centered popup (the hub is loaded by now, so it's visible).
///   • OnRunDied      → discard the snapshot (death loses the loot — no score).
///
/// Self-contained: builds its own screen-space overlay canvas + centered panel in Awake, so the only
/// setup is dropping this component on the GameSystems GameObject. The best score is stored in
/// WorldStateManager (like CurrencyService's credits), so it belongs to the SAVE FILE — a new game
/// starts at 0, loading an old save restores that save's record — and persists for free via
/// WorldStateSaveAdapter with no dedicated save adapter to maintain.
///
/// Implementors: one instance on GameSystems (Hub scene).
/// </summary>
public sealed class RunScoreUI : MonoBehaviour, IRunLifecycleListener
{
    [Header("=== Timing ===")]
    [Tooltip("How long the popup stays fully visible before fading out.")]
    [SerializeField] private float _holdSeconds = 3.5f;
    [SerializeField] private float _fadeInSeconds  = 0.4f;
    [SerializeField] private float _fadeOutSeconds = 0.6f;

    [Header("=== Style ===")]
    [SerializeField] private int   _headerFontSize = 30;
    [SerializeField] private int   _valueFontSize  = 22;
    [SerializeField] private int   _bestFontSize   = 16;
    [SerializeField] private Color _headerColor    = new Color(0.86f, 0.86f, 0.86f, 1f);
    [SerializeField] private Color _valueColor     = new Color(1.00f, 0.95f, 0.80f, 1f);
    [SerializeField] private Color _recordColor    = new Color(1.00f, 0.82f, 0.25f, 1f); // gold
    [SerializeField] private Color _bestColor      = new Color(0.70f, 0.70f, 0.70f, 1f);
    [SerializeField] private Color _backdropColor  = new Color(0f, 0f, 0f, 0.55f);

    // Stored in WorldStateManager so it's part of the save file (see CurrencyService's "economy.credits").
    private const string HighscoreKey = "stats.run_highscore";

    // UI
    private CanvasGroup     _group;
    private TextMeshProUGUI _headerText;
    private TextMeshProUGUI _valueText;
    private TextMeshProUGUI _bestText;
    private Coroutine       _showRoutine;

    // Run state
    private readonly HashSet<System.Guid> _startSnapshot = new HashSet<System.Guid>();
    private int  _pendingRunValue;
    private bool _pendingNewRecord;
    private int  _pendingBest;
    private bool _hasPending;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake() => BuildUI();

    // Register in both OnEnable and Start: OnEnable may run before RunManager.Awake sets Instance;
    // RegisterListener is idempotent (Contains-guarded) so the second call is harmless.
    private void OnEnable() => RunManager.Instance?.RegisterListener(this);
    private void Start()    => RunManager.Instance?.RegisterListener(this);
    private void OnDisable() => RunManager.Instance?.UnregisterListener(this);

    // ── IRunLifecycleListener ────────────────────────────────────────────────

    public void OnRunStarted()
    {
        _startSnapshot.Clear();
        var grid = InventoryUI.Player != null ? InventoryUI.Player.Grid : null;
        if (grid == null) return; // no inventory yet → everything found this run counts as loot
        foreach (var item in grid.PlacedItems)
            if (item != null) _startSnapshot.Add(item.InstanceId);
    }

    public void OnRunExtracted()
    {
        int runValue = ComputeFoundLootValue();

        var wsm  = WorldStateManager.Instance;
        int best = wsm != null ? wsm.GetInt(HighscoreKey) : 0;

        bool newRecord = runValue > best;
        if (newRecord)
        {
            best = runValue;
            wsm?.SetInt(HighscoreKey, best); // committed to the save file by WorldStateSaveAdapter on next save
        }

        _pendingRunValue  = runValue;
        _pendingNewRecord = newRecord;
        _pendingBest      = best;
        _hasPending       = true;
    }

    public void OnRunDied() => _startSnapshot.Clear(); // loot lost on death — no score

    public void OnReturnedToHub()
    {
        if (!_hasPending) return; // only after an extraction (not after a death/load return)
        _hasPending = false;
        ShowPopup(_pendingRunValue, _pendingNewRecord, _pendingBest);
    }

    /// <summary>Sum of sellValue for placed items whose instance wasn't in the start snapshot.</summary>
    private int ComputeFoundLootValue()
    {
        var grid = InventoryUI.Player != null ? InventoryUI.Player.Grid : null;
        if (grid == null) return 0;

        int total = 0;
        foreach (var item in grid.PlacedItems)
        {
            if (item?.data == null) continue;
            if (_startSnapshot.Contains(item.InstanceId)) continue; // brought in from the hub
            total += Mathf.Max(0, item.data.sellValue);
        }
        return total;
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    private void ShowPopup(int runValue, bool newRecord, int best)
    {
        _headerText.text = newRecord ? "NEW HIGH SCORE!" : "EXTRACTED";
        _headerText.color = newRecord ? _recordColor : _headerColor;

        // Make clear this is the loot's worth (what you could sell it for), NOT credits received.
        _valueText.text  = $"Loot gathered worth {runValue:N0} cr";
        _valueText.color = newRecord ? _recordColor : _valueColor;

        _bestText.text = newRecord ? "Best run yet" : $"Best run: {best:N0} cr worth";

        if (_showRoutine != null) StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(FadeSequence());
    }

    private IEnumerator FadeSequence()
    {
        _group.gameObject.SetActive(true);
        yield return Fade(0f, 1f, _fadeInSeconds);
        yield return new WaitForSecondsRealtime(_holdSeconds);
        yield return Fade(1f, 0f, _fadeOutSeconds);
        _group.gameObject.SetActive(false);
        _showRoutine = null;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (duration <= 0f) { _group.alpha = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // unscaled so a paused/slow-mo hub still animates
            _group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        _group.alpha = to;
    }

    // ── UI construction ────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Dedicated overlay canvas so this is fully self-contained and always draws on top,
        // regardless of which HUD canvas exists in the scene.
        var canvasGO = new GameObject("RunScoreCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas        = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // above standard HUD
        var scaler            = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode    = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // No GraphicRaycaster → the popup never eats clicks.

        // Group root (centered), with a subtle backdrop behind the text.
        var groupGO = new GameObject("RunScorePanel", typeof(RectTransform), typeof(CanvasGroup),
                                     typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        groupGO.transform.SetParent(canvasGO.transform, false);
        var rt        = groupGO.GetComponent<RectTransform>();
        rt.anchorMin  = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        _group       = groupGO.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = _group.blocksRaycasts = false;

        var vlg = groupGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 6f;
        vlg.padding            = new RectOffset(48, 48, 28, 28);
        vlg.childAlignment     = TextAnchor.MiddleCenter;
        vlg.childControlWidth  = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

        var fitter        = groupGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddBackdrop(rt);

        _headerText = NewText("Header", groupGO.transform, _headerFontSize, _headerColor, FontStyles.Bold);
        _valueText  = NewText("Value",  groupGO.transform, _valueFontSize,  _valueColor,  FontStyles.Bold);
        _bestText   = NewText("Best",   groupGO.transform, _bestFontSize,   _bestColor,   FontStyles.Normal);

        groupGO.SetActive(false);
    }

    /// <summary>A dimmed rounded-ish backdrop image stretched behind the layout group.</summary>
    private void AddBackdrop(RectTransform parent)
    {
        var bgGO = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        bgGO.transform.SetParent(parent, false);
        bgGO.GetComponent<LayoutElement>().ignoreLayout = true; // VLG must not size/position the backdrop
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = new Vector2(-12f, -12f);
        bgRT.offsetMax = new Vector2(12f, 12f);
        var img          = bgGO.GetComponent<Image>();
        img.color        = _backdropColor;
        img.raycastTarget = false;
        bgGO.transform.SetAsFirstSibling(); // behind the text
    }

    private static TextMeshProUGUI NewText(string goName, Transform parent, int size, Color color, FontStyles style)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t           = go.GetComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.color         = color;
        t.fontStyle     = style;
        t.alignment     = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}

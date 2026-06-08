using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-RIGHT HUD list of currently ACTIVE quests: each shows its title and the current step
/// (the first revealed, incomplete mandatory objective's description), so the line auto-advances
/// from "Recover the sample" to "Give the sample to The Doctor" as objectives tick.
///
/// Self-builds its panel under the nearest Canvas (like PlayerHUD / WeaponHUD). Refreshes only when
/// QuestManager fires OnQuestsChanged (plus once on Start) — no per-frame work.
///
/// Setup: add to any child of a Canvas, assign QuestManager in the Inspector.
/// </summary>
public sealed class QuestTrackerUI : MonoBehaviour
{
    [Header("=== References ===")]
    [SerializeField] private QuestManager _questManager;

    [Header("=== Position ===")]
    [SerializeField] private float _paddingRight = 20f;
    [SerializeField] private float _paddingTop   = 20f;
    [SerializeField] private float _width        = 280f;

    [Header("=== Style ===")]
    [SerializeField] private int   _titleFontSize = 15;
    [SerializeField] private int   _stepFontSize  = 12;
    [SerializeField] private Color _titleColor    = new Color(1.00f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _stepColor     = new Color(0.86f, 0.86f, 0.86f, 1f);

    private RectTransform _panel;
    private readonly List<GameObject> _entries = new List<GameObject>();

    private void Awake()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[QuestTrackerUI] Must live inside a Canvas.", this); return; }
        BuildPanel(canvas.transform);
    }

    private void OnEnable()
    {
        if (_questManager != null) _questManager.OnQuestsChanged += Refresh;
        Refresh();
    }

    private void Start() => Refresh(); // QuestManager may finish InitRuntime after our OnEnable

    private void OnDisable()
    {
        if (_questManager != null) _questManager.OnQuestsChanged -= Refresh;
    }

    private void Refresh()
    {
        if (_panel == null) return;

        foreach (var e in _entries) if (e != null) Destroy(e);
        _entries.Clear();

        if (_questManager == null) return;

        foreach (var quest in _questManager.Quests)
        {
            if (quest == null) continue;
            if (_questManager.GetStatus(quest) != QuestStatus.Active) continue;
            BuildEntry(quest.title, TrackerLineFor(quest));
        }

        _panel.gameObject.SetActive(_entries.Count > 0);
    }

    /// <summary>Current step text: first revealed, incomplete, mandatory objective; else a quest-level fallback.</summary>
    private string TrackerLineFor(QuestSO quest)
    {
        var objs = quest.objectives;
        if (objs != null)
        {
            for (int i = 0; i < objs.Length; i++)
            {
                var o = objs[i];
                if (o == null || o.optional) continue;
                if (!_questManager.IsObjectiveRevealed(quest, i)) continue;
                if (_questManager.IsObjectiveComplete(quest, i))  continue;

                if (!string.IsNullOrEmpty(o.description))                 return o.description;
                if (o.condition != null && !string.IsNullOrEmpty(o.condition.description))
                    return o.condition.description;
                return Fallback(quest);
            }
        }
        return Fallback(quest);
    }

    private static string Fallback(QuestSO quest) =>
        !string.IsNullOrEmpty(quest.trackerText) ? quest.trackerText : quest.description;

    // ── UI construction ──────────────────────────────────────────────────────

    private void BuildPanel(Transform canvas)
    {
        var panelGO = new GameObject("QuestTrackerPanel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGO.transform.SetParent(canvas, false);

        _panel               = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin     = new Vector2(1f, 1f);
        _panel.anchorMax     = new Vector2(1f, 1f);
        _panel.pivot         = new Vector2(1f, 1f);
        _panel.sizeDelta     = new Vector2(_width, 0f);
        _panel.anchoredPosition = new Vector2(-_paddingRight, -_paddingTop);

        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 10f;
        vlg.childAlignment     = TextAnchor.UpperRight;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = panelGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _panel.gameObject.SetActive(false);
    }

    private void BuildEntry(string title, string step)
    {
        var entryGO = new GameObject("QuestEntry", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        entryGO.transform.SetParent(_panel, false);

        var vlg = entryGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing            = 1f;
        vlg.childAlignment     = TextAnchor.UpperLeft;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = entryGO.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var titleText = NewText("Title", entryGO.transform, _titleFontSize, _titleColor);
        titleText.fontStyle = FontStyles.Bold;
        titleText.text      = string.IsNullOrEmpty(title) ? "Quest" : title;

        if (!string.IsNullOrEmpty(step))
        {
            var stepText = NewText("Step", entryGO.transform, _stepFontSize, _stepColor);
            stepText.text = step;
        }

        _entries.Add(entryGO);
    }

    private static TextMeshProUGUI NewText(string goName, Transform parent, int size, Color color)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t           = go.GetComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.color         = color;
        t.alignment     = TextAlignmentOptions.TopRight;
        t.raycastTarget = false;
        return t;
    }
}

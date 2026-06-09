using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a <see cref="DialogueConversation"/> (built by <see cref="QuestGiver"/>): lines in order,
/// then gated choices. Self-builds its panel under the nearest Canvas (like PlayerHUD / WeaponHUD),
/// so no prefab UI is required. Blocks gameplay input while open via GameInputState.
///
/// Bridges to quests only by writing WSM facts (line writesOnShow, choice writesOnPick) — the
/// QuestManager reacts to those changes itself.
///
/// Item hand-in is atomic: a choice with a giveItem reward is rolled back (the takeItem is returned
/// and no WSM is written) if the reward can't fit, so the player can never lose an item mid-trade.
/// </summary>
public sealed class DialogueUI : MonoBehaviour
{
    /// <summary>Scene-wide accessor used by QuestGiver. Null if no DialogueUI exists.</summary>
    public static DialogueUI Instance { get; private set; }

    /// <summary>True while a conversation is on screen. Use to gate save/load menus, etc.</summary>
    public static bool IsOpen { get; private set; }

    [Header("=== Style ===")]
    [SerializeField] private int   _nameFontSize = 18;
    [SerializeField] private int   _bodyFontSize = 16;
    [SerializeField] private Color _panelColor   = new Color(0.06f, 0.07f, 0.09f, 0.92f);
    [SerializeField] private Color _nameColor    = new Color(1.00f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color _bodyColor    = new Color(0.92f, 0.92f, 0.92f, 1f);
    [SerializeField] private Color _choiceColor  = new Color(0.16f, 0.18f, 0.24f, 0.95f);
    [SerializeField] private Color _hintColor    = new Color(0.95f, 0.55f, 0.30f, 1f);

    [Header("=== Advance ===")]
    [Tooltip("Key that advances a line (in addition to clicking Continue). Choices are click-only.")]
    [SerializeField] private KeyCode _advanceKey = KeyCode.Space;

    // ── Built UI ──────────────────────────────────────────────────────────
    private RectTransform   _panel;
    private Image           _portrait;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _bodyText;
    private TextMeshProUGUI _hintText;
    private Button          _continueButton;
    private RectTransform   _choicesParent;
    private readonly List<GameObject> _choiceButtons = new List<GameObject>();

    private static Sprite _whiteSprite;

    // ── Playback state ──────────────────────────────────────────────────────
    private DialogueConversation _conversation;
    private int                  _lineIndex;
    private bool                 _showingChoices;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[DialogueUI] A second DialogueUI on '{name}' — destroying it; keep one per scene.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[DialogueUI] Must live inside a Canvas.", this); return; }
        if (canvas.GetComponent<GraphicRaycaster>() == null)
            Debug.LogWarning("[DialogueUI] Canvas has no GraphicRaycaster — choice buttons won't be clickable.", this);

        Build(canvas.transform);
        _panel.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (IsOpen) { GameInputState.Unblock(); IsOpen = false; }
    }

    private void Update()
    {
        if (!IsOpen || _showingChoices) return;
        if (Input.GetKeyDown(_advanceKey)) Advance();
    }

    // ── Public entry ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a conversation (built by <see cref="QuestGiver"/>). After a choice it closes; the caller
    /// recomputes what to say on the next interaction.
    /// </summary>
    public void Open(DialogueConversation convo)
    {
        if (convo == null || ((convo.lines == null || convo.lines.Length == 0) &&
                              (convo.choices == null || convo.choices.Length == 0)))
            return;

        if (!IsOpen)
        {
            GameInputState.Block();
            IsOpen = true;
            _panel.gameObject.SetActive(true);
        }

        Play(convo);
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    private void Play(DialogueConversation convo)
    {
        _conversation = convo;
        _lineIndex    = 0;
        SetHint(string.Empty);
        ShowCurrentLineOrChoices();
    }

    private void ShowCurrentLineOrChoices()
    {
        var lines = _conversation?.lines;
        if (lines != null && _lineIndex < lines.Length)
        {
            ShowLine(lines[_lineIndex]);
            return;
        }
        ShowChoices();
    }

    private void ShowLine(DialogueLine line)
    {
        _showingChoices = false;
        ClearChoiceButtons();
        _choicesParent.gameObject.SetActive(false);
        _continueButton.gameObject.SetActive(true);

        _nameText.text = line.speakerName ?? string.Empty;
        _bodyText.text = line.text ?? string.Empty;

        bool hasPortrait = line.portrait != null;
        _portrait.gameObject.SetActive(hasPortrait);
        if (hasPortrait) _portrait.sprite = line.portrait;

        // Low-risk facts only (e.g. "seen intro"); quest-critical writes belong on choices.
        if (line.writesOnShow != null)
            foreach (var w in line.writesOnShow) w?.Apply();
    }

    private void Advance()
    {
        _lineIndex++;
        ShowCurrentLineOrChoices();
    }

    private void ShowChoices()
    {
        _showingChoices = true;
        _continueButton.gameObject.SetActive(false);
        ClearChoiceButtons();

        var choices = _conversation?.choices;
        if (choices == null || choices.Length == 0) { Close(); return; }

        int shown = 0;
        for (int i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            if (choice == null || !IsChoiceVisible(choice)) continue;
            BuildChoiceButton(choice);
            shown++;
        }

        // No visible choice (e.g. all gated out) — nothing to do, end the conversation.
        if (shown == 0) { Close(); return; }
        _choicesParent.gameObject.SetActive(true);
    }

    private static bool IsChoiceVisible(DialogueChoice choice) =>
        DialogueUtil.ConditionPassesOrEmpty(choice.showIf) &&
        DialogueUtil.PlayerHasItem(choice.takeItem); // takeItem null => true

    // ── Choice resolution (atomic item transaction) ──────────────────────────

    private void OnChoicePicked(DialogueChoice choice)
    {
        var player = InventoryUI.Player;

        // 1. Locate the take item (the choice only shows if it's present, but re-check defensively).
        ItemInstance taken = null;
        if (choice.takeItem != null)
        {
            taken = FindInPlayerGrid(choice.takeItem);
            if (taken == null) { SetHint("You no longer have that item."); ShowChoices(); return; }
        }

        // 2. Commit the take, then attempt the give. Roll the take back if the reward won't fit.
        if (taken != null) player.RemoveItemAndDetach(taken);

        if (choice.giveItem != null)
        {
            var reward = ItemInstanceFactory.Create(choice.giveItem);
            if (player == null || reward == null || player.TryPickup(reward) == PickupResult.NoSpace)
            {
                if (taken != null && player != null) player.TryPickup(taken); // rollback — slot just freed
                SetHint("No room in your inventory.");
                ShowChoices();
                return; // no writes, no advance — nothing was committed
            }
        }

        // 3. Commit the facts, then close. QuestManager reacts to these; the QuestGiver recomputes
        //    what to say the next time the player talks.
        if (choice.writesOnPick != null)
            foreach (var w in choice.writesOnPick) w?.Apply();

        Close();
    }

    private static ItemInstance FindInPlayerGrid(ItemSO data)
    {
        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return null;
        foreach (var inst in grid.PlacedItems)
            if (inst != null && inst.data == data) return inst;
        return null;
    }

    private void Close()
    {
        ClearChoiceButtons();
        _showingChoices = false;
        _conversation   = null;
        if (_panel != null) _panel.gameObject.SetActive(false);
        if (IsOpen) { GameInputState.Unblock(); IsOpen = false; }
    }

    private void SetHint(string msg)
    {
        if (_hintText == null) return;
        _hintText.text = msg ?? string.Empty;
        _hintText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
    }

    // ── UI construction ──────────────────────────────────────────────────────

    private void Build(Transform canvas)
    {
        EnsureWhiteSprite();

        // Middle-center dialogue box (Unity's alt+shift+middle preset: anchor, pivot and position all centered).
        var panelGO = new GameObject("DialoguePanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvas, false);
        _panel               = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin     = new Vector2(0.5f, 0.5f);
        _panel.anchorMax     = new Vector2(0.5f, 0.5f);
        _panel.pivot         = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta     = new Vector2(820f, 220f);
        _panel.anchoredPosition = Vector2.zero;
        var panelImg         = panelGO.GetComponent<Image>();
        panelImg.color       = _panelColor;
        panelImg.sprite      = _whiteSprite;

        // Portrait (left).
        var portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
        portraitGO.transform.SetParent(_panel, false);
        var pRT             = portraitGO.GetComponent<RectTransform>();
        pRT.anchorMin       = new Vector2(0f, 0f);
        pRT.anchorMax       = new Vector2(0f, 1f);
        pRT.pivot           = new Vector2(0f, 0.5f);
        pRT.offsetMin       = new Vector2(14f, 14f);
        pRT.offsetMax       = new Vector2(14f + 160f, -14f);
        _portrait           = portraitGO.GetComponent<Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget  = false;

        const float textLeft = 190f;

        // Speaker name (top).
        _nameText = NewText("Name", _panel, _nameFontSize, _nameColor, TextAlignmentOptions.TopLeft);
        var nRT = _nameText.rectTransform;
        nRT.anchorMin = new Vector2(0f, 1f); nRT.anchorMax = new Vector2(1f, 1f); nRT.pivot = new Vector2(0f, 1f);
        nRT.offsetMin = new Vector2(textLeft, -42f); nRT.offsetMax = new Vector2(-20f, -12f);
        _nameText.fontStyle = FontStyles.Bold;

        // Body text (middle).
        _bodyText = NewText("Body", _panel, _bodyFontSize, _bodyColor, TextAlignmentOptions.TopLeft);
        var bRT = _bodyText.rectTransform;
        bRT.anchorMin = new Vector2(0f, 0f); bRT.anchorMax = Vector2.one; bRT.pivot = new Vector2(0f, 1f);
        bRT.offsetMin = new Vector2(textLeft, 52f); bRT.offsetMax = new Vector2(-20f, -46f);
        _bodyText.enableWordWrapping = true;

        // Hint label (e.g. "No room in your inventory.") — bottom-left.
        _hintText = NewText("Hint", _panel, 13, _hintColor, TextAlignmentOptions.BottomLeft);
        var hRT = _hintText.rectTransform;
        hRT.anchorMin = new Vector2(0f, 0f); hRT.anchorMax = new Vector2(1f, 0f); hRT.pivot = new Vector2(0f, 0f);
        hRT.offsetMin = new Vector2(textLeft, 12f); hRT.offsetMax = new Vector2(-150f, 34f);
        _hintText.gameObject.SetActive(false);

        // Continue button (bottom-right).
        _continueButton = BuildButton(_panel, "Continue", _choiceColor, new Vector2(1f, 0f), new Vector2(1f, 0f),
                                      new Vector2(-130f, 12f), new Vector2(120f, 32f));
        _continueButton.onClick.AddListener(Advance);

        // Choices container (vertical stack, replaces Continue when choices show).
        var choicesGO = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
        choicesGO.transform.SetParent(_panel, false);
        _choicesParent = choicesGO.GetComponent<RectTransform>();
        _choicesParent.anchorMin = new Vector2(0f, 0f); _choicesParent.anchorMax = new Vector2(1f, 0f);
        _choicesParent.pivot = new Vector2(0.5f, 0f);
        _choicesParent.offsetMin = new Vector2(textLeft, 12f); _choicesParent.offsetMax = new Vector2(-20f, 12f);
        _choicesParent.sizeDelta = new Vector2(_choicesParent.sizeDelta.x, 0f);
        var vlg = choicesGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f; vlg.childAlignment = TextAnchor.LowerLeft;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = choicesGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _choicesParent.gameObject.SetActive(false);
    }

    private void BuildChoiceButton(DialogueChoice choice)
    {
        var btn = BuildButton(_choicesParent, choice.label, _choiceColor, Vector2.zero, Vector2.one,
                              Vector2.zero, new Vector2(0f, 34f));
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 30f; le.preferredHeight = 34f;
        btn.onClick.AddListener(() => OnChoicePicked(choice));
        _choiceButtons.Add(btn.gameObject);
    }

    private void ClearChoiceButtons()
    {
        foreach (var go in _choiceButtons) if (go != null) Destroy(go);
        _choiceButtons.Clear();
    }

    private Button BuildButton(Transform parent, string label, Color color,
                               Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.color = color; img.sprite = _whiteSprite;

        var label3 = NewText("Label", rt, _bodyFontSize, _bodyColor, TextAlignmentOptions.Center);
        var lrt = label3.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(-10f, 0f);
        label3.text = label;

        return go.GetComponent<Button>();
    }

    private static TextMeshProUGUI NewText(string goName, Transform parent, int size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t           = go.GetComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.color         = color;
        t.alignment     = align;
        t.raycastTarget = false;
        return t;
    }

    private static void EnsureWhiteSprite()
    {
        if (_whiteSprite != null) return;
        var tex = Texture2D.whiteTexture;
        _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

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
    [SerializeField] private int   _nameFontSize = 12;
    [SerializeField] private int   _bodyFontSize = 10;
    [SerializeField] private Color _panelColor   = new Color(0.055f, 0.075f, 0.055f, 0.92f);  // HudKit.Ink
    [SerializeField] private Color _nameColor    = new Color(0.08f, 0.07f, 0.06f, 1f);         // near-black on orange chip
    [SerializeField] private Color _bodyColor    = new Color(0.949f, 0.941f, 0.902f, 1f);      // HudKit.OffWhite
    [SerializeField] private Color _choiceColor  = new Color(0.055f, 0.075f, 0.055f, 0.95f);  // HudKit.Ink
    [SerializeField] private Color _hintColor    = new Color(1.00f, 0.227f, 0.125f, 1f);       // HudKit.Danger

    [Header("=== Advance ===")]
    [Tooltip("Key that advances a line (in addition to clicking Continue). Choices are click-only.")]
    [SerializeField] private KeyCode _advanceKey = KeyCode.Space;

    [Header("=== Typewriter ===")]
    [Tooltip("Characters revealed per second during typewriter effect.")]
    [SerializeField] private float _charsPerSecond = 45f;

    // ── Built UI ──────────────────────────────────────────────────────────
    private RectTransform    _panel;
    private CanvasGroup      _panelGroup;
    private Image            _portrait;
    private RectTransform    _nameplateRoot;   // the orange chip (hidden when name is empty)
    private TextMeshProUGUI  _nameText;
    private TextMeshProUGUI  _bodyText;
    private TextMeshProUGUI  _hintText;
    private Button           _continueButton;
    private TextMeshProUGUI  _arrowText;       // animated "▼" next to Continue
    private RectTransform    _choicesParent;
    private readonly List<GameObject> _choiceButtons = new List<GameObject>();

    // ── Playback state ────────────────────────────────────────────────────
    private DialogueConversation _conversation;
    private int                  _lineIndex;
    private bool                 _showingChoices;

    // ── Typewriter state ──────────────────────────────────────────────────
    private Coroutine _typewriterCoroutine;
    private bool      _lineFullyRevealed;

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

        // Force palette — prefab carries pre-overhaul serialized values.
        _panelColor  = HudKit.Ink;
        _nameColor   = new Color(0.06f, 0.05f, 0.03f, 1f);
        _bodyColor   = HudKit.OffWhite;
        _choiceColor = HudKit.Ink;
        _hintColor   = HudKit.Danger;
        _nameFontSize = 12;
        _bodyFontSize = 10;

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

    private void OnDisable()
    {
        // If the panel exists but the conversation is already closed, just hide the panel visually.
        // (Close already handles the open-state cleanup; do not call Close here.)
        if (_panel != null && !IsOpen)
            _panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen || _showingChoices) return;
        if (Input.GetKeyDown(_advanceKey)) TryAdvanceOrReveal();
    }

    // ── Public entry ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens a conversation (built by <see cref="QuestGiver"/>). After a choice it closes; the caller
    /// recomputes what to say on the next interaction.
    /// </summary>
    public void Open(DialogueConversation convo)
    {
        if (convo == null || ((convo.lines == null || convo.lines.Length == 0) &&
                              (convo.choices == null || convo.choices.Length == 0)))
            return;

        bool wasOpen = IsOpen;
        if (!IsOpen)
        {
            GameInputState.Block();
            IsOpen = true;
            _panel.gameObject.SetActive(true);

            // Slide+fade in only when transitioning from closed to open
            _panelGroup.alpha = 0f;
            var startPos = _panel.anchoredPosition;
            _panel.anchoredPosition = new Vector2(startPos.x, startPos.y - 18f);
            _panelGroup.DOFade(1f, 0.18f)
                .SetEase(Ease.OutCubic)
                .SetLink(_panel.gameObject);
            _panel.DOAnchorPosY(startPos.y, 0.18f)
                .SetEase(Ease.OutCubic)
                .SetLink(_panel.gameObject);
        }

        Play(convo);
    }

    // ── Playback ──────────────────────────────────────────────────────────

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
        StopTypewriter();

        _showingChoices = false;
        ClearChoiceButtons();
        _choicesParent.gameObject.SetActive(false);
        _continueButton.gameObject.SetActive(true);

        // Speaker nameplate visibility
        string speakerName = line.speakerName ?? string.Empty;
        _nameText.text = speakerName.ToUpper();
        _nameplateRoot.gameObject.SetActive(!string.IsNullOrEmpty(speakerName));

        bool hasPortrait = line.portrait != null;
        _portrait.gameObject.SetActive(hasPortrait);
        if (hasPortrait) _portrait.sprite = line.portrait;

        // Set full body text once, then animate visibility via typewriter
        _bodyText.text = line.text ?? string.Empty;
        _bodyText.ForceMeshUpdate();
        _bodyText.maxVisibleCharacters = 0;
        _lineFullyRevealed = false;
        _arrowText.gameObject.SetActive(false);

        // Low-risk facts only (e.g. "seen intro"); quest-critical writes belong on choices.
        if (line.writesOnShow != null)
            foreach (var w in line.writesOnShow) w?.Apply();

        _typewriterCoroutine = StartCoroutine(TypewriterRoutine());
    }

    private IEnumerator TypewriterRoutine()
    {
        int totalChars = _bodyText.textInfo.characterCount;
        if (totalChars <= 0)
        {
            RevealLineFully();
            yield break;
        }

        float charsRevealed = 0f;
        float rate = Mathf.Max(1f, _charsPerSecond);

        while (Mathf.RoundToInt(charsRevealed) < totalChars)
        {
            charsRevealed += rate * Time.deltaTime;
            _bodyText.maxVisibleCharacters = Mathf.Min(Mathf.RoundToInt(charsRevealed), totalChars);
            yield return null;
        }

        RevealLineFully();
    }

    private void RevealLineFully()
    {
        StopTypewriter();
        _bodyText.maxVisibleCharacters = int.MaxValue;
        _lineFullyRevealed = true;
        _arrowText.gameObject.SetActive(true);
    }

    private void StopTypewriter()
    {
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }
    }

    /// <summary>
    /// Unified advance/reveal handler. First press reveals line fully; second press advances.
    /// Routed from both the advance key in Update() and the Continue button click.
    /// </summary>
    private void TryAdvanceOrReveal()
    {
        if (!_lineFullyRevealed)
        {
            RevealLineFully();
        }
        else
        {
            Advance();
        }
    }

    private void Advance()
    {
        _lineIndex++;
        ShowCurrentLineOrChoices();
    }

    private void ShowChoices()
    {
        StopTypewriter();

        _showingChoices = true;
        _continueButton.gameObject.SetActive(false);
        _arrowText.gameObject.SetActive(false);
        ClearChoiceButtons();

        var choices = _conversation?.choices;
        if (choices == null || choices.Length == 0) { Close(); return; }

        int shown = 0;
        for (int i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            if (choice == null || !IsChoiceVisible(choice)) continue;
            BuildChoiceButton(choice, shown);
            shown++;
        }

        // No visible choice (e.g. all gated out) — nothing to do, end the conversation.
        if (shown == 0) { Close(); return; }
        _choicesParent.gameObject.SetActive(true);
    }

    private static bool IsChoiceVisible(DialogueChoice choice) =>
        DialogueUtil.ConditionPassesOrEmpty(choice.showIf) &&
        DialogueUtil.PlayerHasItem(choice.takeItem); // takeItem null => true

    // ── Choice resolution (atomic item transaction) ───────────────────────

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
        StopTypewriter();
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

    // ── UI construction ───────────────────────────────────────────────────

    private void Build(Transform canvas)
    {
        // ── Root panel ── 620×150 reference units, bottom-center, 24 units above bottom ──
        var panelGO = new GameObject("DialoguePanel", typeof(RectTransform), typeof(CanvasGroup));
        panelGO.transform.SetParent(canvas, false);
        _panel               = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin     = new Vector2(0.5f, 0.5f);
        _panel.anchorMax     = new Vector2(0.5f, 0.5f);
        _panel.pivot         = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta     = new Vector2(620f, 150f);
        _panel.anchoredPosition = new Vector2(0f, -20f);
        _panelGroup = panelGO.GetComponent<CanvasGroup>();

        // (a) Back plate — Ink, skew 10
        var backPlate = HudKit.Img(panelGO.transform, "BackPlate", HudKit.Ink);
        StretchFull(backPlate.rectTransform);
        HudKit.Skew(backPlate, 10f);

        // (b) OrangeHot accent strip along top edge — height 2.5, extends 8 past each side, skew 10
        var accentStrip = HudKit.Img(panelGO.transform, "AccentStrip", HudKit.OrangeHot);
        var asRT        = accentStrip.rectTransform;
        asRT.anchorMin  = new Vector2(0f, 1f); asRT.anchorMax = new Vector2(1f, 1f);
        asRT.pivot      = new Vector2(0.5f, 1f);
        asRT.offsetMin  = new Vector2(-8f, -2.5f); asRT.offsetMax = new Vector2(8f, 0f);
        HudKit.Skew(accentStrip, 10f);

        // (c) Diagonal stripe overlay — child of panel, stretched to panel (NOT canvas)
        var stripeOverlay = HudKit.Img(panelGO.transform, "StripeOverlay",
            new Color(HudKit.Orange.r, HudKit.Orange.g, HudKit.Orange.b, 0.07f), HudKit.Stripes);
        stripeOverlay.type = Image.Type.Tiled;
        StretchFull(stripeOverlay.rectTransform);

        // ── Portrait region ──────────────────────────────────────────────────
        // Portrait: 95×95, left side, inset 8 from panel left; center-anchored vertically.

        const float portraitW = 95f;
        const float portraitH = 95f;
        const float portraitX = 8f;   // left inset

        // Skewed InkSoft backing behind portrait (+4 each side, skew 8)
        var portraitBacking = HudKit.Img(panelGO.transform, "PortraitBacking", HudKit.InkSoft);
        var pbRT            = portraitBacking.rectTransform;
        pbRT.anchorMin      = new Vector2(0f, 0.5f); pbRT.anchorMax = new Vector2(0f, 0.5f);
        pbRT.pivot          = new Vector2(0f, 0.5f);
        pbRT.sizeDelta      = new Vector2(portraitW + 8f, portraitH + 8f);
        pbRT.anchoredPosition = new Vector2(portraitX - 4f, 0f);
        HudKit.Skew(portraitBacking, 8f);

        // Portrait image
        var portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
        portraitGO.transform.SetParent(panelGO.transform, false);
        var pRT               = portraitGO.GetComponent<RectTransform>();
        pRT.anchorMin         = new Vector2(0f, 0.5f); pRT.anchorMax = new Vector2(0f, 0.5f);
        pRT.pivot             = new Vector2(0f, 0.5f);
        pRT.sizeDelta         = new Vector2(portraitW, portraitH);
        pRT.anchoredPosition  = new Vector2(portraitX, 0f);
        _portrait             = portraitGO.GetComponent<Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget  = false;

        // ── Speaker nameplate ─────────────────────────────────────────────────
        // 150×24 skewed chip overlapping panel top-left edge:
        // anchorMin=anchorMax=(0,1), pivot=(0,0.5), anchoredPosition=(12,0)
        // so the chip center sits on the panel's top edge — half above, half inside.

        const float nameplateW = 150f;
        const float nameplateH = 24f;

        var nameplateRoot = new GameObject("NameplateRoot", typeof(RectTransform));
        nameplateRoot.transform.SetParent(panelGO.transform, false);
        _nameplateRoot          = nameplateRoot.GetComponent<RectTransform>();
        _nameplateRoot.anchorMin = new Vector2(0f, 1f); _nameplateRoot.anchorMax = new Vector2(0f, 1f);
        _nameplateRoot.pivot     = new Vector2(0f, 0.5f);
        _nameplateRoot.sizeDelta = new Vector2(nameplateW, nameplateH);
        _nameplateRoot.anchoredPosition = new Vector2(12f, 0f);

        // GreenDeep echo chip — offset (+3,-3) behind the orange chip
        var echoChip = HudKit.Img(_nameplateRoot, "NameEcho",
            new Color(HudKit.GreenDeep.r, HudKit.GreenDeep.g, HudKit.GreenDeep.b, 0.75f));
        var ecRT     = echoChip.rectTransform;
        StretchFull(ecRT);
        ecRT.offsetMin = new Vector2(3f, -3f); ecRT.offsetMax = new Vector2(3f, -3f);
        HudKit.Skew(echoChip, 10f);

        // Orange chip (foreground)
        var nameChip = HudKit.Img(_nameplateRoot, "NameChip", HudKit.Orange);
        StretchFull(nameChip.rectTransform);
        HudKit.Skew(nameChip, 10f);

        // Name text — near-black (0.06,0.05,0.03), bold-italic, uppercase, font 12.5
        _nameText = HudKit.Text(_nameplateRoot, "NameText", 12.5f,
            _nameColor, TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold | FontStyles.Italic);
        var nRT     = _nameText.rectTransform;
        StretchFull(nRT);
        nRT.offsetMin = new Vector2(10f, 0f); nRT.offsetMax = new Vector2(-10f, 0f);
        _nameText.textWrappingMode = TextWrappingModes.NoWrap;
        _nameText.overflowMode     = TextOverflowModes.Ellipsis;

        // ── Text region ───────────────────────────────────────────────────────
        // Text column left edge: 115 from panel left.

        const float textLeft = 115f;

        // Body text — OffWhite, normal style; top inset 20 (room for nameplate), bottom inset 30
        _bodyText = HudKit.Text(panelGO.transform, "BodyText", _bodyFontSize,
            _bodyColor, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        var bRT    = _bodyText.rectTransform;
        bRT.anchorMin = new Vector2(0f, 0f); bRT.anchorMax = new Vector2(1f, 1f);
        bRT.pivot     = new Vector2(0f, 1f);
        bRT.offsetMin = new Vector2(textLeft, 30f);
        bRT.offsetMax = new Vector2(-20f, -20f);
        _bodyText.textWrappingMode = TextWrappingModes.Normal;
        _bodyText.overflowMode     = TextOverflowModes.Overflow;

        // Hint label — bottom-left in text column, Danger color, font 8.5
        _hintText = HudKit.Text(panelGO.transform, "HintText", 8.5f,
            _hintColor, TextAlignmentOptions.BottomLeft, FontStyles.Italic);
        var hRT    = _hintText.rectTransform;
        hRT.anchorMin = new Vector2(0f, 0f); hRT.anchorMax = new Vector2(1f, 0f);
        hRT.pivot     = new Vector2(0f, 0f);
        hRT.offsetMin = new Vector2(textLeft, 8f); hRT.offsetMax = new Vector2(-10f, 22f);
        _hintText.gameObject.SetActive(false);

        // ── Continue button — 80×18 skewed chip, inside panel bottom-right ──────
        // anchorMin=anchorMax=(1,0), pivot=(1,0), anchoredPosition=(-10, 8)

        var continueGO = new GameObject("Btn_Continue",
            typeof(RectTransform), typeof(Image), typeof(Button));
        continueGO.transform.SetParent(panelGO.transform, false);
        var cRT         = continueGO.GetComponent<RectTransform>();
        cRT.anchorMin   = new Vector2(1f, 0f); cRT.anchorMax = new Vector2(1f, 0f);
        cRT.pivot       = new Vector2(1f, 0f);
        cRT.sizeDelta   = new Vector2(80f, 18f);
        cRT.anchoredPosition = new Vector2(-10f, 8f);

        var cImg        = continueGO.GetComponent<Image>();
        cImg.color      = HudKit.Ink;
        cImg.sprite     = HudKit.White;
        HudKit.Skew(cImg, 10f);

        var cLabel = HudKit.Text(continueGO.transform, "Label", 9.5f,
            HudKit.OffWhite, TextAlignmentOptions.Center,
            FontStyles.Bold | FontStyles.Italic);
        StretchFull(cLabel.rectTransform);
        cLabel.rectTransform.offsetMin = new Vector2(6f, 0f);
        cLabel.rectTransform.offsetMax = new Vector2(-6f, 0f);
        cLabel.text = "CONTINUE";

        _continueButton = continueGO.GetComponent<Button>();
        var cColors              = _continueButton.colors;
        cColors.normalColor      = Color.white;
        cColors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        cColors.pressedColor     = new Color(0.75f, 0.75f, 0.75f, 1f);
        _continueButton.colors   = cColors;
        _continueButton.targetGraphic = cImg;
        _continueButton.onClick.AddListener(TryAdvanceOrReveal);

        // Add hover effect to Continue button
        var continueHover = continueGO.AddComponent<ChoiceHover>();
        continueHover.Configure(cImg, cLabel, null);

        // ── Animated "▼" arrow — anchored to panel bottom-right, 8 units left of Continue ──
        // Continue chip occupies x=[-10-80, -10] = [-90, -10] from right.
        // Arrow sits 8 units further left: anchoredPosition.x = -10 - 80 - 8 = -98.

        var arrowGO = new GameObject("ArrowText", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGO.transform.SetParent(panelGO.transform, false);
        _arrowText            = arrowGO.GetComponent<TextMeshProUGUI>();
        _arrowText.text       = ">";
        _arrowText.fontSize   = 10f;
        _arrowText.color      = HudKit.OrangeHot;
        _arrowText.alignment  = TextAlignmentOptions.Center;
        _arrowText.fontStyle  = FontStyles.Bold;
        _arrowText.raycastTarget = false;
        var aRT               = _arrowText.rectTransform;
        aRT.anchorMin         = new Vector2(1f, 0f); aRT.anchorMax = new Vector2(1f, 0f);
        aRT.pivot             = new Vector2(1f, 0f);
        aRT.sizeDelta         = new Vector2(16f, 18f);
        aRT.anchoredPosition  = new Vector2(-98f, 8f);
        aRT.localEulerAngles  = new Vector3(0f, 0f, -90f); // ">" rotated to point down
        _arrowText.gameObject.SetActive(false);

        // Bob the arrow up and down — amplitude 3 reference units from base y=8
        _arrowText.rectTransform
            .DOAnchorPosY(11f, 0.55f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(_arrowText.gameObject);

        // ── Choices container ─────────────────────────────────────────────────

        var choicesGO = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
        choicesGO.transform.SetParent(panelGO.transform, false);
        _choicesParent           = choicesGO.GetComponent<RectTransform>();
        _choicesParent.anchorMin = new Vector2(0f, 0f);
        _choicesParent.anchorMax = new Vector2(1f, 0f);
        _choicesParent.pivot     = new Vector2(0.5f, 0f);
        _choicesParent.offsetMin = new Vector2(textLeft, 8f);
        _choicesParent.offsetMax = new Vector2(-10f, 8f);
        _choicesParent.sizeDelta = new Vector2(_choicesParent.sizeDelta.x, 0f);
        var vlg                  = choicesGO.GetComponent<VerticalLayoutGroup>();
        vlg.spacing              = 4f;
        vlg.childAlignment       = TextAnchor.LowerLeft;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var fitter               = choicesGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit       = ContentSizeFitter.FitMode.PreferredSize;
        _choicesParent.gameObject.SetActive(false);
    }

    private void BuildChoiceButton(DialogueChoice choice, int index)
    {
        // Container
        var go = new GameObject($"Btn_{choice.label}",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_choicesParent, false);
        var rt       = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, 18f);

        var img      = go.GetComponent<Image>();
        img.color    = HudKit.Ink;
        img.sprite   = HudKit.White;
        HudKit.Skew(img, 8f);

        var btn      = go.GetComponent<Button>();
        var cols     = btn.colors;
        cols.normalColor      = Color.white;
        cols.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        cols.pressedColor     = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors       = cols;
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnChoicePicked(choice));

        var le          = go.AddComponent<LayoutElement>();
        le.minHeight    = 16f;
        le.preferredHeight = 18f;

        // Inner label row container (for marker + text side by side)
        var rowGO = new GameObject("LabelRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        rowGO.transform.SetParent(go.transform, false);
        var rowRT        = rowGO.GetComponent<RectTransform>();
        rowRT.anchorMin  = Vector2.zero; rowRT.anchorMax = Vector2.one;
        rowRT.offsetMin  = new Vector2(8f, 0f); rowRT.offsetMax = new Vector2(-6f, 0f);
        var hlg          = rowGO.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing      = 3f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth  = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        // Orange marker "▸"
        var markerGO = new GameObject("Marker", typeof(RectTransform), typeof(TextMeshProUGUI));
        markerGO.transform.SetParent(rowGO.transform, false);
        var marker           = markerGO.GetComponent<TextMeshProUGUI>();
        marker.text          = ">";
        marker.fontSize      = 9.5f;
        marker.color         = HudKit.Orange;
        marker.fontStyle     = FontStyles.Bold;
        marker.alignment     = TextAlignmentOptions.MidlineLeft;
        marker.raycastTarget = false;
        var markerRT         = marker.rectTransform;
        markerRT.sizeDelta   = new Vector2(12f, 18f);

        // Choice label text — OffWhite, bold-italic, left-aligned, font 9.5
        var labelTmp = HudKit.Text(rowGO.transform, "Label", 9.5f,
            HudKit.OffWhite, TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold | FontStyles.Italic);
        labelTmp.text = choice.label;
        labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
        labelTmp.overflowMode     = TextOverflowModes.Ellipsis;
        var labelRT               = labelTmp.rectTransform;
        labelRT.sizeDelta         = new Vector2(400f, 18f);

        // Hover effect
        var hover = go.AddComponent<ChoiceHover>();
        hover.Configure(img, labelTmp, marker);

        // Stagger slide-in from x+20
        float startX = rt.anchoredPosition.x + 20f;
        float endX   = rt.anchoredPosition.x;
        rt.anchoredPosition = new Vector2(startX, rt.anchoredPosition.y);
        rt.DOAnchorPosX(endX, 0.12f)
          .SetDelay(index * 0.05f)
          .SetEase(Ease.OutCubic)
          .SetLink(go);

        _choiceButtons.Add(go);
    }

    private void ClearChoiceButtons()
    {
        foreach (var go in _choiceButtons) if (go != null) Destroy(go);
        _choiceButtons.Clear();
    }

    // ── Layout helper ─────────────────────────────────────────────────────

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ── Hover effect component ────────────────────────────────────────────

    /// <summary>
    /// Applied to choice buttons (and the Continue button) to provide Persona-style
    /// hover feedback: background → HudKit.Orange, text → near-black, chip slides x+10.
    /// </summary>
    private sealed class ChoiceHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image            _bg;
        private TextMeshProUGUI  _label;
        private TextMeshProUGUI  _marker;   // may be null (Continue button has no marker)
        private RectTransform    _rt;

        private Color _bgColorDefault;
        private Color _labelColorDefault;
        private Color _markerColorDefault;
        private float _defaultX;

        private static readonly Color s_nearBlack = new Color(0.08f, 0.07f, 0.06f, 1f);

        public void Configure(Image bg, TextMeshProUGUI label, TextMeshProUGUI marker)
        {
            _bg     = bg;
            _label  = label;
            _marker = marker;
            _rt     = GetComponent<RectTransform>();

            _bgColorDefault     = bg.color;
            _labelColorDefault  = label.color;
            if (marker != null) _markerColorDefault = marker.color;
            _defaultX = _rt.anchoredPosition.x;
        }

        public void OnPointerEnter(PointerEventData _)
        {
            if (_bg    != null) _bg.DOColor(HudKit.Orange, 0.1f).SetLink(_bg.gameObject);
            if (_label != null) _label.DOColor(s_nearBlack, 0.1f).SetLink(_label.gameObject);
            if (_marker != null) _marker.DOColor(s_nearBlack, 0.1f).SetLink(_marker.gameObject);
            if (_rt != null) _rt.DOAnchorPosX(_defaultX + 10f, 0.08f)
                .SetEase(Ease.OutQuad)
                .SetLink(_rt.gameObject);
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (_bg    != null) _bg.DOColor(_bgColorDefault, 0.15f).SetLink(_bg.gameObject);
            if (_label != null) _label.DOColor(_labelColorDefault, 0.15f).SetLink(_label.gameObject);
            if (_marker != null) _marker.DOColor(_markerColorDefault, 0.15f).SetLink(_marker.gameObject);
            if (_rt != null) _rt.DOAnchorPosX(_defaultX, 0.1f)
                .SetEase(Ease.OutQuad)
                .SetLink(_rt.gameObject);
        }
    }
}

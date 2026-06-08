using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The easy way to make a quest-giving NPC: a STACKED LIST of stages, top to bottom. Each stage is
/// "say these lines → give this quest", and a stage automatically requires the stage ABOVE it to be
/// finished first. So a whole questline reads like a script you fill in — no states, no WSM keys.
///
/// Per talk, this figures out what to say from the live quest statuses:
///   • stage not started + reachable → OFFER lines (Accept activates the quest)
///   • stage active, not done        → IN-PROGRESS lines (+ Hand-It-Over if a turn-in item is held)
///   • a stage just completed         → its DONE lines, then the next stage's offer, in one go
///   • nothing available yet          → IDLE lines ("nothing for you right now")
///
/// After editing stages, click "Set Up Questline" in the inspector once — it wires each quest's
/// accept-gate + chain order (so quests only start after talking, in order). Rendering reuses
/// <see cref="DialogueUI"/>.
/// </summary>
public class QuestGiver : MonoBehaviour, IInteractable
{
    [Serializable]
    public class QuestStage
    {
        [Tooltip("Editor label so you can tell stages apart in the list.")]
        public string label;

        [Tooltip("The quest this stage gives the player.")]
        public QuestSO quest;

        [Tooltip("Optional EXTERNAL quest (from another NPC) that must also be done before this stage " +
                 "unlocks. The stage ABOVE in this list is always required automatically.")]
        public QuestSO requiresQuestBefore;

        [Tooltip("Optional: the item the player hands in to complete this stage. Leave empty for a " +
                 "do-it-in-the-world quest (its Objective asset completes it).")]
        public ItemSO turnInItem;

        public Sprite portrait;

        [TextArea(1, 4)] public string[] offerLines      = { "I've got a job for you." };
        [TextArea(1, 4)] public string[] inProgressLines = { "Come back when it's done." };
        [TextArea(1, 4)] public string[] doneLines       = { "Nice work." };

        [Tooltip("Show an explicit 'Accept' button. If off, the quest activates as the last offer line is read.")]
        public bool requireAcceptChoice = true;
    }

    [Header("=== Identity ===")]
    [SerializeField] private string _prompt = "Talk";
    [SerializeField] private string _speakerName = "NPC";
    [SerializeField] private Sprite _defaultPortrait;

    [Tooltip("Said when no stage is available yet (waiting on a prerequisite) or the whole line is finished.")]
    [TextArea(1, 4)] [SerializeField] private string[] _idleLines = { "I don't have anything for you right now." };

    [Header("=== Questline (top to bottom; each stage needs the one above done first) ===")]
    [SerializeField] private List<QuestStage> _stages = new List<QuestStage>();

    public IReadOnlyList<QuestStage> Stages => _stages;

    // ── IInteractable ──────────────────────────────────────────────────────

    public bool   CanInteract(GameObject interactor) => DialogueUI.Instance != null;
    public string GetPrompt(GameObject interactor)   => _prompt;

    private bool _checkedRegistration;

    public void Interact(GameObject interactor)
    {
        if (DialogueUI.Instance == null)
        {
            Debug.LogWarning($"[QuestGiver] '{name}' has no DialogueUI in the scene.", this);
            return;
        }

        if (!_checkedRegistration) { _checkedRegistration = true; WarnIfQuestsUnregistered(); }

        DialogueUI.Instance.Open(BuildConversation());
    }

    /// <summary>
    /// Surfaces the #1 setup mistake: a quest referenced here but not added to the QuestManager's
    /// Quests list (so it can never activate, repeats forever, and never shows in the tracker).
    /// </summary>
    private void WarnIfQuestsUnregistered()
    {
        var qm = QuestManager.Instance;
        if (qm == null)
        {
            Debug.LogWarning($"[QuestGiver] '{name}': no QuestManager in the scene — quests can't activate or show.", this);
            return;
        }
        foreach (var st in _stages)
        {
            if (st?.quest == null) continue;
            bool registered = false;
            foreach (var q in qm.Quests) if (q == st.quest) { registered = true; break; }
            if (!registered)
                Debug.LogWarning($"[QuestGiver] '{name}': quest '{st.quest.title}' is NOT in the " +
                                 $"QuestManager's Quests list — drag it in, or it will never activate, " +
                                 $"will repeat its offer, and won't appear in the tracker.", this);
        }
    }

    /// <summary>
    /// True if this NPC currently has something for the player: a new quest to OFFER (reachable +
    /// not yet accepted) or a turn-in the player can hand in right now. Drives <see cref="QuestGiverIcon"/>.
    /// </summary>
    public bool HasSomethingForPlayer()
    {
        int front = -1;
        for (int i = 0; i < _stages.Count; i++)
            if (_stages[i]?.quest != null && !IsSucceeded(_stages[i].quest)) { front = i; break; }
        if (front == -1) return false;

        var stage = _stages[front];
        bool prevDone   = front == 0 || _stages[front - 1].quest == null || IsSucceeded(_stages[front - 1].quest);
        bool prereqDone = stage.requiresQuestBefore == null || IsSucceeded(stage.requiresQuestBefore);
        if (!prevDone || !prereqDone) return false;

        var status = StatusOf(stage.quest);
        if (status == QuestStatus.Inactive) return true; // a new offer is waiting
        if (status == QuestStatus.Active && stage.turnInItem != null &&
            DialogueUtil.PlayerHasItem(stage.turnInItem)) return true; // ready to hand in
        return false;
    }

    // ── Decide what to say ─────────────────────────────────────────────────

    private DialogueConversation BuildConversation()
    {
        // Front stage = first whose quest isn't finished yet.
        int front = -1;
        for (int i = 0; i < _stages.Count; i++)
            if (_stages[i]?.quest != null && !IsSucceeded(_stages[i].quest)) { front = i; break; }

        // Whole questline done → final stage's done lines (or idle).
        if (front == -1)
        {
            var last = _stages.Count > 0 ? _stages[_stages.Count - 1] : null;
            return Simple(last != null && last.doneLines.Length > 0 ? last.doneLines : _idleLines, last?.portrait);
        }

        var stage = _stages[front];

        bool prevDone   = front == 0 || _stages[front - 1].quest == null || IsSucceeded(_stages[front - 1].quest);
        bool prereqDone = stage.requiresQuestBefore == null || IsSucceeded(stage.requiresQuestBefore);
        if (!prevDone || !prereqDone)
            return Simple(_idleLines, stage.portrait); // not this NPC's turn yet

        switch (StatusOf(stage.quest))
        {
            case QuestStatus.Inactive:
            {
                // Offer. If the stage above JUST finished, lead with its done lines (acknowledge → new offer).
                var lines = new List<string>();
                if (front > 0 && _stages[front - 1].quest != null && IsSucceeded(_stages[front - 1].quest))
                    lines.AddRange(_stages[front - 1].doneLines);
                lines.AddRange(stage.offerLines);
                return Offer(lines, stage);
            }
            case QuestStatus.Active:
                return Active(stage);
            default:
                return Simple(_idleLines, stage.portrait);
        }
    }

    private DialogueConversation Offer(List<string> lines, QuestStage stage)
    {
        var convo = new DialogueConversation { lines = ToLines(lines.ToArray(), stage.portrait) };
        string acceptedKey = AcceptedKey(stage.quest);

        if (stage.requireAcceptChoice)
        {
            convo.choices = new[]
            {
                new DialogueChoice { label = "Accept", writesOnPick = new[] { WriteBool(acceptedKey) } }
            };
        }
        else if (convo.lines.Length > 0)
        {
            convo.lines[convo.lines.Length - 1].writesOnShow = new[] { WriteBool(acceptedKey) };
        }
        return convo;
    }

    private DialogueConversation Active(QuestStage stage)
    {
        var convo = new DialogueConversation { lines = ToLines(stage.inProgressLines, stage.portrait) };

        // Hand-in option only appears while the player is actually carrying the item.
        if (stage.turnInItem != null && DialogueUtil.PlayerHasItem(stage.turnInItem))
        {
            convo.choices = new[]
            {
                new DialogueChoice
                {
                    label = "Hand it over",
                    takeItem = stage.turnInItem,
                    writesOnPick = new[] { WriteBool(DeliveredKey(stage.quest)) }
                },
                new DialogueChoice { label = "Not yet" }
            };
        }
        return convo;
    }

    private DialogueConversation Simple(string[] texts, Sprite portrait) =>
        new DialogueConversation { lines = ToLines(texts, portrait) };

    // ── Helpers ────────────────────────────────────────────────────────────

    private DialogueLine[] ToLines(string[] texts, Sprite portrait)
    {
        if (texts == null || texts.Length == 0) return Array.Empty<DialogueLine>();
        var arr = new DialogueLine[texts.Length];
        for (int i = 0; i < texts.Length; i++)
            arr[i] = new DialogueLine
            {
                speakerName = _speakerName,
                portrait    = portrait != null ? portrait : _defaultPortrait,
                text        = texts[i]
            };
        return arr;
    }

    private static bool IsSucceeded(QuestSO q) =>
        QuestManager.Instance != null && QuestManager.Instance.GetStatus(q) == QuestStatus.Succeeded;

    private static QuestStatus StatusOf(QuestSO q) =>
        QuestManager.Instance != null ? QuestManager.Instance.GetStatus(q) : QuestStatus.Inactive;

    public static string AcceptedKey(QuestSO q)  => $"dialogue.quest.{q.QuestId}.accepted";
    public static string DeliveredKey(QuestSO q) => $"dialogue.quest.{q.QuestId}.delivered";

    private static DialogueWrite WriteBool(string key) =>
        new DialogueWrite { key = key, type = QuestValueType.Bool, boolValue = true };
}

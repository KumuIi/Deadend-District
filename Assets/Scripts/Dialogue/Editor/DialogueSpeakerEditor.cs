using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="DialogueSpeaker"/> with a one-click quest-dialogue scaffold.
///
/// "Generate Simple Quest Dialogue" builds the standard gated states (Done / TurnIn / Waiting /
/// Offer, plus an optional chain-only Reward that activates a follow-up quest) from a few text
/// boxes — using questId-derived WSM keys so you never type a key. It writes ONLY normal
/// DialogueSpeaker data (one runtime system; no parallel quest-dialogue type).
///
/// "Apply Quest Wiring" is the ONLY thing that touches the QuestSO asset, and only on an explicit
/// click (never in OnValidate): it points the quest's activeCondition at dialogue.quest.{id}.accepted
/// and its first objective at dialogue.quest.{id}.delivered, creating the objective if missing.
/// </summary>
[CustomEditor(typeof(DialogueSpeaker))]
public class DialogueSpeakerEditor : Editor
{
    // Generator inputs (editor-local; reset when selection changes — fine for a one-shot scaffold).
    private QuestSO _quest;
    private ItemSO  _turnInItem;
    private QuestSO _nextQuest;
    private string  _speakerName = "";
    private bool    _requireAcceptChoice = true;

    private string _offerText      = "I've got a job for you.";
    private string _inProgressText = "Come back when it's done.";
    private string _turnInText     = "You've got it? Hand it over.";
    private string _doneText       = "Thanks again.";
    private string _rewardText     = "Good work. I've got something else for you.";

    private bool _foldout = true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        _foldout = EditorGUILayout.BeginFoldoutHeaderGroup(_foldout, "Quick Quest Dialogue Generator");
        if (_foldout)
        {
            EditorGUILayout.HelpBox(
                "Fill the quest + texts, click Generate to build the conversation states, then " +
                "Apply Quest Wiring to point the quest at the generated flags. You never type a WSM key.",
                MessageType.Info);

            _quest       = (QuestSO)EditorGUILayout.ObjectField("Quest", _quest, typeof(QuestSO), false);
            _turnInItem  = (ItemSO) EditorGUILayout.ObjectField("Turn-In Item (optional)", _turnInItem, typeof(ItemSO), false);
            _nextQuest   = (QuestSO)EditorGUILayout.ObjectField("Next Quest (optional)", _nextQuest, typeof(QuestSO), false);
            _speakerName = EditorGUILayout.TextField("Speaker Name", _speakerName);
            _requireAcceptChoice = EditorGUILayout.Toggle("Require 'Accept' Choice", _requireAcceptChoice);

            EditorGUILayout.LabelField("Lines", EditorStyles.boldLabel);
            _offerText      = EditorGUILayout.TextField("Offer", _offerText);
            _inProgressText = EditorGUILayout.TextField("In-Progress", _inProgressText);
            _turnInText     = EditorGUILayout.TextField("Turn-In", _turnInText);
            _doneText       = EditorGUILayout.TextField("Done", _doneText);
            if (_nextQuest != null)
                _rewardText = EditorGUILayout.TextField("Reward / Next-Offer", _rewardText);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_quest == null))
            {
                if (GUILayout.Button("Generate Simple Quest Dialogue"))
                    Generate((DialogueSpeaker)target);

                if (GUILayout.Button("Apply Quest Wiring (edits the QuestSO)"))
                    ApplyQuestWiring();
            }
            if (_quest == null)
                EditorGUILayout.HelpBox("Assign a Quest to enable generation.", MessageType.Warning);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ── Generation ───────────────────────────────────────────────────────────

    private void Generate(DialogueSpeaker speaker)
    {
        string id           = _quest.QuestId;
        string acceptedKey  = AcceptedKey(_quest);
        string deliveredKey = DeliveredKey(_quest);
        string activeFlag   = $"quest.{id}.active";
        string succeededKey = $"quest.{id}.succeeded";

        bool hasNext     = _nextQuest != null;
        int  rewardIndex = 4; // states are added in fixed order below

        var states = new List<DialogueSpeaker.ConversationState>();

        // [0] Done — quest already succeeded.
        states.Add(MakeState("Done", BoolCond(succeededKey, true), null,
            Convo(_doneText)));

        // [1] TurnIn — active AND carrying the item. Give (take item + deliver flag) / Decline.
        var give = new DialogueChoice
        {
            label        = "Hand it over",
            takeItem     = _turnInItem,
            writesOnPick = new[] { WriteBool(deliveredKey) },
            nextStateIndex = hasNext ? rewardIndex : -1
        };
        var decline = new DialogueChoice { label = "Not yet", nextStateIndex = -1 };
        states.Add(MakeState("TurnIn", BoolCond(activeFlag, true), _turnInItem,
            Convo(_turnInText, give, decline)));

        // [2] Waiting — active, item not in hand yet.
        states.Add(MakeState("Waiting", BoolCond(activeFlag, true), null,
            Convo(_inProgressText)));

        // [3] Offer — default (no condition). Accept writes the accepted flag → quest activates.
        DialogueConversation offer;
        if (_requireAcceptChoice)
        {
            var accept = new DialogueChoice
            {
                label        = "Accept",
                writesOnPick = new[] { WriteBool(acceptedKey) },
                nextStateIndex = -1
            };
            offer = Convo(_offerText, accept);
        }
        else
        {
            // Auto-accept: commit the flag on the (final) offer line as it's shown.
            offer = Convo(_offerText);
            offer.lines[offer.lines.Length - 1].writesOnShow = new[] { WriteBool(acceptedKey) };
        }
        states.Add(MakeState("Offer", EmptyCond(), null, offer));

        // [4] Reward (chain-only) — reached from TurnIn's Give when a follow-up quest is set.
        if (hasNext)
        {
            var reward = MakeState("Reward", EmptyCond(), null,
                Convo(_rewardText));
            reward.chainOnly = true;
            reward.inline.choices = null;
            // Activate the follow-up by writing its accepted flag on the reward line.
            reward.inline.lines[reward.inline.lines.Length - 1].writesOnShow =
                new[] { WriteBool(AcceptedKey(_nextQuest)) };
            states.Add(reward);
        }

        Undo.RecordObject(speaker, "Generate Quest Dialogue");
        speaker.EditorSetStates(states);
        if (!string.IsNullOrEmpty(_speakerName)) speaker.EditorSetPrompt($"Talk to {_speakerName}");
        EditorUtility.SetDirty(speaker);

        Debug.Log($"[DialogueSpeakerEditor] Generated {states.Count} states for '{speaker.name}'. " +
                  $"Now click 'Apply Quest Wiring' to point '{_quest.title}' at the generated flags.", speaker);
    }

    // ── Quest wiring (explicit, only on button press) ────────────────────────

    private void ApplyQuestWiring()
    {
        string acceptedKey  = AcceptedKey(_quest);
        string deliveredKey = DeliveredKey(_quest);

        string msg = $"This will edit the asset '{_quest.name}':\n\n" +
                     $"• activeCondition → {acceptedKey} == true\n" +
                     $"• objective[0]   → {deliveredKey} == true\n";
        if (_nextQuest != null)
            msg += $"\nAnd '{_nextQuest.name}':\n• activeCondition → {AcceptedKey(_nextQuest)} == true\n";
        msg += "\nExisting values on these fields will be overwritten. Continue?";

        if (!EditorUtility.DisplayDialog("Apply Quest Wiring", msg, "Apply", "Cancel")) return;

        Undo.RecordObject(_quest, "Apply Quest Wiring");
        _quest.activeCondition = BoolCond(acceptedKey, true);

        if (_quest.objectives == null || _quest.objectives.Length == 0)
            _quest.objectives = new QuestObjectiveDefinition[1];
        _quest.objectives[0] ??= new QuestObjectiveDefinition();
        _quest.objectives[0].condition = BoolCond(deliveredKey, true);
        if (string.IsNullOrEmpty(_quest.objectives[0].description))
            _quest.objectives[0].description = !string.IsNullOrEmpty(_quest.trackerText)
                ? _quest.trackerText : "Deliver the item";
        EditorUtility.SetDirty(_quest);

        if (_nextQuest != null)
        {
            Undo.RecordObject(_nextQuest, "Apply Quest Wiring (next)");
            _nextQuest.activeCondition = BoolCond(AcceptedKey(_nextQuest), true);
            EditorUtility.SetDirty(_nextQuest);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[DialogueSpeakerEditor] Wired '{_quest.title}' (and any next quest) to the generated dialogue flags.");
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static string AcceptedKey(QuestSO q)  => $"dialogue.quest.{q.QuestId}.accepted";
    private static string DeliveredKey(QuestSO q)  => $"dialogue.quest.{q.QuestId}.delivered";

    private static QuestConditionDefinition EmptyCond() => new QuestConditionDefinition();

    private static QuestConditionDefinition BoolCond(string key, bool expected) => new QuestConditionDefinition
    {
        wsmKey       = key,
        valueType    = QuestValueType.Bool,
        comparison   = QuestComparison.Equals,
        expectedBool = expected
    };

    private static DialogueWrite WriteBool(string key, bool value = true) => new DialogueWrite
    {
        key       = key,
        type      = QuestValueType.Bool,
        boolValue = value
    };

    private DialogueConversation Convo(string text, params DialogueChoice[] choices) => new DialogueConversation
    {
        lines   = new[] { new DialogueLine { speakerName = _speakerName, text = text } },
        choices = (choices != null && choices.Length > 0) ? choices : null
    };

    private static DialogueSpeaker.ConversationState MakeState(
        string label, QuestConditionDefinition when, ItemSO requiredItem, DialogueConversation convo) =>
        new DialogueSpeaker.ConversationState
        {
            label        = label,
            when         = when,
            requiredItem = requiredItem,
            inline       = convo
        };
}

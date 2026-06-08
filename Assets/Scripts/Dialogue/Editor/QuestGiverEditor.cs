using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="QuestGiver"/> with a single "Set Up Questline" button that wires every
/// stage's quest — the ONLY thing that edits the QuestSO assets, and only on an explicit click.
/// For each stage it sets:
///   • activeCondition → dialogue.quest.{id}.accepted  (so the quest only starts after talking)
///   • Required Quests += the stage above (+ any external 'requires quest before')  (chain order)
///   • for a turn-in stage: objective[0] → dialogue.quest.{id}.delivered
/// Objective-driven stages keep whatever Objective asset you dragged into the quest.
/// </summary>
[CustomEditor(typeof(QuestGiver))]
public class QuestGiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "After editing the stages, click this ONCE. It wires each quest so it only starts after the " +
            "player talks (Accept), and so each stage requires the one above. You never type a WSM key.",
            MessageType.Info);

        if (GUILayout.Button("Set Up Questline (wire the quests)"))
            SetUp((QuestGiver)target);
    }

    private void SetUp(QuestGiver giver)
    {
        var stages = giver.Stages;
        int wired = 0;

        for (int i = 0; i < stages.Count; i++)
        {
            var st = stages[i];
            if (st?.quest == null) continue;

            Undo.RecordObject(st.quest, "Set Up Questline");

            // Only starts after the player accepts in dialogue.
            st.quest.activeCondition = BoolCond(QuestGiver.AcceptedKey(st.quest));

            // Chain order: previous stage's quest + any external prerequisite.
            var reqs = new List<QuestSO>(st.quest.requiredQuests ?? new QuestSO[0]);
            if (i > 0 && stages[i - 1].quest != null && !reqs.Contains(stages[i - 1].quest))
                reqs.Add(stages[i - 1].quest);
            if (st.requiresQuestBefore != null && !reqs.Contains(st.requiresQuestBefore))
                reqs.Add(st.requiresQuestBefore);
            st.quest.requiredQuests = reqs.ToArray();

            // Turn-in stages: the hand-in IS the objective. Objective-driven stages are left alone.
            if (st.turnInItem != null)
            {
                if (st.quest.objectives == null || st.quest.objectives.Length == 0)
                    st.quest.objectives = new QuestObjectiveDefinition[1];
                st.quest.objectives[0] ??= new QuestObjectiveDefinition();
                st.quest.objectives[0].condition = BoolCond(QuestGiver.DeliveredKey(st.quest));
                if (string.IsNullOrEmpty(st.quest.objectives[0].description))
                    st.quest.objectives[0].description = !string.IsNullOrEmpty(st.quest.trackerText)
                        ? st.quest.trackerText : "Deliver the item";
            }

            EditorUtility.SetDirty(st.quest);
            wired++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[QuestGiver] Wired {wired} quest(s) on '{giver.name}'.", giver);
    }

    private static QuestConditionDefinition BoolCond(string key) => new QuestConditionDefinition
    {
        wsmKey       = key,
        valueType    = QuestValueType.Bool,
        comparison   = QuestComparison.Equals,
        expectedBool = true
    };
}

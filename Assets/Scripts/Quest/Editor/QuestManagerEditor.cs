using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds an "Auto-Fill Quests From Project" button to <see cref="QuestManager"/> so you don't have to
/// drag every QuestSO into the Quests list by hand — it scans the whole project and fills the list.
/// </summary>
[CustomEditor(typeof(QuestManager))]
public class QuestManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Auto-Fill scans the project for every Quest asset and fills the Quests list. " +
            "Click it whenever you add new quests — a quest that isn't in this list never activates.",
            MessageType.Info);

        if (GUILayout.Button("Auto-Fill Quests From Project"))
            AutoFill();
    }

    private void AutoFill()
    {
        var quests = new List<QuestSO>();
        foreach (var guid in AssetDatabase.FindAssets("t:QuestSO"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var q = AssetDatabase.LoadAssetAtPath<QuestSO>(path);
            if (q != null) quests.Add(q);
        }

        var so   = new SerializedObject(target);
        var prop = so.FindProperty("_quests");
        prop.arraySize = quests.Count;
        for (int i = 0; i < quests.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = quests[i];
        so.ApplyModifiedProperties();

        Debug.Log($"[QuestManager] Auto-filled {quests.Count} quest(s) from the project.", target);
    }
}

using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="ObjectiveSO"/> that shows ONLY the fields relevant to the chosen
/// type — so a "Reach Zone" objective isn't cluttered with kill-team / item-filter fields, etc.
/// The amount field gets a context-aware label (Credits / Seconds / Kills…).
/// </summary>
[CustomEditor(typeof(ObjectiveSO))]
public class ObjectiveSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
        var typeProp = serializedObject.FindProperty("type");
        EditorGUILayout.PropertyField(typeProp);

        var type = (ObjectiveType)typeProp.enumValueIndex;

        string amountLabel = type switch
        {
            ObjectiveType.ReachCurrency      => "Credits Needed",
            ObjectiveType.SurviveSeconds     => "Seconds",
            ObjectiveType.CollectItems       => "Items To Collect",
            ObjectiveType.KillEnemies        => "Kills",
            ObjectiveType.UnlockAnyDoor      => "Doors To Unlock",
            ObjectiveType.UnlockAnyShortcut  => "Shortcuts To Open",
            ObjectiveType.UseRechargeStation => "Recharges",
            _                                => null // ReachZone / CustomFlag don't use amount
        };
        if (amountLabel != null)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("amount"), new GUIContent(amountLabel));

        if (type == ObjectiveType.CollectItems)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("itemFilter"));

        if (type == ObjectiveType.KillEnemies)
        {
            var useFilter = serializedObject.FindProperty("useTeamFilter");
            EditorGUILayout.PropertyField(useFilter);
            if (useFilter.boolValue)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("killTeam"));
        }

        if (type == ObjectiveType.CustomFlag)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("customKey"));

        EditorGUILayout.Space(6);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("accrual"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("resetEachRun"));

        if (type == ObjectiveType.ReachZone)
            EditorGUILayout.HelpBox(
                "Reach Zone: drop an ObjectiveTrigger (a box collider with Is Trigger on) in the world " +
                "and drag this objective onto it. Walking in completes it.", MessageType.Info);
        else if (type == ObjectiveType.CustomFlag)
            EditorGUILayout.HelpBox(
                "Custom Flag: done when the chosen WSM bool is true. Use only when no built-in type fits.",
                MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }
}

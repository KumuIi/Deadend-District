using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for LootPoolSO. Keeps the normal entry list (item + weight) editable, then
/// shows a live read-only breakdown of each entry's actual DROP CHANCE as a percentage —
/// normalized across every valid entry — so designers tune by % without doing the math.
/// </summary>
[CustomEditor(typeof(LootPoolSO))]
public class LootPoolSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // item + weight fields, add/remove/reorder as usual

        var pool = (LootPoolSO)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Drop Chances", EditorStyles.boldLabel);

        if (pool.Entries == null || pool.Entries.Length == 0)
        {
            EditorGUILayout.HelpBox("No entries yet.", MessageType.Info);
            return;
        }

        float total = 0f;
        foreach (var e in pool.Entries)
            if (e.Item != null && e.Weight > 0f) total += e.Weight;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (total <= 0f)
            {
                EditorGUILayout.HelpBox("All weights are zero — nothing will ever drop.", MessageType.Warning);
                return;
            }

            foreach (var e in pool.Entries)
            {
                string label = e.Item != null ? e.Item.name : "<none>";
                bool   valid = e.Item != null && e.Weight > 0f;
                float  pct   = valid ? (e.Weight / total) * 100f : 0f;
                EditorGUILayout.LabelField(label, valid ? $"{pct:0.0}%" : "0% (ignored)");
            }
        }
    }
}

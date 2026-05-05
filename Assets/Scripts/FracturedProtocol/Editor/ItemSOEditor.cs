#nullable enable
using UnityEditor;
using FracturedProtocol.Combat.Items;

namespace FracturedProtocol.Combat.Editor
{
    /// <summary>
    /// Custom inspector for all ItemSO subclasses. Displays itemId as read-only
    /// so it is visible but cannot be hand-edited.
    /// </summary>
    [CustomEditor(typeof(ItemSO), true)]
    public sealed class ItemSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            ItemSO item = (ItemSO)target;

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Item ID", item.ItemId);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}

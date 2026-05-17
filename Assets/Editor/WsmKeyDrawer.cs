using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Property drawer for [WsmKey] string fields.
/// Shows a searchable GenericMenu popup backed by WsmKeyRegistrySO.
/// Falls back to a plain text field if no registry asset exists.
/// </summary>
[CustomPropertyDrawer(typeof(WsmKeyAttribute))]
public class WsmKeyDrawer : PropertyDrawer
{
    private static WsmKeyRegistrySO _registry;
    private static double           _lastRegistryLoad;
    private const  double           RegistryCacheTtl = 5.0;

    private const float ButtonWidth  = 52f;
    private const float WarningWidth = 20f;
    private const float Spacing      = 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[WsmKey] requires a string field");
            return;
        }

        RefreshRegistry();

        EditorGUI.BeginProperty(position, label, property);

        if (_registry == null)
        {
            // No registry — plain text field with a hint tooltip
            var noReg = EditorGUI.TextField(position,
                new GUIContent(label.text, "No WsmKeyRegistry asset found. Create one via Assets › Create › WSM › Key Registry."),
                property.stringValue);
            if (noReg != property.stringValue) property.stringValue = noReg;
            EditorGUI.EndProperty();
            return;
        }

        string current    = property.stringValue;
        bool   inRegistry = FindEntry(current, out var currentEntry);

        // ── Layout ────────────────────────────────────────────────────────────
        float warningW = (!inRegistry && !string.IsNullOrEmpty(current)) ? WarningWidth + Spacing : 0f;
        float btnW     = ButtonWidth + Spacing;
        float labelW   = EditorGUIUtility.labelWidth;

        Rect warningRect = new(position.x, position.y, warningW, position.height);
        Rect labelRect   = new(position.x + warningW, position.y, labelW, position.height);
        Rect valueRect   = new(position.x + warningW + labelW, position.y,
                               position.width - warningW - labelW - btnW, position.height);
        Rect btnRect     = new(position.xMax - ButtonWidth, position.y, ButtonWidth, position.height);

        // Warning icon for unknown keys
        if (warningW > 0f)
        {
            var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
            icon.tooltip = $"'{current}' is not in the WsmKeyRegistry.";
            GUI.Label(warningRect, icon);
        }

        EditorGUI.LabelField(labelRect, label);

        // Display label for the current value
        string display = BuildDisplay(currentEntry, current);
        var    style   = currentEntry?.deprecated == true ? DeprecatedStyle() : EditorStyles.popup;

        // Clicking the popup button opens the GenericMenu exactly once (GUI.Button is true only on click frame)
        if (GUI.Button(valueRect, new GUIContent(display, current), style))
            ShowMenu(property);

        // Side button: "Add" if key unknown, "Edit" if known
        if (!inRegistry && !string.IsNullOrEmpty(current))
        {
            if (GUI.Button(btnRect, new GUIContent("+ Add", $"Add '{current}' to the registry")))
                AddToRegistry(current);
        }
        else
        {
            if (GUI.Button(btnRect, new GUIContent("Edit", "Open WsmKeyRegistry asset")))
                Selection.activeObject = _registry;
        }

        EditorGUI.EndProperty();
    }

    // ── Menu ─────────────────────────────────────────────────────────────────

    private static void ShowMenu(SerializedProperty property)
    {
        string current = property.stringValue;
        var    menu    = new GenericMenu();

        // None option
        menu.AddItem(new GUIContent("— none —"), string.IsNullOrEmpty(current), () =>
        {
            property.stringValue = "";
            property.serializedObject.ApplyModifiedProperties();
        });
        menu.AddSeparator("");

        if (_registry?.keys != null)
        {
            var entries = _registry.keys
                .Where(e => e != null && !string.IsNullOrEmpty(e.key))
                .OrderBy(e => e.deprecated)
                .ThenBy(e => string.IsNullOrEmpty(e.category) ? "Uncategorized" : e.category)
                .ThenBy(e => string.IsNullOrEmpty(e.displayName) ? e.key : e.displayName);

            foreach (var entry in entries)
            {
                var captured = entry;
                menu.AddItem(
                    new GUIContent(BuildMenuPath(entry)),
                    current == entry.key,
                    () =>
                    {
                        property.stringValue = captured.key;
                        property.serializedObject.ApplyModifiedProperties();
                    });
            }
        }

        menu.ShowAsContext();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildDisplay(WsmKeyEntry entry, string raw)
    {
        if (entry == null) return string.IsNullOrEmpty(raw) ? "— none —" : raw;
        string cat = string.IsNullOrEmpty(entry.category) ? "" : $"[{entry.category}]  ";
        string dep = entry.deprecated ? "  ⚠ deprecated" : "";
        string name = string.IsNullOrEmpty(entry.displayName) ? entry.key : entry.displayName;
        return $"{cat}{name}{dep}";
    }

    private static string BuildMenuPath(WsmKeyEntry e)
    {
        string cat  = string.IsNullOrEmpty(e.category) ? "Uncategorized" : e.category;
        string name = string.IsNullOrEmpty(e.displayName) ? e.key : e.displayName;
        string dep  = e.deprecated ? " [deprecated]" : "";
        return $"{cat}/{name}{dep}";
    }

    private static bool FindEntry(string key, out WsmKeyEntry entry)
    {
        entry = null;
        if (_registry?.keys == null || string.IsNullOrEmpty(key)) return false;
        foreach (var e in _registry.keys)
            if (e != null && e.key == key) { entry = e; return true; }
        return false;
    }

    private static void RefreshRegistry()
    {
        if (_registry != null && EditorApplication.timeSinceStartup - _lastRegistryLoad < RegistryCacheTtl)
            return;

        _lastRegistryLoad = EditorApplication.timeSinceStartup;
        var guids = AssetDatabase.FindAssets("t:WsmKeyRegistrySO");
        if (guids.Length == 0) { _registry = null; return; }

        _registry = AssetDatabase.LoadAssetAtPath<WsmKeyRegistrySO>(
            AssetDatabase.GUIDToAssetPath(guids[0]));

        if (guids.Length > 1)
            Debug.LogWarning("[WsmKeyDrawer] Multiple WsmKeyRegistrySO assets found — using the first. Keep only one.");
    }

    private static void AddToRegistry(string key)
    {
        if (_registry == null) return;
        var list = _registry.keys?.ToList() ?? new List<WsmKeyEntry>();
        list.Add(new WsmKeyEntry { key = key, displayName = key, type = QuestValueType.Bool });
        _registry.keys = list.ToArray();
        EditorUtility.SetDirty(_registry);
        AssetDatabase.SaveAssets();
        _registry = null; // force cache refresh next draw
        Debug.Log($"[WsmKeyRegistry] Added '{key}'. Open the registry to set displayName, category, and type.");
    }

    private static GUIStyle _deprecatedStyle;
    private static GUIStyle DeprecatedStyle()
    {
        if (_deprecatedStyle != null) return _deprecatedStyle;
        _deprecatedStyle = new GUIStyle(EditorStyles.popup);
        _deprecatedStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        return _deprecatedStyle;
    }
}

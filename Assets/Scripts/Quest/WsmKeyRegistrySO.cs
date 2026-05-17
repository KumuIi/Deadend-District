using UnityEngine;

/// <summary>
/// Central registry of all WSM keys used in the project.
/// Create one instance via Assets › Create › WSM › Key Registry.
///
/// The property drawer for [WsmKey] fields loads this asset at editor time to provide
/// a searchable dropdown. Runtime code never touches this asset.
///
/// Usage tips:
///   - Add a key here before wiring it up anywhere else.
///   - Rename here + use the rename tool to update all serialized references.
///   - Mark keys deprecated instead of deleting them to preserve save compatibility.
/// </summary>
[CreateAssetMenu(menuName = "WSM/Key Registry", fileName = "WsmKeyRegistry")]
public class WsmKeyRegistrySO : ScriptableObject
{
    public WsmKeyEntry[] keys;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (keys == null) return;
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var entry in keys)
        {
            if (entry == null) continue;
            if (string.IsNullOrWhiteSpace(entry.key))
            {
                Debug.LogWarning($"[WsmKeyRegistry] An entry has an empty key string.", this);
                continue;
            }
            if (!seen.Add(entry.key))
                Debug.LogWarning($"[WsmKeyRegistry] Duplicate key '{entry.key}' in registry.", this);
        }
    }
#endif
}

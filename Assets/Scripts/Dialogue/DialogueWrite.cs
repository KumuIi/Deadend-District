using System;
using UnityEngine;

/// <summary>
/// A single typed write into WorldStateManager, authored on a dialogue line or choice. Unlike
/// <see cref="WsmKeyEntry"/> (a registry DESCRIPTOR with no value), this carries the VALUE to write —
/// so it can actually flip a fact. Dialogue uses these to set the WSM keys the QuestManager reacts to;
/// dialogue never talks to QuestManager directly.
/// </summary>
[Serializable]
public class DialogueWrite
{
    [WsmKey] public string key;
    public QuestValueType type = QuestValueType.Bool;

    [Tooltip("Value written when type is Bool. Most dialogue writes are a simple flag = true.")]
    public bool   boolValue = true;
    public int    intValue;
    public float  floatValue;
    public string stringValue;

    /// <summary>Writes this fact to WorldStateManager. No-op if the key is empty or WSM is missing.</summary>
    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(key) || WorldStateManager.Instance == null) return;
        switch (type)
        {
            case QuestValueType.Bool:   WorldStateManager.Instance.SetBool(key, boolValue);     break;
            case QuestValueType.Int:    WorldStateManager.Instance.SetInt(key, intValue);       break;
            case QuestValueType.Float:  WorldStateManager.Instance.SetFloat(key, floatValue);   break;
            case QuestValueType.String: WorldStateManager.Instance.SetString(key, stringValue); break;
        }
    }
}

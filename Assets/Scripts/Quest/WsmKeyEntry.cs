using System;
using UnityEngine;

/// <summary>
/// One named key in the WSM key registry.
/// The property drawer uses displayName + category for search; writes the raw key string into serialized fields.
/// </summary>
[Serializable]
public class WsmKeyEntry
{
    [Tooltip("Human-readable label shown in the dropdown, e.g. 'Opened Laboratory 1 Door'.")]
    public string displayName;

    [Tooltip("The actual key written to WorldStateManager, e.g. 'door.lab1.opened'.")]
    public string key;

    [Tooltip("Expected value type — used for type-mismatch warnings in the inspector.")]
    public QuestValueType type = QuestValueType.Bool;

    [Tooltip("Grouping category shown in the dropdown, e.g. 'Door', 'NPC', 'Quest', 'Zone', 'Combat'.")]
    public string category;

    [Tooltip("Optional notes about when/how this key is written.")]
    public string description;

    [Tooltip("Mark as deprecated to grey it out in the dropdown without deleting it (safe for existing saves).")]
    public bool deprecated;
}

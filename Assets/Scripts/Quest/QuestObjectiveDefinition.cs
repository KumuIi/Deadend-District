using System;
using UnityEngine;

/// <summary>
/// One quest objective — wraps a WSM condition with display and visibility metadata.
///
/// hidden        : HUD won't show this until it completes or revealCondition passes.
/// optional      : Doesn't block success, but appears in the journal as a bonus task.
/// revealCondition: If non-empty, the objective becomes visible when this WSM check passes
///                  (e.g. player picks up a clue that reveals a secret step).
/// </summary>
[Serializable]
public class QuestObjectiveDefinition
{
    [Tooltip("Text shown in the journal/HUD. Leave empty to auto-use the Objective's name or the condition description.")]
    public string description;

    [Tooltip("DRAG-AND-DROP: an Objective asset that marks this complete (Reach Zone, Collect N, Kill N, " +
             "Survive, etc.). When set, you don't need a WSM Condition below — the Objective handles everything.")]
    public ObjectiveSO objective;

    [Tooltip("Manual WSM condition (only used when no Objective asset is assigned above).")]
    public QuestConditionDefinition condition;

    [Tooltip("Does not block quest success. Shows in journal as a bonus task.")]
    public bool optional;

    [Tooltip("Hides this objective in the HUD until it completes or revealCondition passes.")]
    public bool hidden;

    [Tooltip("WSM check that reveals this objective early (e.g. finding a clue). Leave wsmKey empty to disable.")]
    public QuestConditionDefinition revealCondition;
}

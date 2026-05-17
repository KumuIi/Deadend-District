using System;
using UnityEngine;

/// <summary>
/// One possible resolution for a branching quest.
/// QuestManager checks outcomes[] in array order; the first whose condition passes triggers.
///
/// Use this when a quest has multiple valid endings (kill/spare, escape/caught, etc.).
/// For simple quests with one ending, leave outcomes[] empty and use objectives[] instead.
///
/// activateQuests : Calls TryActivate — still checks requiredQuests and activeCondition.
/// cancelQuests   : Sets those quests to Cancelled immediately.
/// failQuests     : Propagates failure to those quests (respects failsWithMe cycle guard).
/// </summary>
[Serializable]
public class QuestOutcomeDefinition
{
    [Tooltip("Editor label for this outcome, e.g. 'Kill', 'Spare', 'Escape'. Not shown to player.")]
    public string label;

    [Tooltip("WSM condition that triggers this outcome. First matching outcome wins.")]
    public QuestConditionDefinition condition;

    [Tooltip("Status this quest transitions to when this outcome fires.")]
    public QuestTerminalStatus terminalStatus = QuestTerminalStatus.Succeeded;

    [Tooltip("Quests to try activating when this outcome fires (prerequisites still checked).")]
    public QuestSO[] activateQuests;

    [Tooltip("Quests to cancel when this outcome fires (faction/mutually exclusive paths).")]
    public QuestSO[] cancelQuests;

    [Tooltip("Quests to fail when this outcome fires.")]
    public QuestSO[] failQuests;
}

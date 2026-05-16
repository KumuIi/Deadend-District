using UnityEngine;

/// <summary>
/// Immutable quest definition. Contains only data — no runtime state.
///
/// Lifecycle:
///   Inactive  → QuestManager watches activeCondition; when true → Active
///   Active    → fail conditions checked first; if any true → Failed
///               then success objectives checked; if all true → Succeeded
///   Succeeded / Failed → terminal, ignored unless status is reset
///
/// Create via Assets > Create > Quest > Quest Definition.
/// </summary>
[CreateAssetMenu(menuName = "Quest/Quest Definition", fileName = "QuestSO_")]
public class QuestSO : ScriptableObject
{
    [Tooltip("Stable unique id — never change after saving. e.g. 'get_blackbox'")]
    public string questId;
    public string title;
    [TextArea] public string description;

    [Tooltip("Quest becomes Active when this condition is true. Leave wsmKey empty to start Active immediately.")]
    public QuestConditionDefinition activeCondition;

    [Tooltip("All must be true for the quest to Succeed.")]
    public QuestConditionDefinition[] objectives;

    [Tooltip("Any one being true immediately Fails the quest.")]
    public QuestConditionDefinition[] failConditions;
}

/// <summary>
/// Per-save mutable quest state. Never stored on QuestSO (shared asset).
/// Owned and serialized by QuestManager.
/// </summary>
public class QuestRuntimeState
{
    public string      questId;
    public QuestStatus status              = QuestStatus.Inactive;
    public bool[]      objectivesComplete;
    public bool[]      objectivesRevealed;
    public bool[]      failConditionsTriggered;
    public int         resolvedOutcomeIndex = -1;
    public float       activeTimeElapsed;

    public QuestRuntimeState(string id, int objectiveCount, int failCount)
    {
        questId                 = id;
        objectivesComplete      = new bool[objectiveCount];
        objectivesRevealed      = new bool[objectiveCount];
        failConditionsTriggered = new bool[failCount];
    }

    public bool AllMandatoryObjectivesMet(QuestObjectiveDefinition[] defs)
    {
        if (defs == null || defs.Length == 0) return false;
        for (int i = 0; i < defs.Length && i < objectivesComplete.Length; i++)
        {
            if (defs[i] == null || defs[i].optional) continue;
            if (!objectivesComplete[i]) return false;
        }
        // At least one non-optional objective must exist
        foreach (var d in defs)
            if (d != null && !d.optional) return true;
        return false;
    }

    public bool AnyFailTriggered()
    {
        foreach (var b in failConditionsTriggered) if (b) return true;
        return false;
    }

    public void ResetForRepeat()
    {
        status               = QuestStatus.Inactive;
        resolvedOutcomeIndex = -1;
        activeTimeElapsed    = 0f;
        for (int i = 0; i < objectivesComplete.Length; i++)
        {
            objectivesComplete[i]  = false;
            objectivesRevealed[i]  = false;
        }
        for (int i = 0; i < failConditionsTriggered.Length; i++)
            failConditionsTriggered[i] = false;
    }
}

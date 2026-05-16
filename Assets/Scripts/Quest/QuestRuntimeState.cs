/// <summary>
/// Per-save mutable quest state. Never stored on QuestSO (shared asset).
/// Owned and serialized by QuestManager.
/// </summary>
public class QuestRuntimeState
{
    public string      questId;
    public QuestStatus status              = QuestStatus.Inactive;
    public bool[]      objectivesComplete;
    public bool[]      failConditionsTriggered;

    public QuestRuntimeState(string id, int objectiveCount, int failCount)
    {
        questId                 = id;
        objectivesComplete      = new bool[objectiveCount];
        failConditionsTriggered = new bool[failCount];
    }

    public bool AllObjectivesMet()
    {
        // A quest with no objectives cannot succeed — requires at least one.
        if (objectivesComplete == null || objectivesComplete.Length == 0) return false;
        foreach (var b in objectivesComplete) if (!b) return false;
        return true;
    }

    public bool AnyFailTriggered()
    {
        foreach (var b in failConditionsTriggered) if (b) return true;
        return false;
    }
}

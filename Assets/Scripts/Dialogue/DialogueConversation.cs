using System;

/// <summary>
/// The dialogue payload — a sequence of <see cref="DialogueLine"/>s, then optional
/// <see cref="DialogueChoice"/>s shown after the last line. Built at runtime by
/// <see cref="QuestGiver"/> and played by <see cref="DialogueUI"/>.
/// </summary>
[Serializable]
public class DialogueConversation
{
    public DialogueLine[]   lines;
    public DialogueChoice[] choices;
}

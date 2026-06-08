using System;

/// <summary>
/// The shared dialogue payload — a sequence of <see cref="DialogueLine"/>s, then optional
/// <see cref="DialogueChoice"/>s shown after the last line. Used BOTH inline on a
/// <see cref="DialogueSpeaker"/> state and inside a <see cref="DialogueSO"/>, so the two storage
/// containers can never structurally diverge.
/// </summary>
[Serializable]
public class DialogueConversation
{
    public DialogueLine[]   lines;
    public DialogueChoice[] choices;
}

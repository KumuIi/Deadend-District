using UnityEngine;

/// <summary>
/// Optional reusable conversation asset. A <see cref="DialogueSpeaker"/> state can reference one of
/// these instead of authoring its conversation inline — use it only when the SAME conversation is
/// shared across NPCs. For the common per-NPC case, inline data on the speaker is less clutter.
/// </summary>
[CreateAssetMenu(menuName = "Dialogue/Conversation", fileName = "Dialogue_")]
public class DialogueSO : ScriptableObject
{
    public DialogueConversation conversation;
}

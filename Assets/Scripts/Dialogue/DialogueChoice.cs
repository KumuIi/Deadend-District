using System;
using UnityEngine;

/// <summary>
/// A selectable option shown after a conversation's lines. Optionally gated (showIf / takeItem),
/// optionally moves an item, writes WSM facts on pick, then either chains to another state on the
/// same <see cref="DialogueSpeaker"/> (<see cref="nextStateIndex"/>) or closes the dialogue (-1).
/// </summary>
[Serializable]
public class DialogueChoice
{
    public string label;

    [Tooltip("Optional WSM gate — the choice is hidden unless this passes. Empty wsmKey = always shown.")]
    public QuestConditionDefinition showIf;

    [Tooltip("Optional: this choice only appears while the player is CARRYING this item (live check). " +
             "On pick the item is removed from the inventory.")]
    public ItemSO takeItem;
    [Tooltip("Optional: an item handed TO the player on pick. If there's no inventory space the pick " +
             "is aborted with no writes (and any takeItem is returned), so nothing is ever lost.")]
    public ItemSO giveItem;

    [Tooltip("WSM facts written when this choice is picked (e.g. the quest 'delivered' flag).")]
    public DialogueWrite[] writesOnPick;

    [Tooltip("State index on this DialogueSpeaker to continue to immediately after picking, or -1 to close.")]
    public int nextStateIndex = -1;
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An NPC (or talkable object) you interact with to start a conversation. Holds a prioritized list
/// of conversation STATES; talking plays the FIRST state whose gate passes — so the same NPC says
/// different things as the world changes (offer → waiting → turn-in → done) with no per-NPC code.
/// It bridges to quests purely through the WSM facts its choices write; QuestManager reacts on its own.
///
/// State gate = (when passes OR is empty) AND (player holds requiredItem OR it's null).
/// A <see cref="ConversationState.chainOnly"/> state is never auto-selected on talk — it's only
/// reachable via a choice's nextStateIndex (used for "hand in the item → immediately roll into the
/// reward / next-offer line" without a second key-press).
/// </summary>
public class DialogueSpeaker : MonoBehaviour, IInteractable
{
    [Serializable]
    public class ConversationState
    {
        [Tooltip("Editor-only label so you can tell states apart (Offer / Waiting / TurnIn / Done…).")]
        public string label;

        [Tooltip("Never auto-selected on talk; only reached via another choice's nextStateIndex.")]
        public bool chainOnly;

        [Tooltip("WSM gate for this state. Empty wsmKey = always passes — use that as the default (last).")]
        public QuestConditionDefinition when;

        [Tooltip("Optional: this state only applies while the player is CARRYING this item (live check). " +
                 "This is what lets a 'hand it over' state win over a 'go find it' state only when held.")]
        public ItemSO requiredItem;

        [Tooltip("Inline conversation for this state. Ignored when Override Asset is set.")]
        public DialogueConversation inline = new DialogueConversation();

        [Tooltip("Optional shared conversation asset; overrides Inline when assigned.")]
        public DialogueSO overrideAsset;

        public DialogueConversation Resolve() =>
            overrideAsset != null ? overrideAsset.conversation : inline;
    }

    [Header("=== Interaction ===")]
    [Tooltip("Crosshair prompt, e.g. 'Talk to The Doctor'.")]
    [SerializeField] private string _prompt = "Talk";

    [Header("=== Conversation States (checked top-to-bottom, first match plays) ===")]
    [SerializeField] private List<ConversationState> _states = new List<ConversationState>();

    public IReadOnlyList<ConversationState> States => _states;
    public int StateCount => _states.Count;

    // ── IInteractable ─────────────────────────────────────────────────────

    public bool   CanInteract(GameObject interactor) => DialogueUI.Instance != null && PickStateIndex() >= 0;
    public string GetPrompt(GameObject interactor)   => _prompt;

    public void Interact(GameObject interactor)
    {
        int idx = PickStateIndex();
        if (idx < 0) return;

        if (DialogueUI.Instance == null)
        {
            Debug.LogWarning($"[DialogueSpeaker] '{name}' tried to talk but there's no DialogueUI in the scene.", this);
            return;
        }
        DialogueUI.Instance.Open(this, idx);
    }

    /// <summary>Resolves the conversation at a state index (bounds-checked). Null if out of range.</summary>
    public DialogueConversation ConversationAt(int index)
    {
        if (index < 0 || index >= _states.Count || _states[index] == null) return null;
        return _states[index].Resolve();
    }

    // ── Selection ─────────────────────────────────────────────────────────

    private int PickStateIndex()
    {
        for (int i = 0; i < _states.Count; i++)
        {
            var s = _states[i];
            if (s == null || s.chainOnly) continue;
            if (!ConditionPassesOrEmpty(s.when)) continue;
            if (!PlayerHasItem(s.requiredItem)) continue;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// A null or empty-key condition is treated as 'always true'. We must NOT call Evaluate() on an
    /// empty condition — it returns false for an empty wsmKey, which would wrongly hide default states.
    /// </summary>
    public static bool ConditionPassesOrEmpty(QuestConditionDefinition c) =>
        c == null || string.IsNullOrEmpty(c.wsmKey) || c.Evaluate();

    /// <summary>True if item is null (no requirement) or the player's grid currently holds one.</summary>
    public static bool PlayerHasItem(ItemSO item)
    {
        if (item == null) return true;
        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return false;
        foreach (var inst in grid.PlacedItems)
            if (inst != null && inst.data == item) return true;
        return false;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: replace the whole state list (used by the quest-dialogue generator).</summary>
    public void EditorSetStates(List<ConversationState> states) => _states = states ?? new List<ConversationState>();

    /// <summary>Editor-only: set the interaction prompt (used by the quest-dialogue generator).</summary>
    public void EditorSetPrompt(string prompt) { if (!string.IsNullOrEmpty(prompt)) _prompt = prompt; }
#endif
}

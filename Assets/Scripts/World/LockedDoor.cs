using UnityEngine;

/// <summary>
/// The LOCK on a door — the small interactable keyhole/padlock the player aims at to unlock.
/// Owns AUTHORIZATION ONLY: it writes the world-state flag "door.{doorId}.unlocked" (the single
/// source of truth, surviving save/load via the WorldStateSaveAdapter). It does NOT move or block
/// the door — that is the separate <see cref="Door"/> leaf, which reads the same flag and owns the
/// swing + physics.
///
/// Why split lock from leaf? So the player aims at the small keyhole to UNLOCK, then aims at the
/// big door to OPEN/CLOSE — two distinct interactions instead of one collider doing everything.
/// They stay decoupled through the shared <c>doorId</c> world-state key.
///
/// Subclasses implement only HOW an unlock is authorised (<see cref="BeginUnlock"/>) and what the
/// locked prompt reads (<see cref="GetLockedPrompt"/>). Once unlocked, the lock stops prompting
/// (<see cref="CanInteract"/> → false) but stays visible in the scene.
///
/// See <see cref="KeyLockedDoor"/> (inventory keys) and the planned keypad lock (async unlock via
/// <see cref="CompleteExternalUnlock"/>).
/// </summary>
public abstract class LockedDoor : MonoBehaviour, IInteractable
{
    [Header("=== Identity ===")]
    [Tooltip("The Door leaf this lock controls. The doorId is read from it — author the id ONCE on " +
             "the Door, link it here, and lock + leaf can never drift apart.")]
    [SerializeField] private Door _door;

    [Header("=== Debug ===")]
    [Tooltip("Logs the unlock flow and, on failure, dumps every inventory panel so you can see " +
             "where the key actually is. Off by default; enable while wiring a door.")]
    [SerializeField] protected bool _debugLogs = false;

    /// <summary>Global signal: ANY lock opened (ShortcutLock subclasses fire it too). Used by ObjectiveService.</summary>
    public static event System.Action<LockedDoor> AnyUnlocked;

    [Header("=== Audio ===")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played when the lock successfully opens.")]
    [SerializeField] private AudioClip _unlockClip;
    [Tooltip("Played when an unlock attempt is denied (no key / wrong code).")]
    [SerializeField] private AudioClip _lockedClip;

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>
    /// The door's id, read from the linked <see cref="Door"/> leaf — the single place it's authored.
    /// Null/empty if no Door is linked; callers fail closed on that.
    /// </summary>
    public string DoorId => _door != null ? _door.DoorId : null;

    /// <summary>The per-door WSM flag. Protected so subclasses can read it if needed.</summary>
    protected string UnlockKey => $"door.{DoorId}.unlocked";

    /// <summary>True when the WSM flag is set. Defaults to locked if WSM isn't up or no Door is linked.</summary>
    protected bool IsUnlocked =>
        WorldStateManager.Instance != null
        && !string.IsNullOrWhiteSpace(DoorId)
        && WorldStateManager.Instance.GetBool(UnlockKey);

    // ── IInteractable ────────────────────────────────────────────────────────

    // An unlocked lock stops prompting — the player then interacts with the Door leaf to open it.
    public bool   CanInteract(GameObject interactor) => !IsUnlocked;
    public string GetPrompt(GameObject interactor)   => GetLockedPrompt(interactor);

    public void Interact(GameObject interactor)
    {
        if (IsUnlocked) return;

        // Fail closed before BeginUnlock: subclasses consume credentials (e.g. a single-use key)
        // inside BeginUnlock, so if we can't persist the unlock we must not let them be spent.
        if (WorldStateManager.Instance == null)
        {
            if (_debugLogs) Debug.LogWarning($"[LockedDoor:{name}] No WorldStateManager — cannot unlock.", this);
            PlayLockedFeedback();
            return;
        }

        UnlockAttempt result = BeginUnlock(interactor);
        if (_debugLogs) Debug.Log($"[LockedDoor:{name}] doorId='{DoorId}' BeginUnlock -> {result}", this);

        switch (result)
        {
            case UnlockAttempt.Succeeded: Unlock();              break;
            case UnlockAttempt.Failed:    PlayLockedFeedback();  break;
            case UnlockAttempt.Pending:                          break; // async; CompleteExternalUnlock() later
        }
    }

    // ── Subclass contract ──────────────────────────────────────────────────

    /// <summary>
    /// Authorise an unlock. Return Succeeded to open now, Failed to deny (base plays locked
    /// feedback), or Pending to defer to an async flow that calls <see cref="CompleteExternalUnlock"/>.
    /// Implementations that consume a credential (e.g. a single-use key) should do so only on Succeeded.
    /// </summary>
    protected abstract UnlockAttempt BeginUnlock(GameObject interactor);

    /// <summary>The crosshair prompt while the door is locked (e.g. "Unlock Door (Key)" or "Locked").</summary>
    protected abstract string GetLockedPrompt(GameObject interactor);

    // ── Unlock flow ────────────────────────────────────────────────────────

    /// <summary>Commit an unlock: write world state and play the unlock sound. Idempotent.</summary>
    protected void Unlock()
    {
        if (IsUnlocked) return;

        // Fail closed: a lock with no Door linked has no id, so it would write "door..unlocked" and
        // collide with every other unconfigured lock. Refuse rather than corrupt world state.
        if (string.IsNullOrWhiteSpace(DoorId))
        {
            Debug.LogWarning($"[LockedDoor] '{name}' unlock ignored — no Door linked, so it has no id.", this);
            return;
        }

        // Fail closed: never authorise an unlock whose state can't be persisted, or it would
        // re-lock on the next scene load / save (and CompleteExternalUnlock could reach here too).
        if (WorldStateManager.Instance == null)
        {
            Debug.LogWarning($"[LockedDoor] '{name}' unlock ignored — no WorldStateManager to persist it.", this);
            return;
        }

        WorldStateManager.Instance.SetBool(UnlockKey, true);
        AnyUnlocked?.Invoke(this);
        PlayClip(_unlockClip);
    }

    /// <summary>
    /// Called by an external async unlock flow (e.g. a keypad UI on correct code) after
    /// <see cref="BeginUnlock"/> returned <see cref="UnlockAttempt.Pending"/>.
    /// </summary>
    public void CompleteExternalUnlock() => Unlock();

    /// <summary>Plays the denied feedback for a rejected unlock attempt. Subclasses may reuse it.</summary>
    protected void PlayLockedFeedback() => PlayClip(_lockedClip);

    private void PlayClip(AudioClip clip)
    {
        if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_door == null)
            Debug.LogWarning($"[LockedDoor] '{name}' has no Door linked — assign the Door leaf it " +
                             $"controls so it inherits the doorId.", this);
    }
#endif
}

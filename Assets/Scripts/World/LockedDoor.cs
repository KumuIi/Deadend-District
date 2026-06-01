using UnityEngine;

/// <summary>
/// Base class for any door whose unlocked-state is world state. The single source of truth
/// is the WorldStateManager bool key "door.{doorId}.unlocked" — NOT a field on this component —
/// so unlocking survives scene reloads and save/load via the existing WorldStateSaveAdapter.
///
/// This base owns everything credential-agnostic: the WSM key, the collider/animator/audio,
/// the open + feedback flow, and (critically) re-reading world state after a deferred save load.
/// Subclasses implement only how an unlock is authorised via <see cref="BeginUnlock"/> and what
/// the locked prompt reads via <see cref="GetLockedPrompt"/>.
///
/// Restore-ordering note: SaveSystem defers a scene-load restore by one frame (after every
/// Start()), and WorldStateManager.LoadState() does not fire per-key change events. So reading
/// state only in Start() would miss a loaded-unlocked door. We therefore also refresh on
/// <see cref="WorldStateManager.OnStateReplaced"/>, and keep <see cref="RefreshState"/> idempotent.
///
/// See also: <see cref="KeyLockedDoor"/> (inventory keys) and the planned KeypadCodeLock
/// (async UI unlock via <see cref="CompleteExternalUnlock"/>).
/// </summary>
public abstract class LockedDoor : MonoBehaviour, IInteractable
{
    [Header("=== Identity ===")]
    [Tooltip("Unique per door. The full world-state flag is \"door.{doorId}.unlocked\". " +
             "Register that flag in WsmKeyRegistrySO before wiring the scene.")]
    [SerializeField] private string _doorId;

    [Header("=== Visuals / Physics ===")]
    [Tooltip("Disabled when the door is unlocked so the player can pass and the interaction " +
             "raycast no longer hits it. Leave null for a trigger/decorative door.")]
    [SerializeField] private Collider _doorCollider;

    [Tooltip("Optional. Driven via a bool param on unlock. Leave null to just toggle the collider.")]
    [SerializeField] private Animator _animator;

    [Tooltip("Animator BOOL set true while unlocked. Holds the open state across restores.")]
    [SerializeField] private string _unlockedParam = "Unlocked";

    [Tooltip("Optional animator TRIGGER fired for the live opening animation (the swing). " +
             "Skipped on restore so a reloaded door snaps open instead of replaying.")]
    [SerializeField] private string _openTrigger = "Open";

    [Tooltip("Optional animator state name snapped to (normalized time 1) on restore, so a " +
             "door loaded already-unlocked shows fully open without playing the animation.")]
    [SerializeField] private string _openStateName = "DoorOpen";

    [Tooltip("Optional animator state name snapped to on restore when the door is LOCKED. Lets a " +
             "door revert to closed when loading an older save where it wasn't yet unlocked. " +
             "Leave empty if your closed state is the animator's default.")]
    [SerializeField] private string _closedStateName = "DoorClosed";

    [Header("=== Audio ===")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played when the door successfully unlocks.")]
    [SerializeField] private AudioClip _unlockClip;
    [Tooltip("Played when an unlock attempt is denied (no key / wrong code).")]
    [SerializeField] private AudioClip _lockedClip;

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>The per-door WSM flag. Protected so subclasses can read it if needed.</summary>
    protected string UnlockKey => $"door.{_doorId}.unlocked";

    /// <summary>The door's unique id, used by subclasses to match credentials (e.g. KeySO.targetDoorId).</summary>
    protected string DoorId => _doorId;

    /// <summary>True when the WSM flag is set. Defaults to locked if WSM isn't up yet.</summary>
    protected bool IsUnlocked =>
        WorldStateManager.Instance != null && WorldStateManager.Instance.GetBool(UnlockKey);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    // Subscribe in OnEnable AND Start: OnEnable may run before the WSM singleton's Awake on the
    // very first scene, so Start is the reliable catch. Subscribe() is idempotent.
    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Start()
    {
        Subscribe();
        RefreshState();
    }

    private void Subscribe()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;
        wsm.OnStateReplaced -= OnWorldStateReplaced; // guard against double-subscription
        wsm.OnStateReplaced += OnWorldStateReplaced;
    }

    private void Unsubscribe()
    {
        if (WorldStateManager.Instance != null)
            WorldStateManager.Instance.OnStateReplaced -= OnWorldStateReplaced;
    }

    private void OnWorldStateReplaced() => RefreshState();

    // ── IInteractable ────────────────────────────────────────────────────────

    // Unlocked doors stop being interactable (and we disable the collider, so the raycast
    // misses them anyway). While locked, the crosshair shows the subclass's locked prompt.
    public bool   CanInteract(GameObject interactor) => !IsUnlocked;
    public string GetPrompt(GameObject interactor)   => GetLockedPrompt(interactor);

    public void Interact(GameObject interactor)
    {
        if (IsUnlocked) return;

        switch (BeginUnlock(interactor))
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

    // ── Unlock / restore flow ────────────────────────────────────────────────

    /// <summary>Commit an unlock: write world state and open with effects. Idempotent.</summary>
    protected void Unlock()
    {
        if (IsUnlocked) return;
        WorldStateManager.Instance?.SetBool(UnlockKey, true);
        OpenDoor(playEffects: true);
    }

    /// <summary>
    /// Called by an external async unlock flow (e.g. a keypad UI on correct code) after
    /// <see cref="BeginUnlock"/> returned <see cref="UnlockAttempt.Pending"/>.
    /// </summary>
    public void CompleteExternalUnlock() => Unlock();

    /// <summary>Plays the denied feedback for a rejected unlock attempt. Subclasses may reuse it.</summary>
    protected void PlayLockedFeedback() => PlayClip(_lockedClip);

    /// <summary>
    /// Applies the OPEN visual/physics state. <paramref name="playEffects"/> true = live unlock
    /// (swing animation + sound); false = silent restore that snaps straight to the open pose.
    /// Safe to call repeatedly.
    /// </summary>
    protected void OpenDoor(bool playEffects)
    {
        if (_doorCollider != null) _doorCollider.enabled = false;

        if (_animator != null)
        {
            if (!string.IsNullOrEmpty(_unlockedParam)) _animator.SetBool(_unlockedParam, true);

            if (playEffects)
            {
                if (!string.IsNullOrEmpty(_openTrigger)) _animator.SetTrigger(_openTrigger);
            }
            else if (!string.IsNullOrEmpty(_openStateName))
            {
                _animator.Play(_openStateName, 0, 1f); // snap to fully-open, no replay
            }
        }

        if (playEffects) PlayClip(_unlockClip);
    }

    /// <summary>Reconciles scene visuals with current world state. Idempotent; no audio, no WSM writes.</summary>
    private void RefreshState()
    {
        if (IsUnlocked)
        {
            OpenDoor(playEffects: false);
            return;
        }

        // Locked: ensure the closed/blocking state (handles re-entry and reverting to an older
        // save where the door was still locked). Snap the visual closed too — symmetric with the
        // open snap above — so we don't depend on a bool-driven transition to play back.
        if (_doorCollider != null) _doorCollider.enabled = true;
        if (_animator != null)
        {
            if (!string.IsNullOrEmpty(_unlockedParam)) _animator.SetBool(_unlockedParam, false);
            if (!string.IsNullOrEmpty(_closedStateName)) _animator.Play(_closedStateName, 0, 0f);
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(_doorId))
        {
            Debug.LogWarning($"[LockedDoor] '{name}' has an empty doorId — its WSM flag would " +
                             $"collide with every other empty-id door.", this);
        }
        else if (_doorId.IndexOf(' ') >= 0)
        {
            Debug.LogWarning($"[LockedDoor] doorId '{_doorId}' on '{name}' contains spaces. " +
                             $"Use a stable token like 'factory_01' so the WSM key stays clean.", this);
        }
    }
#endif
}

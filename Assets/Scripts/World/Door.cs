using UnityEngine;

/// <summary>
/// The openable LEAF of a door. Owns MOTION (via a <see cref="DoorVisual"/>) and PHYSICS (a solid
/// blocking collider), but NOT authorization — it reads the lock state written by a
/// <see cref="LockedDoor"/> that shares the same <c>doorId</c>.
///
/// Two world-state flags drive a door, both persisted so a save restores the exact pose:
///   "door.{doorId}.unlocked" — written by the LOCK. May this leaf be opened at all?
///   "door.{doorId}.open"      — written HERE. Is the leaf currently swung open?
///
/// Interaction model: aim at the keyhole (the lock) to UNLOCK; aim at the leaf to OPEN/CLOSE.
/// While locked, interacting with the leaf just rattles — it won't open.
///
/// Collider note: keep TWO colliders on the leaf — a solid <see cref="_blockingCollider"/> on a
/// physics layer (stops the player while closed) AND a separate trigger/box on the interaction
/// layer (InteractI) so the crosshair raycast can hit the door to open it. The blocking collider
/// must NOT be on the interaction layer, or aiming at it would also try to "open" while you walk.
///
/// Restore ordering: SaveSystem defers its scene-load restore by one frame, so reading state only
/// in Start() can miss a loaded-open door. We also refresh on
/// <see cref="WorldStateManager.OnStateReplaced"/>; <see cref="RefreshPose"/> is idempotent.
/// </summary>
public class Door : MonoBehaviour, IInteractable
{
    [Header("=== Identity ===")]
    [Tooltip("Must match the paired lock's doorId (case-sensitive). Flags are " +
             "\"door.{doorId}.unlocked\" and \"door.{doorId}.open\".")]
    [SerializeField] private string _doorId;

    [Header("=== Motion / Physics ===")]
    [Tooltip("How the leaf presents open vs closed — HingeDoorVisual (DOTween swing) or AnimatorDoorVisual.")]
    [SerializeField] private DoorVisual _visual;
    [Tooltip("Solid collider that blocks the player while CLOSED; disabled while open. " +
             "Keep this on a physics layer, NOT the interaction layer.")]
    [SerializeField] private Collider _blockingCollider;

    [Header("=== Debug ===")]
    [Tooltip("Logs each open/close + locked rattle. Off by default; enable while wiring a door.")]
    [SerializeField] private bool _debugLogs = false;

    [Header("=== Audio ===")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openClip;
    [SerializeField] private AudioClip _closeClip;
    [Tooltip("Rattle played when the player tries to open a still-locked door.")]
    [SerializeField] private AudioClip _lockedRattleClip;

    // ── Derived state ────────────────────────────────────────────────────────

    private string UnlockKey => $"door.{_doorId}.unlocked";
    private string OpenKey    => $"door.{_doorId}.open";

    private bool IsUnlocked =>
        WorldStateManager.Instance != null && WorldStateManager.Instance.GetBool(UnlockKey);
    private bool IsOpen =>
        WorldStateManager.Instance != null && WorldStateManager.Instance.GetBool(OpenKey);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Start()
    {
        Subscribe();
        RefreshPose(animate: false); // snap to whatever the (possibly restored) WSM state says
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

    private void OnWorldStateReplaced() => RefreshPose(animate: false);

    // ── IInteractable ────────────────────────────────────────────────────────

    // Always interactable — the player can always grab the handle; a locked door just rattles.
    public bool CanInteract(GameObject interactor) => true;

    public string GetPrompt(GameObject interactor)
    {
        if (!IsUnlocked) return "Locked";
        return IsOpen ? "Close Door" : "Open Door";
    }

    public void Interact(GameObject interactor)
    {
        if (WorldStateManager.Instance == null)
        {
            if (_debugLogs) Debug.LogWarning($"[Door:{name}] No WorldStateManager — cannot toggle.", this);
            return;
        }

        if (!IsUnlocked)
        {
            if (_debugLogs) Debug.Log($"[Door:{name}] doorId='{_doorId}' still locked — rattle.", this);
            PlayClip(_lockedRattleClip);
            return;
        }

        bool nowOpen = !IsOpen;
        WorldStateManager.Instance.SetBool(OpenKey, nowOpen);
        ApplyPose(nowOpen, animate: true);
        PlayClip(nowOpen ? _openClip : _closeClip);

        if (_debugLogs) Debug.Log($"[Door:{name}] doorId='{_doorId}' -> {(nowOpen ? "OPEN" : "CLOSED")}", this);
    }

    // ── Pose ──────────────────────────────────────────────────────────────────

    /// <summary>Reconciles the leaf with current WSM open-state. Idempotent; no audio, no WSM writes.</summary>
    private void RefreshPose(bool animate) => ApplyPose(IsOpen, animate);

    private void ApplyPose(bool open, bool animate)
    {
        _visual?.Apply(open, animate);
        if (_blockingCollider != null) _blockingCollider.enabled = !open;
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
            Debug.LogWarning($"[Door] '{name}' has an empty doorId — it can't pair with a lock.", this);
            return;
        }

        // Catch the silent-desync footgun: the lock and the leaf hold SEPARATE doorId strings, so a
        // typo on either (e.g. "Lab_2A" vs "Lab2A") means this leaf never sees the unlock and just
        // rattles forever — with no runtime error. Warn at author time if no lock under the same
        // hierarchy root carries a matching id. (No locks at all is fine — the door may be opened by
        // a quest/WSM write elsewhere, so we only warn when locks exist but none matches.)
        var locks = transform.root.GetComponentsInChildren<LockedDoor>(includeInactive: true);
        if (locks.Length == 0) return;

        foreach (var lockComp in locks)
            if (string.Equals(lockComp.DoorId, _doorId, System.StringComparison.Ordinal))
                return; // matched — all good

        Debug.LogWarning(
            $"[Door] '{name}' doorId='{_doorId}' has NO matching lock under '{transform.root.name}'. " +
            $"Lock ids found: [{string.Join(", ", System.Array.ConvertAll(locks, l => l.DoorId))}]. " +
            $"The leaf will never unlock — lock and leaf doorIds must match (case-sensitive).", this);
    }
#endif
}

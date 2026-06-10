using UnityEngine;

/// <summary>
/// The single place that turns game EVENTS into sound. It subscribes to signals the rest of the
/// codebase already broadcasts — run lifecycle, player damage, quest transitions, door unlocks — and
/// fires the matching <see cref="GameSoundId"/> cue from the <see cref="SoundBankSO"/> through the
/// <see cref="SpatialAudioManager"/>.
///
/// Keeping this glue in one component means RunManager / QuestManager / PlayerHealth never grow an
/// AudioSource field or a "play sound" line — they stay about gameplay, and audio stays here. Re-skin
/// every cue from the SoundBank asset; re-route what triggers it from this file.
///
/// Implements <see cref="IRunLifecycleListener"/> for run events (registers like every other
/// listener). Other sources are C# events we subscribe/unsubscribe in OnEnable/OnDisable.
/// Implementors: one instance on the GameSystems GameObject.
/// </summary>
public class AudioDirector : MonoBehaviour, IRunLifecycleListener
{
    [SerializeField] private SoundBankSO _soundBank;

    // The player whose damage/death we're currently listening to. Re-resolved each frame from
    // RunManager because the player rig can change between runs.
    private PlayerHealth _trackedHealth;
    private bool _firstHitThisRun;

    // ── Lifecycle ──────────────────────────────────────────────────

    private void OnEnable()
    {
        RunManager.Instance?.RegisterListener(this);
        QuestManager.OnAnyQuestTransition    += HandleQuestTransition;
        LockedDoor.AnyUnlocked               += HandleDoorUnlocked;
        InventoryUI.OnPlayerInventoryToggled += HandleInventoryToggled;
        TraderSystem.OnItemBought            += HandleItemBought;
        TraderSystem.OnItemSold              += HandleItemSold;
        WeaponManager.OnAnyWeaponEquipChanged += HandleWeaponEquipChanged;
    }

    // Safety net for Awake ordering: if RunManager's singleton wasn't set yet in OnEnable, catch it
    // here. RegisterListener de-dupes, so a double call is harmless.
    private void Start() => RunManager.Instance?.RegisterListener(this);

    private void OnDisable()
    {
        RunManager.Instance?.UnregisterListener(this);
        QuestManager.OnAnyQuestTransition    -= HandleQuestTransition;
        LockedDoor.AnyUnlocked               -= HandleDoorUnlocked;
        InventoryUI.OnPlayerInventoryToggled -= HandleInventoryToggled;
        TraderSystem.OnItemBought            -= HandleItemBought;
        TraderSystem.OnItemSold              -= HandleItemSold;
        WeaponManager.OnAnyWeaponEquipChanged -= HandleWeaponEquipChanged;
        TrackPlayer(null);
    }

    private void Update()
    {
        // The active player can swap (death, load, new run) without an explicit event — keep our
        // damage subscription pointed at whoever RunManager currently owns.
        var current = RunManager.Instance != null ? RunManager.Instance.PlayerHealth : null;
        if (current != _trackedHealth) TrackPlayer(current);
    }

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>Fire a cue by id. No-op if unmapped or silent. Spatial cues play at the player.</summary>
    public void Play(GameSoundId id)
    {
        if (_soundBank == null || SpatialAudioManager.Instance == null) return;
        if (!_soundBank.TryGet(id, out var entry)) return;
        SpatialAudioManager.Instance.PlayCue(entry, PlayerPosition());
    }

    // ── Run lifecycle (IRunLifecycleListener) ──────────────────────

    public void OnRunStarted()
    {
        _firstHitThisRun = false; // arm the one-time "first blood" sting for this run
        Play(GameSoundId.RunEnter);
    }

    public void OnRunExtracted()  => Play(GameSoundId.RunExtract);
    public void OnRunDied()       => Play(GameSoundId.PlayerDeath);
    public void OnReturnedToHub() => Play(GameSoundId.ReturnToHub);

    // ── Player damage ──────────────────────────────────────────────

    private void TrackPlayer(PlayerHealth next)
    {
        if (_trackedHealth == next) return;
        if (_trackedHealth != null) _trackedHealth.OnDamaged -= HandlePlayerDamaged;
        _trackedHealth = next;
        if (_trackedHealth != null) _trackedHealth.OnDamaged += HandlePlayerDamaged;
    }

    private void HandlePlayerDamaged(float amount)
    {
        if (amount <= 0f) return;
        if (!_firstHitThisRun)
        {
            _firstHitThisRun = true;
            Play(GameSoundId.FirstHit); // "you are not alone" — only the first hit of a run
        }
        else
        {
            Play(GameSoundId.PlayerHurt);
        }
    }

    // ── Quests ─────────────────────────────────────────────────────

    private void HandleQuestTransition(QuestSO quest, QuestStatus status)
    {
        switch (status)
        {
            case QuestStatus.Succeeded: Play(GameSoundId.QuestComplete); break;
            case QuestStatus.Failed:
            case QuestStatus.Expired:   Play(GameSoundId.QuestFailed);   break;
        }
    }

    // ── Doors ──────────────────────────────────────────────────────

    private void HandleDoorUnlocked(LockedDoor door) => Play(GameSoundId.DoorUnlock);

    // ── Inventory & trading ────────────────────────────────────────

    private void HandleInventoryToggled(bool open) =>
        Play(open ? GameSoundId.InventoryOpen : GameSoundId.InventoryClose);

    private void HandleItemBought() => Play(GameSoundId.ItemBuy);
    private void HandleItemSold()   => Play(GameSoundId.ItemSell);

    private void HandleWeaponEquipChanged(bool equipped) =>
        Play(equipped ? GameSoundId.ItemEquip : GameSoundId.ItemUnequip);

    // ── Helpers ────────────────────────────────────────────────────

    private Vector3 PlayerPosition()
    {
        if (_trackedHealth != null) return _trackedHealth.transform.position;
        return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    }
}

/// <summary>
/// Stable identifiers for every non-diegetic "stinger" the <see cref="AudioDirector"/> can fire
/// in response to a game event. The clip(s) for each id live on a <see cref="SoundBankSO"/> asset,
/// so designers re-map sounds in the Inspector without touching code.
///
/// Diegetic, per-entity sounds (footsteps, gunshots, the door lock click) are NOT listed here —
/// those stay on their own components (FootstepAudio, GunController, LockedDoor) close to the
/// thing making the noise. This enum is only for global, UI-space cues tied to game state.
/// </summary>
public enum GameSoundId
{
    // ── Run lifecycle ──────────────────────────────────────────────
    RunEnter,        // descending into a run
    RunExtract,      // successful extraction
    ReturnToHub,     // back home, safe
    PlayerDeath,     // the run ended badly

    // ── Player feedback ────────────────────────────────────────────
    PlayerHurt,      // any damage taken
    FirstHit,        // the FIRST damage of a run — a one-time "you are not alone" sting
    LowBattery,      // battery warning threshold crossed

    // ── Combat music transitions (fired by MusicDirector) ──────────
    CombatStart,     // a guard locked onto you
    CombatEnd,       // threat cleared, breathing room returns

    // ── World interactions ─────────────────────────────────────────
    QuestComplete,   // a quest resolved Succeeded
    QuestFailed,     // a quest resolved Failed / Expired
    DoorUnlock,      // a lock opened (global layer on top of the door's own click)

    // ── Inventory & trading ────────────────────────────────────────
    InventoryOpen,   // the player opened their grid
    InventoryClose,  // …and closed it
    ItemBuy,         // bought from a trader (cash-register "cha-ching")
    ItemSell,        // sold to a trader
    ItemEquip,       // equipped a weapon
    ItemUnequip,     // went to empty hands / holstered

    // ── UI ─────────────────────────────────────────────────────────
    MenuOpen,
    MenuClose,
}

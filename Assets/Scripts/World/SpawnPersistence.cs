using UnityEngine;

/// <summary>
/// Persistence + run-counter helper shared by the enemy and loot spawners for the
/// "limited spawn" feature: an entity that keeps spawning every run UNTIL it is
/// CONSUMED (an enemy killed, an item looted), after which it stays gone — either
/// forever, or for a fixed number of runs before it re-arms and spawns again.
///
/// All state lives in <see cref="WorldStateManager"/>, so it saves with the slot and
/// reverts on load — which is exactly the manual-save model the project uses: a kill or
/// a pickup only "sticks" once the player saves at the hub. Reloading an older save
/// brings the enemy / loot back. Keys written here:
///   "world.run.index"        — global run counter, advanced once per run start.
///   "spawn.&lt;id&gt;.consumedRun" — the run index at which &lt;id&gt; was consumed (0 = not consumed).
///
/// We use 0 (not "key missing") as the "available" sentinel because WorldStateManager has
/// no delete; re-arming just writes the value back to 0.
/// </summary>
public static class SpawnPersistence
{
    private const string RunIndexKey = "world.run.index";

    /// <summary>The current run number. 0 before the first run of the session/save has started.</summary>
    public static int CurrentRun =>
        WorldStateManager.Instance != null
            ? WorldStateManager.Instance.GetInt(RunIndexKey, 0)
            : 0;

    /// <summary>
    /// Bumps the global run counter. Called once per run start by <see cref="RunManager"/>,
    /// BEFORE it broadcasts OnRunStarted, so every spawn system sees the new value.
    /// </summary>
    public static void AdvanceRunCounter()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) return;
        wsm.SetInt(RunIndexKey, wsm.GetInt(RunIndexKey, 0) + 1);
    }

    private static string ConsumedKey(string id) => $"spawn.{id}.consumedRun";

    /// <summary>
    /// Whether a limited-spawn entity should appear this run.
    /// <paramref name="runsUntilRespawn"/>: 0 = never respawn once consumed (gone for good);
    /// N = stays gone for N runs after the run it was consumed, then re-arms (this call clears
    /// the consumed flag as a side effect once the window has elapsed).
    /// Non-limited callers should never call this — they always spawn.
    /// Fails OPEN (returns true) if there is no persistence layer or id, so a misconfigured
    /// point still spawns rather than silently vanishing.
    /// </summary>
    public static bool IsAvailableThisRun(string id, int runsUntilRespawn)
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null || string.IsNullOrEmpty(id)) return true;

        int consumedRun = wsm.GetInt(ConsumedKey(id), 0);
        if (consumedRun <= 0) return true;          // never consumed → spawns

        if (runsUntilRespawn <= 0) return false;    // consumed + permanent → gone for good

        if (CurrentRun - consumedRun > runsUntilRespawn)
        {
            wsm.SetInt(ConsumedKey(id), 0);         // suppression window elapsed → re-arm
            return true;
        }
        return false;                                // still inside the suppression window
    }

    /// <summary>Records that the entity was consumed this run (kill / pickup). Persists with the save.</summary>
    public static void MarkConsumed(string id)
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null || string.IsNullOrEmpty(id)) return;
        wsm.SetInt(ConsumedKey(id), Mathf.Max(1, CurrentRun)); // >=1 so it never reads as "available"
    }
}

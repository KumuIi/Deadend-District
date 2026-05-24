/// <summary>
/// Declares what run-event resets a saveable's data.
/// Profile  — persists across all runs (stash, money, augments, quest progress).
/// Run      — resets on death or extraction (inventory, health, battery charge).
/// World    — major persistent world flags (shortcuts, sector discoveries, major quests).
/// Temp     — resets on sector reload (enemy state, loose loot positions).
/// </summary>
public enum RunScopeTag
{
    Profile,
    Run,
    World,
    Temp,
}

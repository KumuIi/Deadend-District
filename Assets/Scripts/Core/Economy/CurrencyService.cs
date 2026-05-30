using System;
using UnityEngine;

/// <summary>
/// The player's wallet. A thin static helper over WorldStateManager — the credits value
/// lives under the key "economy.credits" so it is persisted (and restored) for free by
/// WorldStateSaveAdapter, with no dedicated save adapter to maintain.
///
/// Scope: WorldStateManager saves as one World-scoped blob, so credits persist across death
/// and extraction (money is not lost on a failed run) — the intended behaviour.
///
/// All economy code (TraderSystem, RechargeStation, quest rewards) goes through here so there is
/// one choke point for spend/earn and one event to drive the HUD wallet display.
/// </summary>
public static class CurrencyService
{
    private const string Key = "economy.credits";

    /// <summary>Fired after the balance changes, with the new total. Unsubscribe on disable.</summary>
    public static event Action<int> OnCreditsChanged;

    public static int GetCredits()
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) { Debug.LogWarning("[CurrencyService] No WorldStateManager — returning 0 credits."); return 0; }
        return wsm.GetInt(Key);
    }

    public static bool CanAfford(int amount) => GetCredits() >= amount;

    /// <summary>Adds credits (e.g. selling loot, quest reward). Negative amounts are ignored.</summary>
    public static void Add(int amount)
    {
        if (amount <= 0) return;
        SetCredits(GetCredits() + amount);
    }

    /// <summary>
    /// Spends credits if affordable. Returns false and changes nothing when the player
    /// cannot afford it — callers must check the result before granting the purchase.
    /// </summary>
    public static bool Spend(int amount)
    {
        if (amount <= 0) return true;       // free — nothing to deduct
        if (!CanAfford(amount)) return false;
        SetCredits(GetCredits() - amount);
        return true;
    }

    private static void SetCredits(int value)
    {
        var wsm = WorldStateManager.Instance;
        if (wsm == null) { Debug.LogWarning("[CurrencyService] No WorldStateManager — credit change dropped."); return; }
        wsm.SetInt(Key, value);
        OnCreditsChanged?.Invoke(value);
    }
}

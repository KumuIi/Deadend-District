using System.Collections.Generic;

/// <summary>
/// Runtime state of one physical magazine — the actual rounds currently loaded.
/// Rounds are stored back-to-front: index [Count-1] = next round to fire.
///
/// Flow:
///   Inventory holds a List&lt;MagazineInstance&gt;.
///   On reload: call gun.EjectMagazine() to get the old one back,
///              then gun.StartReload(chosenMag) to slot in the new one.
///   Each shot: GunController calls ConsumeRound() automatically.
///
/// To save to disk: store MagazineSO.name + each AmmunitionSO.name in _rounds order.
/// </summary>
public class MagazineInstance
{
    /// <summary>The SO definition this magazine was created from.</summary>
    public readonly MagazineSO data;

    private readonly List<AmmunitionSO> _rounds;

    public MagazineInstance(MagazineSO definition)
    {
        data = definition;
        _rounds = new List<AmmunitionSO>(definition.capacity);
    }

    // ── State ──────────────────────────────────────────────────────────────

    /// <summary>How many rounds are currently loaded.</summary>
    public int BulletCount => _rounds.Count;
    /// <summary>True when no rounds remain.</summary>
    public bool IsEmpty => _rounds.Count == 0;
    /// <summary>True when loaded to full capacity.</summary>
    public bool IsFull => _rounds.Count >= data.capacity;

    // ── Round access ───────────────────────────────────────────────────────

    /// <summary>Returns the next round to fire without removing it.</summary>
    public AmmunitionSO PeekNextRound() => IsEmpty ? null : _rounds[^1];

    /// <summary>Removes and returns the top round. Call once per shot fired.</summary>
    public AmmunitionSO ConsumeRound()
    {
        if (IsEmpty) return null;
        var round = _rounds[^1];
        _rounds.RemoveAt(_rounds.Count - 1);
        return round;
    }

    /// <summary>
    /// Pushes one round into the magazine.
    /// Returns false if full or caliber does not match.
    /// </summary>
    public bool LoadRound(AmmunitionSO ammo)
    {
        if (IsFull || ammo == null || ammo.caliber != data.caliber) return false;
        _rounds.Add(ammo);
        return true;
    }

    /// <summary>Fills the magazine to capacity with the given ammo type.</summary>
    public void FillWith(AmmunitionSO ammo)
    {
        while (!IsFull && LoadRound(ammo)) { }
    }

    // ── Save / Load ──────────────────────────────────────────────────────────

    /// <summary>
    /// Loaded rounds in fire order — index [Count-1] is the next round out.
    /// Read-only snapshot for serialization (see W3-09 inventory save).
    /// </summary>
    public IReadOnlyList<AmmunitionSO> Rounds => _rounds;

    /// <summary>
    /// Replaces all loaded rounds with <paramref name="rounds"/> (same fire order as
    /// <see cref="Rounds"/>). Used by save/load. Null entries and overflow past capacity
    /// are dropped; caliber is NOT re-validated since the data came from a prior valid state.
    /// </summary>
    public void RestoreRounds(IReadOnlyList<AmmunitionSO> rounds)
    {
        _rounds.Clear();
        if (rounds == null) return;
        for (int i = 0; i < rounds.Count && _rounds.Count < data.capacity; i++)
            if (rounds[i] != null) _rounds.Add(rounds[i]);
    }
}

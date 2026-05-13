using System.Collections.Generic;

/// <summary>
/// Runtime state of a single physical magazine.
/// Rounds are stored back-to-front: index [Count-1] = next round to fire.
///
/// Passed between the inventory system and GunController.
/// To save: record MagazineSO.name + the list of AmmunitionSO.names in order.
/// </summary>
public class MagazineInstance
{
    public readonly MagazineSO data;

    private readonly List<AmmunitionSO> _rounds;

    public MagazineInstance(MagazineSO definition)
    {
        data    = definition;
        _rounds = new List<AmmunitionSO>(definition.capacity);
    }

    // ── State ─────────────────────────────────────────────────────────────

    public int  BulletCount => _rounds.Count;
    public bool IsEmpty     => _rounds.Count == 0;
    public bool IsFull      => _rounds.Count >= data.capacity;

    // ── Round access ──────────────────────────────────────────────────────

    /// <summary>Returns the next round to fire without removing it.</summary>
    public AmmunitionSO PeekNextRound() => IsEmpty ? null : _rounds[^1];

    /// <summary>Removes and returns the top round (call once per shot fired).</summary>
    public AmmunitionSO ConsumeRound()
    {
        if (IsEmpty) return null;
        var round = _rounds[^1];
        _rounds.RemoveAt(_rounds.Count - 1);
        return round;
    }

    /// <summary>
    /// Pushes one round into the magazine.
    /// Returns false if the magazine is full or the caliber doesn't match.
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
}

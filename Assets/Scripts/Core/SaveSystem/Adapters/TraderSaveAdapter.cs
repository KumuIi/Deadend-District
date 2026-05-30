using System;
using UnityEngine;

/// <summary>
/// Persists one trader's runtime stock counts and restock countdown.
/// Profile-scoped — stock survives death and extraction, only resets when the restock
/// interval elapses.
///
/// Attach to the same GameObject as TraderSystem. Set _traderId once in the Inspector
/// to a stable, human-readable string (e.g. "hub_general") — do NOT use the SO name or
/// the scene object name, which can change and break saves.
/// </summary>
public class TraderSaveAdapter : MonoBehaviour, ISaveable
{
    [SerializeField] private TraderSystem _traderSystem;
    [Tooltip("Stable per-trader key baked into the SaveId. Set once; never rename after shipping saves.")]
    [SerializeField] private string _traderId = "hub_general";

    public string      SaveId    => $"trader.{_traderId}.stock";
    public string      SaveType  => "TraderStock";
    public RunScopeTag SaveScope => RunScopeTag.Profile;

    private void Start()     => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        if (_traderSystem == null) throw new InvalidOperationException("TraderSystem not assigned on TraderSaveAdapter.");
        return _traderSystem.CaptureStockState();
    }

    public void RestoreSaveData(object data)
    {
        if (_traderSystem == null) return;
        var dto = JsonUtility.FromJson<TraderStockSaveData>((string)data);
        _traderSystem.RestoreStockState(dto);
    }
}

[Serializable]
public class TraderStockSaveData
{
    public int[] stockRemaining;
    public int   runsUntilRestock;
}

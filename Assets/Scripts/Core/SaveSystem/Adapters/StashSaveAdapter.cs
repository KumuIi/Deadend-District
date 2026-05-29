using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persists the stash grid contents. RunScopeTag.Profile — the stash survives death,
/// extraction, and sector reloads; it only changes when the player moves items in the hub.
///
/// Attach to the same GameObject as the stash InventoryUI. Mirrors InventorySaveAdapter, but
/// is profile-scoped and never auto-clears on death (it does NOT implement IRunLifecycleListener).
///
/// Restored automatically on hub load via SceneTransitionManager → RestoreAfterSceneLoad(Profile).
/// </summary>
public class StashSaveAdapter : MonoBehaviour, ISaveable
{
    [SerializeField] private InventoryUI _stashUI;

    public string      SaveId    => "player.stash";
    public string      SaveType  => "Stash";
    public RunScopeTag SaveScope => RunScopeTag.Profile;

    private void Start()     => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        if (_stashUI == null) throw new InvalidOperationException("Stash InventoryUI not assigned.");
        return new StashSaveData { entries = _stashUI.Grid.GetSaveData() };
    }

    public void RestoreSaveData(object data)
    {
        if (_stashUI == null) return;
        var dto = JsonUtility.FromJson<StashSaveData>((string)data);
        if (dto?.entries == null) return;

        // ClearAll() + LoadFromSaveData() atomically replaces grid contents and rebuilds views.
        var resolver = new ResourcesItemSOResolver();
        _stashUI.LoadFromSaveData(dto.entries, resolver);
    }
}

[Serializable]
public class StashSaveData
{
    public List<InventoryGrid.GridSaveEntry> entries;
}

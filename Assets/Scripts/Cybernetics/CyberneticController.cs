using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages equipped cybernetics at runtime. Owns CyberneticRuntime instances
/// so mutable state never lives on shared SO assets.
/// </summary>
public class CyberneticController : MonoBehaviour, ISaveable
{
    [Header("References")]
    public PlayerMotor  Motor;
    public PlayerHealth Health;

    [SerializeField] private CyberneticSOCatalog _catalog;

    public string      SaveId    => "player.cybernetics";
    public string      SaveType  => "CyberneticController";
    public RunScopeTag SaveScope => RunScopeTag.Profile;

    private void Start()     => SaveSystem.Instance?.Register(this);
    private void OnEnable()  => SaveSystem.Instance?.Register(this);
    private void OnDisable() => SaveSystem.Instance?.Unregister(this);
    private void OnDestroy() => UnequipAll();

    private readonly List<CyberneticRuntime> _runtimes    = new List<CyberneticRuntime>();
    private readonly List<string>            _equippedIds = new List<string>();

    public void Equip(CyberneticSO so)
    {
        if (so == null) return;
        if (string.IsNullOrEmpty(so.cyberneticId))
        { Debug.LogWarning($"[CyberneticController] SO '{so.name}' has no cyberneticId."); return; }
        if (_equippedIds.Contains(so.cyberneticId))
        { Debug.LogWarning($"[CyberneticController] '{so.cyberneticId}' already equipped."); return; }

        var runtime = so.CreateRuntime(this);
        if (runtime == null)
        { Debug.LogWarning($"[CyberneticController] '{so.cyberneticId}' CreateRuntime() returned null."); return; }

        runtime.Equip();
        _runtimes.Add(runtime);
        _equippedIds.Add(so.cyberneticId);
    }

    public void Unequip(CyberneticSO so)
    {
        if (so == null) return;
        int idx = _equippedIds.IndexOf(so.cyberneticId);
        if (idx < 0) return;
        _runtimes[idx].Unequip();
        _runtimes.RemoveAt(idx);
        _equippedIds.RemoveAt(idx);
    }

    public void UseAbility(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < _runtimes.Count)
            _runtimes[slotIndex].UseAbility();
    }

    public object CaptureSaveData() =>
        new CyberneticsDTO { equippedIds = _equippedIds.ToArray() };

    public void RestoreSaveData(object data)
    {
        if (_catalog == null)
        { Debug.LogWarning("[CyberneticController] No catalog — cannot restore cybernetics."); return; }
        var dto = JsonUtility.FromJson<CyberneticsDTO>((string)data);
        if (dto?.equippedIds == null) return;
        UnequipAll();
        foreach (var id in dto.equippedIds)
        {
            var so = _catalog.Find(id);
            if (so != null) Equip(so);
            else Debug.LogWarning($"[CyberneticController] Unknown id '{id}' in save.");
        }
    }

    private void UnequipAll()
    {
        foreach (var r in _runtimes) r.Unequip();
        _runtimes.Clear();
        _equippedIds.Clear();
    }

    [Serializable] private class CyberneticsDTO { public string[] equippedIds; }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asset catalog mapping cyberneticId strings to CyberneticSO assets.
/// Used by CyberneticController to resolve saved ids on load.
/// Create via Assets > Create > Cybernetics > Catalog.
/// </summary>
[CreateAssetMenu(menuName = "Cybernetics/Catalog", fileName = "CyberneticSOCatalog")]
public class CyberneticSOCatalog : ScriptableObject
{
    [SerializeField] private List<CyberneticSO> _entries = new List<CyberneticSO>();

    private Dictionary<string, CyberneticSO> _lookup;

    private void OnEnable() => BuildLookup();

    public CyberneticSO Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var so) ? so : null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, CyberneticSO>();
        foreach (var so in _entries)
        {
            if (so == null || string.IsNullOrEmpty(so.cyberneticId)) continue;
            if (_lookup.ContainsKey(so.cyberneticId))
                Debug.LogWarning($"[CyberneticSOCatalog] Duplicate id '{so.cyberneticId}' — later entry ignored.", this);
            else
                _lookup[so.cyberneticId] = so;
        }
    }
}

using System;
using UnityEngine;

/// <summary>
/// Bridges PlayerHealth into the ISaveable system.
/// Attach to the same GameObject as PlayerHealth.
/// </summary>
public class PlayerHealthSaveAdapter : MonoBehaviour, ISaveable
{
    private PlayerHealth _health;

    public string SaveId   => "player.health";
    public string SaveType => "PlayerHealth";

    private void Awake() => _health = GetComponent<PlayerHealth>();

    private void Start()
    {
        // Register in Start, not OnEnable — guarantees SaveSystem.Instance
        // exists (initialized in Awake) before adapters attempt to register.
        SaveSystem.Instance?.Register(this);
    }

    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        if (_health == null) throw new InvalidOperationException("PlayerHealth not found.");
        return new PlayerHealthSaveData
        {
            currentHealth = _health.CurrentHealth,
            currentEnergy = _health.CurrentEnergy,
            maxHealth     = _health.maxHealth,
            maxEnergy     = _health.maxEnergy,
        };
    }

    public void RestoreSaveData(object data)
    {
        if (_health == null) return;
        var dto = JsonUtility.FromJson<PlayerHealthSaveData>((string)data);
        if (dto == null) return;

        _health.maxHealth = dto.maxHealth;
        _health.maxEnergy = dto.maxEnergy;
        _health.LoadFromSave(dto.currentHealth, dto.currentEnergy);
    }
}

[Serializable]
public class PlayerHealthSaveData
{
    public float currentHealth;
    public float currentEnergy;
    public float maxHealth;
    public float maxEnergy;
}

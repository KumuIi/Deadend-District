using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks player health and energy.
/// Add to the player root GameObject.
/// Other systems call TakeDamage / Heal / UseEnergy / RestoreEnergy.
/// Energy regenerates automatically at <see cref="energyRegenRate"/> per second.
/// </summary>
public sealed class PlayerHealth : MonoBehaviour
{
    [Header("=== Health ===")]
    public float maxHealth = 100f;
    [SerializeField] private float _currentHealth = 100f;

    [Header("=== Energy ===")]
    public float maxEnergy    = 100f;
    [SerializeField] private float _currentEnergy = 100f;
    [Tooltip("Energy points restored per second when not actively depleted.")]
    public float energyRegenRate = 5f;

    // ── Read-only state ────────────────────────────────────────────────────

    public float CurrentHealth => _currentHealth;
    public float CurrentEnergy => _currentEnergy;
    public bool  IsDead        => _currentHealth <= 0f;

    // ── Events ─────────────────────────────────────────────────────────────

    public UnityEvent OnDeath;
    public UnityEvent OnHealthChanged;
    public UnityEvent OnEnergyChanged;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _currentHealth = maxHealth;
        _currentEnergy = maxEnergy;
    }

    private void Update()
    {
        if (_currentEnergy < maxEnergy)
        {
            _currentEnergy = Mathf.Min(maxEnergy, _currentEnergy + energyRegenRate * Time.deltaTime);
            OnEnergyChanged?.Invoke();
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        OnHealthChanged?.Invoke();
        if (IsDead) OnDeath?.Invoke();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        OnHealthChanged?.Invoke();
    }

    public void UseEnergy(float amount)
    {
        if (amount <= 0f) return;
        _currentEnergy = Mathf.Max(0f, _currentEnergy - amount);
        OnEnergyChanged?.Invoke();
    }

    public void RestoreEnergy(float amount)
    {
        if (amount <= 0f) return;
        _currentEnergy = Mathf.Min(maxEnergy, _currentEnergy + amount);
        OnEnergyChanged?.Invoke();
    }
}

using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks player health and energy.
/// Add to the player root GameObject.
/// Other systems call TakeDamage / Heal / UseEnergy / RestoreEnergy.
/// Energy regenerates automatically at <see cref="energyRegenRate"/> per second.
/// </summary>
public sealed class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("=== Health ===")]
    public float maxHealth = 100f;
    [SerializeField] private float _currentHealth = 100f;

    [Header("=== Energy ===")]
    public float maxEnergy    = 100f;
    [SerializeField] private float _currentEnergy = 100f;
    [Tooltip("Energy points restored per second when not actively depleted.")]
    public float energyRegenRate = 5f;

    // ── Stat modifiers ─────────────────────────────────────────────────────

    /// <summary>Push stamina-affecting modifiers here (e.g. encumbrance.stamina, augment.*).</summary>
    public StatModifierStack StatModifiers { get; } = new StatModifierStack();

    // ── Read-only state ────────────────────────────────────────────────────

    public float CurrentHealth => _currentHealth;
    public float CurrentEnergy => _currentEnergy;
    public bool  IsDead        => _currentHealth <= 0f;
    public bool  IsAlive       => _currentHealth > 0f;

    // ── Events ─────────────────────────────────────────────────────────────

    public UnityEvent OnDeath;
    public UnityEvent OnHealthChanged;
    public UnityEvent OnEnergyChanged;

    /// <summary>Fired with the actual damage dealt (clamped, never negative) after health is reduced.</summary>
    public event System.Action<float> OnDamaged;

    // ── Lifecycle ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (energyRegenRate < 0f) energyRegenRate = 0f;
        if (energyRegenRate == 0f)
            Debug.LogWarning("[PlayerHealth] energyRegenRate is 0 — stamina will never regenerate.", this);
    }
#endif

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

    public float ApplyDamage(DamageContext ctx)
    {
        float before = _currentHealth;
        TakeDamage(ctx.BaseDamage);
        return Mathf.Max(0f, before - _currentHealth);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        float before = _currentHealth;
        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        float dealt = before - _currentHealth;
        OnHealthChanged?.Invoke();
        OnDamaged?.Invoke(dealt);
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
        float drainMult = Mathf.Max(0f, StatModifiers.Net(StatType.StaminaDrain));
        _currentEnergy = Mathf.Max(0f, _currentEnergy - amount * drainMult);
        OnEnergyChanged?.Invoke();
    }

    public void RestoreEnergy(float amount)
    {
        if (amount <= 0f) return;
        _currentEnergy = Mathf.Min(maxEnergy, _currentEnergy + amount);
        OnEnergyChanged?.Invoke();
    }

    /// <summary>
    /// Sets health and energy to exact absolute values. Only for save/load.
    /// Fires change events so HUD updates correctly.
    /// </summary>
    public void LoadFromSave(float health, float energy)
    {
        _currentHealth = Mathf.Clamp(health, 0f, maxHealth);
        _currentEnergy = Mathf.Clamp(energy, 0f, maxEnergy);
        OnHealthChanged?.Invoke();
        OnEnergyChanged?.Invoke();
    }
}

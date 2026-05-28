using System;
using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable, IFactionProvider
{
    [SerializeField] private float  _maxHealth = 100f;
    [SerializeField] private TeamId _teamId    = TeamId.Guard;

    public float CurrentHealth { get; private set; }
    public bool  IsAlive       => CurrentHealth > 0f;

    public TeamId TeamId => _teamId;

    public bool IsHostileTo(TeamId other)
        => other == TeamId.Player || other == TeamId.Monster;

    public event Action              OnDeath;
    public event Action<DamageContext> OnDamaged;

    private void Awake() => CurrentHealth = _maxHealth;

    public float ApplyDamage(DamageContext ctx)
    {
        if (!IsAlive) return 0f;
        float dealt = Mathf.Min(ctx.BaseDamage, CurrentHealth);
        CurrentHealth -= dealt;
        OnDamaged?.Invoke(ctx);
        if (CurrentHealth <= 0f)
        {
            enabled = false;
            OnDeath?.Invoke();
        }
        return dealt;
    }
}

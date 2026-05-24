/// <summary>
/// Anything that can receive damage.
/// Implementors: PlayerHealth, BaseEnemyAI subclasses, destructible props.
/// ApplyDamage must return actual damage dealt (after armour/resistance) so callers
/// can trigger stagger or kill sounds correctly.
/// Player death must route through RunManager.TriggerDeath — never SceneManager.LoadScene.
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }

    /// <summary>
    /// Apply damage described by ctx. Returns actual damage dealt after all modifiers.
    /// </summary>
    float ApplyDamage(DamageContext ctx);
}

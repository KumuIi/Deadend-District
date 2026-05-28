using UnityEngine;

/// <summary>
/// Forwarding IDamageable placed on the detached armature root by EnemyRagdoll.Awake().
///
/// When EnemyRagdoll detaches the skeleton from the enemy root (SetParent(null)), all
/// bone colliders lose their parent chain to EnemyHealth. GetComponentInParent<IDamageable>
/// on a hit bone now terminates here instead of returning null.
/// </summary>
public sealed class EnemyDamageProxy : MonoBehaviour, IDamageable
{
    private EnemyHealth _target;

    public bool IsAlive => _target != null && _target.IsAlive;

    public void Initialize(EnemyHealth target)
    {
        if (target == null)
            Debug.LogError($"[EnemyDamageProxy] {name}: EnemyHealth target is null — hits will be dropped.");
        _target = target;
    }

    public float ApplyDamage(DamageContext ctx) =>
        _target != null ? _target.ApplyDamage(ctx) : 0f;
}

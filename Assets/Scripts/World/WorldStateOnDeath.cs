using UnityEngine;

/// <summary>
/// Writes a WorldStateManager key when a watched PlayerHealth fires OnDeath.
/// Scoped to PlayerHealth for now — enemies will get their own health system later.
///
/// Example: set "npc.sergeant.dead" = true when a specific enemy dies.
/// Drop on the same GameObject as PlayerHealth, or any GO with a reference.
/// </summary>
[RequireComponent(typeof(WorldStateWriter))]
public class WorldStateOnDeath : MonoBehaviour
{
    [Tooltip("Leave null to use PlayerHealth on this same GameObject.")]
    [SerializeField] private PlayerHealth _target;

    private WorldStateWriter _writer;

    private void Awake()
    {
        _writer = GetComponent<WorldStateWriter>();
        if (_target == null) _target = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        if (_target != null) _target.OnDeath.AddListener(HandleDeath);
    }

    private void OnDisable()
    {
        if (_target != null) _target.OnDeath.RemoveListener(HandleDeath);
    }

    private void HandleDeath() => _writer.Write();
}

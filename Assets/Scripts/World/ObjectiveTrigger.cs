using UnityEngine;

/// <summary>
/// Marks a <see cref="ObjectiveSO"/> of type <c>ReachZone</c> done when the player walks into this
/// volume. The ONLY thing you place in the world for a "go there" objective: drop this on a
/// GameObject with a trigger collider and drag the objective in.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ObjectiveTrigger : MonoBehaviour
{
    [Tooltip("The Reach-Zone objective satisfied by entering this volume.")]
    [SerializeField] private ObjectiveSO _objective;

    [Tooltip("Only fire for colliders with this tag. Leave empty to fire for anything.")]
    [SerializeField] private string _playerTag = "Player";

    private void Reset()
    {
        // Convenience: make the collider a trigger when the component is first added.
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_objective == null) return;
        if (!string.IsNullOrEmpty(_playerTag) && !other.CompareTag(_playerTag)) return;
        ObjectiveService.Instance?.MarkZoneReached(_objective);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_objective != null && _objective.type != ObjectiveType.ReachZone)
            Debug.LogWarning($"[ObjectiveTrigger] '{name}' references a '{_objective.type}' objective — " +
                             $"triggers only drive Reach Zone objectives.", this);
    }
#endif
}

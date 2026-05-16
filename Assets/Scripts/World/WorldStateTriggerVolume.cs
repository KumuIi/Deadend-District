using UnityEngine;

/// <summary>
/// Writes a WorldStateManager key when a matching GameObject enters this trigger volume.
/// Compose with WorldStateWriter for the actual write.
/// Add a Collider (set Is Trigger = true) to this GameObject.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(WorldStateWriter))]
public class WorldStateTriggerVolume : MonoBehaviour
{
    [Tooltip("Only fire for GameObjects with this tag. Leave empty to match anything.")]
    [SerializeField] private string _requiredTag = "Player";

    [Tooltip("Fire only once even if the trigger is entered multiple times.")]
    [SerializeField] private bool _onlyOnce = true;

    private WorldStateWriter _writer;
    private bool             _fired;

    private void Awake()
    {
        _writer = GetComponent<WorldStateWriter>();

        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogWarning("[WorldStateTriggerVolume] Collider is not a trigger — set Is Trigger = true.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_onlyOnce && _fired) return;
        if (!string.IsNullOrEmpty(_requiredTag) && !other.CompareTag(_requiredTag)) return;

        _fired = true;
        _writer.Write();
    }
}

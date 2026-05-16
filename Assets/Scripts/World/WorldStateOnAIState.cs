using UnityEngine;

/// <summary>
/// Writes a WorldStateManager key when a watched AIPerception reaches a specific state.
/// Drop this on any GameObject — it just needs a reference to the AIPerception to watch.
///
/// Example: set "quest.infiltrate.detected" = true when any guard enters Alert or Combat.
/// </summary>
[RequireComponent(typeof(WorldStateWriter))]
public class WorldStateOnAIState : MonoBehaviour
{
    [SerializeField] private AIPerception            _target;
    [SerializeField] private AIPerception.AIState    _triggerState = AIPerception.AIState.Alert;
    [Tooltip("Also fire for states higher than triggerState (Alert also fires on Combat).")]
    [SerializeField] private bool                    _fireOnHigherStates = true;
    [SerializeField] private bool                    _onlyOnce = true;

    private WorldStateWriter _writer;
    private bool             _fired;

    private void Awake() => _writer = GetComponent<WorldStateWriter>();

    private void OnEnable()
    {
        if (_target != null) _target.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (_target != null) _target.OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(AIPerception.AIState prev, AIPerception.AIState next)
    {
        if (_onlyOnce && _fired) return;

        bool matches = next == _triggerState ||
                       (_fireOnHigherStates && (int)next > (int)_triggerState);
        if (!matches) return;

        _fired = true;
        _writer.Write();
    }
}

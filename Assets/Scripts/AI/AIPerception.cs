using System;
using UnityEngine;

/// <summary>
/// Perception and state machine for a basic AI agent.
/// Listens to StimulusSystem for sounds; raycasts for sight.
/// State machine: Idle → Investigate → Alert → Combat
///
/// OnStateChanged fires on every real transition so world scripts can react
/// without polling. Phase 3 can replace patrol/combat behavior by reading
/// State and LastKnownPosition without touching perception logic.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AIPerception : MonoBehaviour, IStimulusListener
{
    public enum AIState { Idle, Investigate, Alert, Combat }

    [Header("Sight")]
    [SerializeField] private Transform _playerTarget;
    [SerializeField] private float     _sightAngle    = 60f;
    [SerializeField] private float     _sightDistance = 15f;
    [SerializeField] private LayerMask _occlusionMask;

    [Header("Timers")]
    [SerializeField] private float _investigateDuration = 5f;
    [SerializeField] private float _alertDuration       = 8f;
    [SerializeField] private float _sightCheckInterval  = 0.2f;

    // ── Public state ─────────────────────────────────────────────────────────

    public AIState State             { get; private set; } = AIState.Idle;
    public Vector3 LastKnownPosition { get; private set; }

    /// <summary>Fired when state changes. Args: (previousState, newState).</summary>
    public event Action<AIState, AIState> OnStateChanged;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _stateTimer;
    private float _sightTimer;

    private static readonly StimulusType[] _listenTypes = { StimulusType.Sound };

    // ── IStimulusListener ────────────────────────────────────────────────────

    public StimulusType[] ListensTo => _listenTypes;

    private void OnEnable()
    {
        if (StimulusSystem.Instance != null)
            StimulusSystem.Instance.Register(this);
    }

    private void OnDisable()
    {
        if (StimulusSystem.Instance != null)
            StimulusSystem.Instance.Unregister(this);
    }

    public void OnStimulus(in Stimulus s)
    {
        if (s.Instigator == gameObject) return;
        LastKnownPosition = s.Position;
        if (State == AIState.Idle) TransitionTo(AIState.Investigate);
    }

    // ── Unity ────────────────────────────────────────────────────────────────

    private void Update()
    {
        _sightTimer += Time.deltaTime;
        if (_sightTimer >= _sightCheckInterval)
        {
            _sightTimer = 0f;
            EvaluateState();
        }
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private void EvaluateState()
    {
        bool canSee = CheckSight();

        switch (State)
        {
            case AIState.Idle:
                if (canSee) TransitionTo(AIState.Alert);
                break;

            case AIState.Investigate:
                if (canSee) { TransitionTo(AIState.Alert); break; }
                _stateTimer -= _sightCheckInterval;
                if (_stateTimer <= 0f) TransitionTo(AIState.Idle);
                break;

            case AIState.Alert:
                if (canSee)
                {
                    LastKnownPosition = _playerTarget.position;
                    _stateTimer -= _sightCheckInterval;
                    if (_stateTimer <= 0f) TransitionTo(AIState.Combat);
                }
                else TransitionTo(AIState.Investigate);
                break;

            case AIState.Combat:
                if (!canSee) TransitionTo(AIState.Investigate);
                else         LastKnownPosition = _playerTarget.position;
                break;
        }
    }

    private void TransitionTo(AIState next)
    {
        if (State == next) return;
        var prev = State;
        State = next;
        _stateTimer = next switch
        {
            AIState.Investigate => _investigateDuration,
            AIState.Alert       => _alertDuration,
            _                   => 0f,
        };
        OnStateChanged?.Invoke(prev, next);
        Debug.Log($"[AIPerception] {name} → {next}");
    }

    private bool CheckSight()
    {
        if (_playerTarget == null) return false;
        Vector3 toPlayer = _playerTarget.position - transform.position;
        if (toPlayer.sqrMagnitude > _sightDistance * _sightDistance) return false;
        if (Vector3.Angle(transform.forward, toPlayer) > _sightAngle * 0.5f) return false;
        return !Physics.Raycast(transform.position, toPlayer.normalized,
                                toPlayer.magnitude, _occlusionMask);
    }
}

using System;
using UnityEngine;

/// <summary>
/// Perception component for enemy guards.
/// Cold sight uses FOV only before first detection.
/// Hot sight uses distance + LOS only, skipping FOV, so the guard never loses the
/// player just because NavMesh rotation diverges from the player's direction.
/// </summary>
public class EnemyPerception : MonoBehaviour, IStimulusListener
{
    public enum PerceptionEvent { TargetSpotted, SoundHeard, TargetLost }

    [Header("Cold Sight (before first detection)")]
    [SerializeField] private float _coldSightAngle    = 60f;
    [SerializeField] private float _coldSightDistance = 15f;
    [Tooltip("Detection score (PlayerVisibility * distance falloff) needed to spot an " +
             "unaware player. Higher = the player can stay hidden in shadow / while still / crouched.\n" +
             "NOTE: PlayerVisibility is multiplicative, so a still player caps near the movement " +
             "'still' factor (~0.2). Keep this below that so a lit, close, motionless player is still " +
             "spotted; raise it to make stealth more forgiving.")]
    [SerializeField] private float _sightThreshold    = 0.15f;

    [Header("Hot Sight (after first detection — no FOV check)")]
    [SerializeField] private float _hotSightDistance = 25f;

    [Header("Timers")]
    [SerializeField] private float _sightCheckInterval = 0.15f;
    [SerializeField] private float _lostSightTimeout   = 5f;

    [Header("Occlusion")]
    [SerializeField] private LayerMask _occlusionMask;

    // ── Public state ─────────────────────────────────────────────────────────

    public Transform Target            { get; private set; }
    public bool      CanSeeTarget      { get; private set; }
    public Vector3   LastKnownPosition { get; private set; }
    public bool      IsInHotMode       { get; private set; }
    public float     LostSightTimer    { get; private set; }
    public float     LostSightTimeout  => _lostSightTimeout;

    /// <summary>Normalised loudness [0..1] of the most recently heard sound. The brain
    /// grades its reaction off this (faint = grow suspicious, loud = investigate now).</summary>
    public float     LastHeardIntensity { get; private set; }

    public event Action<PerceptionEvent, Vector3> OnPerceptionEvent;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _sightTimer;
    private bool  _lostEventFired;

    private static readonly StimulusType[] _listenTypes = { StimulusType.Sound, StimulusType.Damage };
    public StimulusType[] ListensTo => _listenTypes;

    // ── Init ─────────────────────────────────────────────────────────────────

    /// <summary>Call once from EnemyBrain.Awake. Target is never serialized.</summary>
    public void Initialize(Transform target) => Target = target;

    // ── Unity ────────────────────────────────────────────────────────────────

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

    private void Update()
    {
        _sightTimer += Time.deltaTime;
        if (_sightTimer < _sightCheckInterval) return;
        _sightTimer = 0f;
        EvaluateSight();
    }

    // ── Stimulus ─────────────────────────────────────────────────────────────

    public void OnStimulus(in Stimulus s)
    {
        if (Target == null || s.Instigator == gameObject) return;
        bool fromTarget = Target != null && s.Instigator == Target.gameObject;
        if (fromTarget || s.Type == StimulusType.Damage)
        {
            LastKnownPosition  = s.Position;
            LastHeardIntensity = s.Type == StimulusType.Damage ? 1f : s.Intensity;
            OnPerceptionEvent?.Invoke(PerceptionEvent.SoundHeard, s.Position);
        }
    }

    // ── Sight ─────────────────────────────────────────────────────────────────

    private void EvaluateSight()
    {
        if (Target == null) return;

        bool canSee = IsInHotMode ? CheckHotSight() : CheckColdSight();

        if (canSee)
        {
            LastKnownPosition = Target.position;
            LostSightTimer    = 0f;
            _lostEventFired   = false;

            if (!CanSeeTarget)
            {
                CanSeeTarget = true;
                if (!IsInHotMode)
                {
                    IsInHotMode = true;
                    Debug.Log($"[EnemyPerception] {name}: Target spotted — hot mode active.");
                    OnPerceptionEvent?.Invoke(PerceptionEvent.TargetSpotted, Target.position);
                }
            }
        }
        else
        {
            CanSeeTarget = false;
            if (IsInHotMode)
            {
                LostSightTimer += _sightCheckInterval;
                if (!_lostEventFired && LostSightTimer >= _lostSightTimeout)
                {
                    _lostEventFired = true;
                    Debug.Log($"[EnemyPerception] {name}: Lost sight timeout — firing TargetLost.");
                    OnPerceptionEvent?.Invoke(PerceptionEvent.TargetLost, LastKnownPosition);
                }
            }
        }
    }

    private bool CheckColdSight()
    {
        Vector3 toTarget = Target.position - transform.position;
        float   sqrDist  = toTarget.sqrMagnitude;
        if (sqrDist > _coldSightDistance * _coldSightDistance) return false;
        if (Vector3.Angle(transform.forward, toTarget) > _coldSightAngle * 0.5f) return false;
        if (!HasLineOfSight()) return false;

        // Light/movement gating: a dark, still, crouched player can hide inside FOV.
        // detectionScore = how visible the player is × how close (linear distance falloff).
        float visibility   = VisibilitySystem.PlayerScore();
        float normDistance = Mathf.Sqrt(sqrDist) / _coldSightDistance;
        float detection    = visibility * (1f - normDistance);
        return detection >= _sightThreshold;
    }

    private bool CheckHotSight()
    {
        Vector3 toTarget = Target.position - transform.position;
        if (toTarget.sqrMagnitude > _hotSightDistance * _hotSightDistance) return false;
        return HasLineOfSight();
    }

    /// <summary>
    /// Casts from eye height to target chest, stopping 0.3 m short so the ray
    /// never reaches the target's own collider and falsely reports blocked.
    /// </summary>
    private bool HasLineOfSight()
    {
        Vector3 eye    = transform.position + Vector3.up * 1.4f;
        Vector3 chest  = Target.position    + Vector3.up * 0.8f;
        Vector3 dir    = chest - eye;
        float   dist   = dir.magnitude - 0.3f;
        if (dist <= 0f) return true;
        return !Physics.Raycast(eye, dir.normalized, dist, _occlusionMask);
    }
}

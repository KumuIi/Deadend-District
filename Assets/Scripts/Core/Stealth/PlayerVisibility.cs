using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aggregates every <see cref="IVisibilityContributor"/> into a single 0..1 visibility
/// <see cref="Score"/> that AI sight checks read. The score is the *product* of all
/// contributor factors, so any one contributor returning 0 (e.g. standing in pitch dark,
/// or inside a Wave-5 smoke cloud) drives total visibility to zero — darkness wins.
///
/// "VisibilitySystem" is not a separate component: it's this aggregation pattern. Any
/// system that affects how visible the player is implements IVisibilityContributor and
/// registers here (light on the player, movement speed, crouch, future cloak augment).
///
/// Recomputed on a 0.1s cadence — visibility is a gameplay signal, not a render value,
/// so frame-perfect accuracy is unnecessary and OverlapSphere light sampling is cheap
/// at 10 Hz.
/// </summary>
public class PlayerVisibility : MonoBehaviour
{
    public static PlayerVisibility Instance { get; private set; }

    [Tooltip("How often (seconds) the aggregate score is recomputed.")]
    [SerializeField] private float _updateInterval = 0.1f;

    private readonly List<IVisibilityContributor> _contributors = new List<IVisibilityContributor>();
    private float _score = 1f;
    private float _timer;

    /// <summary>Aggregate visibility in [0..1]. 1 = fully exposed, 0 = effectively invisible.</summary>
    public float Score => _score;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(IVisibilityContributor contributor)
    {
        if (contributor != null && !_contributors.Contains(contributor))
            _contributors.Add(contributor);
    }

    public void Unregister(IVisibilityContributor contributor) =>
        _contributors.Remove(contributor);

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < _updateInterval) return;
        _timer = 0f;
        Recompute();
    }

    private void Recompute()
    {
        float score = 1f;
        for (int i = 0; i < _contributors.Count; i++)
            score *= Mathf.Clamp01(_contributors[i].GetVisibilityFactor());
        _score = Mathf.Clamp01(score);
    }

    /// <summary>Debug helper — lists each contributor's current factor.</summary>
    public string DebugBreakdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"Visibility {_score:0.00} = ");
        for (int i = 0; i < _contributors.Count; i++)
            sb.Append($"[{_contributors[i].ContributorName} {_contributors[i].GetVisibilityFactor():0.00}] ");
        return sb.ToString();
    }
}

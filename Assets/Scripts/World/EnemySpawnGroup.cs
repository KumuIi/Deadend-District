using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Grand spawner": groups many <see cref="EnemySpawnPoint"/> children and spawns only a
/// random subset each run. Drop e.g. 10 mimic points under one of these and set
/// <see cref="_countPerRun"/> = 3 to get 3 random mimics per run, different each time.
///
/// The owning <see cref="EnemySpawnSystem"/> collects groups and asks each for its picks;
/// points that are NOT under a group are spawned individually by the system (each "for
/// itself": always, or limited). Per-point spawn chance and limited-spawn still apply inside
/// a group: a member still inside its post-kill suppression window is excluded from the pool,
/// and a selected member that fails its chance roll simply doesn't spawn that run.
/// </summary>
public class EnemySpawnGroup : MonoBehaviour
{
    [Tooltip("How many of this group's points spawn each run (chosen at random).")]
    [Min(0)]
    [SerializeField] private int _countPerRun = 3;

    [Tooltip("Percent chance [0..100] the whole group activates this run. 100 = always.")]
    [Range(0f, 100f)]
    [SerializeField] private float _groupChancePercent = 100f;

    [Tooltip("Drag the candidate EnemySpawnPoints here. The group spawns Count Per Run of them " +
             "at random each run. They can sit anywhere in the scene (their own positions are used). " +
             "Leave this EMPTY to fall back to auto-collecting the group's child points instead.")]
    [SerializeField] private EnemySpawnPoint[] _memberPoints;

    private readonly List<EnemySpawnPoint> _resolved = new List<EnemySpawnPoint>();
    private readonly List<EnemySpawnPoint> _pool     = new List<EnemySpawnPoint>();

    /// <summary>
    /// Returns the points to spawn this run: up to <see cref="_countPerRun"/> random members
    /// that are available this run and pass their own chance roll. Returns an empty list when
    /// the group sits the run out. The returned list is reused — callers must consume it before
    /// the next call (the system does, spawning immediately).
    /// </summary>
    public List<EnemySpawnPoint> SelectPointsForRun(List<EnemySpawnPoint> picks)
    {
        picks.Clear();
        if (_countPerRun <= 0) return picks;
        if (_groupChancePercent < 100f && Random.value * 100f >= _groupChancePercent)
            return picks; // group sat this run out

        ResolveMembers();

        // Eligible = has a prefab + not suppressed by a prior kill.
        _pool.Clear();
        foreach (var m in _resolved)
            if (m != null && m.HasPrefab && m.IsAvailableThisRun())
                _pool.Add(m);

        Shuffle(_pool);

        int take = Mathf.Min(_countPerRun, _pool.Count);
        for (int i = 0; i < take; i++)
            if (_pool[i].RollSpawnChance())
                picks.Add(_pool[i]);

        return picks;
    }

    /// <summary>
    /// Every point this group owns (the drag-in array, or its child points when the array is
    /// empty). The owning EnemySpawnSystem uses this to exclude these points from standalone
    /// spawning — array members may live outside the group's hierarchy, so it can't rely on
    /// parenting alone.
    /// </summary>
    public void GetOwnedPoints(List<EnemySpawnPoint> buffer)
    {
        ResolveMembers();
        foreach (var m in _resolved)
            if (m != null) buffer.Add(m);
    }

    // Drag-in array wins; falls back to child points only when the array is empty/unset.
    private void ResolveMembers()
    {
        _resolved.Clear();
        if (_memberPoints != null && _memberPoints.Length > 0)
        {
            foreach (var m in _memberPoints)
                if (m != null) _resolved.Add(m);
        }
        else
        {
            GetComponentsInChildren(true, _resolved);
        }
    }

    // Fisher–Yates so each run's subset is a uniform random pick of the eligible members.
    private static void Shuffle(List<EnemySpawnPoint> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void OnDrawGizmos()
    {
        ResolveMembers(); // draws links to the drag-in array (or children when empty)
        Gizmos.color = new Color(1f, 0.2f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.color = new Color(1f, 0.2f, 0.5f, 0.35f);
        foreach (var m in _resolved)
            if (m != null) Gizmos.DrawLine(transform.position, m.transform.position);
    }
}

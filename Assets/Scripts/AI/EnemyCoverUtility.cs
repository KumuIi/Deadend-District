using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Static utility for finding NavMesh-reachable cover positions that block
/// line-of-sight from a candidate point to a threat position.
/// </summary>
public static class EnemyCoverUtility
{
    private static readonly List<Vector3> _candidates = new();

    /// <summary>
    /// Samples <paramref name="sampleCount"/> random points within <paramref name="searchRadius"/>
    /// of <paramref name="agentPos"/>, keeps those that (a) are blocked from the threat's
    /// eye line and (b) have a complete NavMesh path from the agent.
    /// Returns the closest valid candidate, or null if none found.
    /// </summary>
    public static Vector3? FindCoverPoint(
        Vector3      agentPos,
        Vector3      threatPos,
        float        searchRadius,
        int          sampleCount,
        float        eyeHeight,
        LayerMask    coverMask,
        NavMeshAgent agent)
    {
        _candidates.Clear();

        for (int i = 0; i < sampleCount; i++)
        {
            Vector2 rand   = Random.insideUnitCircle * searchRadius;
            Vector3 probe  = agentPos + new Vector3(rand.x, 0f, rand.y);

            // Snap to NavMesh surface
            if (!NavMesh.SamplePosition(probe, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                continue;

            Vector3 candidate = navHit.position;

            // LOS check: the cover is good if a ray from candidate-eye to threat-eye is BLOCKED
            Vector3 covEye    = candidate  + Vector3.up * eyeHeight;
            Vector3 threatEye = threatPos  + Vector3.up * eyeHeight;
            Vector3 dir       = threatEye  - covEye;
            float   dist      = dir.magnitude;

            if (!Physics.Raycast(covEye, dir.normalized, dist, coverMask))
                continue;  // threat can see this point — not valid cover

            // Verify a complete path exists (SamplePosition gives nearest mesh point, not a
            // routable destination — must check path status explicitly)
            var path = new NavMeshPath();
            agent.CalculatePath(candidate, path);
            if (path.status != NavMeshPathStatus.PathComplete) continue;

            _candidates.Add(candidate);
        }

        if (_candidates.Count == 0) return null;

        // Prefer the cover point closest to the agent (minimises travel exposure)
        Vector3? best     = null;
        float    bestSqr  = float.MaxValue;
        foreach (var c in _candidates)
        {
            float sqr = (c - agentPos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = c; }
        }
        return best;
    }
}

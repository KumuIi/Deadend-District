using UnityEngine.AI;

/// <summary>
/// Shared NavMesh area conventions (W3-07).
///
/// Ladders are traversed by a <c>NavMeshLink</c> assigned to the custom area
/// <see cref="LadderClimb"/>. Only the Mimic is allowed across these links — it is a
/// wall-crawler that ascends the rungs naturally (see MonsterAI.CrawlToward). Bipedal
/// guards would teleport/slide up a vertical link, which looks wrong, so they exclude
/// the ladder area from their agent's <c>areaMask</c> via <see cref="ExcludeLadder"/>.
///
/// Editor setup: add a NavMesh area named exactly "LadderClimb" (Navigation ▸ Areas),
/// then set each ladder's NavMeshLink Area to it. Until that area exists, the helpers
/// no-op so nothing breaks.
/// </summary>
public static class NavAreas
{
    public const string LadderClimb = "LadderClimb";

    /// <summary>
    /// Strips the LadderClimb area bit from <paramref name="agent"/>'s areaMask so it never
    /// paths over ladder links. No-op if the agent is null or the area isn't defined.
    /// </summary>
    public static void ExcludeLadder(NavMeshAgent agent)
    {
        if (agent == null) return;
        int area = NavMesh.GetAreaFromName(LadderClimb);
        if (area >= 0) agent.areaMask &= ~(1 << area);
    }
}

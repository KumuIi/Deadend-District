/// <summary>
/// Faction identity used by AI targeting. Never hardcode "Player" tag — call IsHostileTo.
/// Implementors: PlayerHealth, BaseEnemyAI subclasses, neutral NPCs.
/// </summary>
public enum TeamId { Player, Guard, Monster, Neutral, Trader }

public interface IFactionProvider
{
    TeamId TeamId { get; }
    bool IsHostileTo(TeamId other);
}

/// <summary>
/// Receive run-state events from RunManager without polling its state in Update.
/// Implementors: EnemySpawnSystem, LootSpawnSystem, BatterySystem, DarknessStateWriter,
///               MonsterAI, TraderSystem (restock check).
/// Register in OnEnable, unregister in OnDisable.
/// RunManager snapshots its listener list before dispatching — safe to register/unregister
/// inside a callback.
/// </summary>
public interface IRunLifecycleListener
{
    void OnRunStarted();
    void OnRunExtracted();
    void OnRunDied();
    void OnReturnedToHub();
}

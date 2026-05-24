/// <summary>
/// Entity that was spawned by a system and needs clean teardown on sector unload.
/// Implementors: enemy spawns, loot spawns, throwables, item drops.
/// SectorManager calls OnDespawned() on all entities with this interface before
/// unloading a sector scene.
/// PoolId groups entities so systems can bulk-despawn their own spawns only.
/// </summary>
public interface IPoolableSpawnedEntity
{
    /// <summary>Groups entities by spawner, e.g. "enemy", "loot", "throwable".</summary>
    string PoolId { get; }

    void OnSpawned();
    void OnDespawned();
}

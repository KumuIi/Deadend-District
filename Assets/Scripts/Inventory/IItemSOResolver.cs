/// <summary>
/// Resolves an ItemSO asset by its ScriptableObject asset name.
/// Keeps InventoryGrid free of any Unity resource-loading dependency.
/// Implement this interface with your preferred loading strategy
/// (Resources, Addressables, a manual registry, etc.).
/// </summary>
public interface IItemSOResolver
{
    /// <summary>
    /// Returns the ItemSO whose asset name matches <paramref name="soName"/>,
    /// or null if no match is found.
    /// </summary>
    ItemSO Resolve(string soName);
}

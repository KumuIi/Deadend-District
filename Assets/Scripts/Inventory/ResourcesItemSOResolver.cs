using UnityEngine;

/// <summary>
/// Default IItemSOResolver that loads ItemSO assets from a Resources folder.
/// Place your ItemSO assets inside a "Resources/Items/" folder (or change
/// the <see cref="ResourcesFolder"/> constant to match your project layout).
/// </summary>
public sealed class ResourcesItemSOResolver : IItemSOResolver
{
    /// <summary>Path inside any Resources folder where ItemSO assets live.</summary>
    private const string ResourcesFolder = "Items/";

    /// <inheritdoc/>
    public ItemSO Resolve(string soName)
    {
        if (string.IsNullOrEmpty(soName)) return null;
        return Resources.Load<ItemSO>(ResourcesFolder + soName);
    }
}

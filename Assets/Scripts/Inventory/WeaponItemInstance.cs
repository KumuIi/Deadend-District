/// <summary>
/// Runtime state for a weapon item in the inventory grid.
/// Holds an optional loaded magazine and an optional link to the scene GunController.
/// </summary>
public class WeaponItemInstance : ItemInstance
{
    public MagazineItemInstance LoadedMagazine { get; private set; }

    /// <summary>
    /// The scene GunController that corresponds to this inventory item.
    /// Set by InventoryTester (or the future loot system) when the item is registered.
    /// </summary>
    public GunController LinkedGun { get; set; }

    public WeaponItemInstance(WeaponSO definition) : base(definition) { }

    public WeaponSO WeaponDef => (WeaponSO)data;

    /// <summary>
    /// Loads a magazine into the inventory record only — does NOT touch the live GunController.
    /// Returns false if a magazine is already loaded or calibers don't match.
    /// </summary>
    public bool LoadMagazine(MagazineItemInstance mag)
    {
        if (mag == null || LoadedMagazine != null) return false;
        if (WeaponDef.caliber != mag.MagDef.caliber) return false;
        LoadedMagazine = mag;
        return true;
    }

    /// <summary>
    /// Removes and returns the loaded magazine from the inventory record only — does NOT touch the live GunController.
    /// </summary>
    public MagazineItemInstance EjectMagazine()
    {
        if (LoadedMagazine == null) return null;
        var mag = LoadedMagazine;
        LoadedMagazine = null;
        return mag;
    }

    /// <summary>
    /// Sets the inventory-side loaded magazine directly, used by the reload flow
    /// after the live GunController has already been handed the new magazine.
    /// </summary>
    public void BeginReloadWith(MagazineItemInstance mag) => LoadedMagazine = mag;
}

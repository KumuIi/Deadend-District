/// <summary>
/// Runtime state for a magazine item in the inventory grid.
/// Combines grid placement (ItemInstance) with live ammo tracking (MagazineInstance).
/// </summary>
public class MagazineItemInstance : ItemInstance
{
    /// <summary>The live ammo state — rounds loaded, caliber, capacity.</summary>
    public readonly MagazineInstance RuntimeMag;

    public MagazineItemInstance(MagazineSO definition) : base(definition)
    {
        RuntimeMag = new MagazineInstance(definition);
    }

    public MagazineSO MagDef => (MagazineSO)data;
}

namespace PlantOps.Modules.Inventory.Domain;

internal readonly record struct SparePartId(Guid Value)
{
    public static SparePartId New() => new(Guid.CreateVersion7());
}

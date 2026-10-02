namespace PlantOps.Modules.Assets.Domain;

internal readonly record struct ProductionLineId(Guid Value)
{
    public static ProductionLineId New() => new(Guid.CreateVersion7());
}

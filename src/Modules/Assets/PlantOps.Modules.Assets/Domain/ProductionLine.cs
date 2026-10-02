namespace PlantOps.Modules.Assets.Domain;

internal sealed class ProductionLine(ProductionLineId id, string code, string name)
{
    public ProductionLineId Id { get; private set; } = id;

    public string Code { get; private set; } = code;

    public string Name { get; private set; } = name;
}

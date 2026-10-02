namespace PlantOps.Modules.Assets.Domain;

internal sealed record Location
{
    public const int StationMaxLength = 50;

    public Location(ProductionLineId lineId, string station)
    {
        LineId = lineId;
        Station = TextRules.Required(station, "Station", StationMaxLength);
    }

    public ProductionLineId LineId { get; }

    public string Station { get; }
}

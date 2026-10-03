using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Domain;

internal sealed class Asset : AggregateRoot
{
    public const int NameMaxLength = 100;
    public const int ManufacturerMaxLength = 100;
    public const int ModelMaxLength = 100;
    public const int SerialNumberMaxLength = 100;
    public const int DecommissionReasonMaxLength = 500;

    // For EF Core only.
    private Asset()
    {
    }

    public AssetId Id { get; private set; }

    public override string AggregateId => Id.Value.ToString();

    // The mapped column is the raw string, with the AssetTag exposed on top. A value converter would hide the
    // string from EF, so a prefix search could only translate as CAST(Tag AS nvarchar(max)) LIKE ... (no index
    // seek). Queries reach the column with EF.Property<string>(a, TagField).
    internal const string TagField = "_tag";

    private string _tag = null!;

    public AssetTag Tag => AssetTag.Create(_tag);

    public string Name { get; private set; } = null!;

    public string Manufacturer { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    public string? SerialNumber { get; private set; }

    public ProductionLineId LineId { get; private set; }

    public string Station { get; private set; } = null!;

    // Stored as two scalar columns (EF Core 9 cannot index or reference complex-type properties);
    // the value object is rebuilt on demand and is not mapped.
    public Location Location => new(LineId, Station);

    public Criticality Criticality { get; private set; }

    public AssetStatus Status { get; private set; }

    public DateOnly CommissionedOn { get; private set; }

    // Two nullable columns rather than a `Decommissioning` value object: EF Core 9 complex types cannot be
    // optional (EF Core 10 adds that). Revisit when upgrading.
    public DateOnly? DecommissionedOn { get; private set; }

    public string? DecommissionReason { get; private set; }

    public static Asset Register(
        AssetTag tag,
        string name,
        string manufacturer,
        string model,
        string? serialNumber,
        Location location,
        Criticality criticality,
        DateOnly commissionedOn)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(location);
        EnsureDefined(criticality);

        var asset = new Asset
        {
            Id = AssetId.New(),
            _tag = tag.Value,
            Name = TextRules.Required(name, "Name", NameMaxLength),
            Manufacturer = TextRules.Required(manufacturer, "Manufacturer", ManufacturerMaxLength),
            Model = TextRules.Required(model, "Model", ModelMaxLength),
            SerialNumber = TextRules.Optional(serialNumber, "Serial number", SerialNumberMaxLength),
            LineId = location.LineId,
            Station = location.Station,
            Criticality = criticality,
            Status = AssetStatus.InService,
            CommissionedOn = commissionedOn,
        };

        asset.Raise(new AssetRegistered(
            asset.Id.Value,
            asset._tag,
            asset.Name,
            asset.LineId.Value,
            asset.Station,
            asset.Criticality));
        return asset;
    }

    public void UpdateDetails(string name, string manufacturer, string model, string? serialNumber)
    {
        EnsureInService();
        Name = TextRules.Required(name, "Name", NameMaxLength);
        Manufacturer = TextRules.Required(manufacturer, "Manufacturer", ManufacturerMaxLength);
        Model = TextRules.Required(model, "Model", ModelMaxLength);
        SerialNumber = TextRules.Optional(serialNumber, "Serial number", SerialNumberMaxLength);
        Raise(new AssetDetailsUpdated(Id.Value, Name, Manufacturer, Model, SerialNumber));
    }

    public void Relocate(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        EnsureInService();
        var from = Location;
        LineId = location.LineId;
        Station = location.Station;
        Raise(new AssetRelocated(Id.Value, from.LineId.Value, from.Station, LineId.Value, Station));
    }

    public void ChangeCriticality(Criticality criticality)
    {
        EnsureInService();
        EnsureDefined(criticality);
        var from = Criticality;
        Criticality = criticality;
        Raise(new AssetCriticalityChanged(Id.Value, from, criticality));
    }

    /// <param name="on">Date the asset left service.</param>
    /// <param name="today">Passed in (not read from a clock) so the rule stays deterministic and testable.</param>
    public void Decommission(DateOnly on, string reason, DateOnly today)
    {
        EnsureInService();
        var trimmedReason = TextRules.Required(reason, "Decommission reason", DecommissionReasonMaxLength);

        if (on < CommissionedOn)
        {
            throw new DomainException($"Decommission date {on:yyyy-MM-dd} is before the commissioning date {CommissionedOn:yyyy-MM-dd}.");
        }

        if (on > today)
        {
            throw new DomainException($"Decommission date {on:yyyy-MM-dd} is in the future.");
        }

        Status = AssetStatus.Decommissioned;
        DecommissionedOn = on;
        DecommissionReason = trimmedReason;
        Raise(new AssetDecommissioned(Id.Value, on, trimmedReason));
    }

    private static void EnsureDefined(Criticality criticality)
    {
        if (!Enum.IsDefined(criticality))
        {
            throw new DomainException($"'{criticality}' is not a valid criticality.");
        }
    }

    private void EnsureInService()
    {
        if (Status == AssetStatus.Decommissioned)
        {
            throw new DomainException($"Asset {Tag} is decommissioned and can no longer be changed.");
        }
    }
}

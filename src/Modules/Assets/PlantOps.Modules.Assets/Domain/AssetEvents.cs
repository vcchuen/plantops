using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Domain;

// Payloads carry ids and the NEW values, not the whole entity: an audit reader wants "what changed to what",
// and a full snapshot would freeze today's schema into history. Plain Guid ids (not AssetId) so the JSON is flat.
internal sealed record AssetRegistered(
    Guid AssetId,
    string Tag,
    string Name,
    Guid LineId,
    string Station,
    Criticality Criticality) : IDomainEvent;

internal sealed record AssetDetailsUpdated(
    Guid AssetId,
    string Name,
    string Manufacturer,
    string Model,
    string? SerialNumber) : IDomainEvent;

internal sealed record AssetRelocated(
    Guid AssetId,
    Guid FromLineId,
    string FromStation,
    Guid LineId,
    string Station) : IDomainEvent;

internal sealed record AssetCriticalityChanged(
    Guid AssetId,
    Criticality From,
    Criticality To) : IDomainEvent;

internal sealed record AssetDecommissioned(
    Guid AssetId,
    DateOnly On,
    string Reason) : IDomainEvent;

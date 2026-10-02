using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Endpoints;

internal sealed record LineResponse(Guid Id, string Code, string Name);

internal sealed record AssetListItem(
    Guid Id,
    string Tag,
    string Name,
    Guid LineId,
    string LineName,
    string Station,
    Criticality Criticality,
    AssetStatus Status);

internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record AssetDetail(
    Guid Id,
    string Tag,
    string Name,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    Guid LineId,
    string LineCode,
    string LineName,
    string Station,
    Criticality Criticality,
    AssetStatus Status,
    DateOnly CommissionedOn,
    DateOnly? DecommissionedOn,
    string? DecommissionReason);

internal sealed record ListAssetsQuery(
    Guid? LineId,
    Criticality? Criticality,
    AssetStatus? Status,
    string? Search,
    int Page = 1,
    int PageSize = 25);

internal sealed record RegisterAssetRequest(
    string Tag,
    string Name,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    Guid LineId,
    string Station,
    Criticality Criticality,
    DateOnly CommissionedOn);

internal sealed record UpdateDetailsRequest(string Name, string Manufacturer, string Model, string? SerialNumber);

internal sealed record RelocateRequest(Guid LineId, string Station);

internal sealed record ChangeCriticalityRequest(Criticality Criticality);

internal sealed record DecommissionRequest(DateOnly On, string Reason);

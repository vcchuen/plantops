namespace PlantOps.Api.Seeding;

// The demo dataset as plain data: no database, no module types, so it can be generated and unit-tested anywhere.
// DemoSeeder is the only thing that turns it into aggregates.

/// <summary>Subject ids of the demo users. They are also the "id" of the matching users in deploy/keycloak/plantops-realm.json,
/// so a person's Keycloak "sub" equals the id the seeded work orders already carry.</summary>
internal static class DemoSeedIds
{
    public const string Olivia = "11111111-1111-4111-8111-111111111111";
    public const string Tom = "22222222-2222-4222-8222-222222222222";
    public const string Sam = "33333333-3333-4333-8333-333333333333";
    public const string Ada = "44444444-4444-4444-8444-444444444444";
}

internal sealed record SeedUser(string Id, string Name, string Email, string Role);

internal sealed record SeedAsset(
    string Tag,
    string Name,
    string Manufacturer,
    string Model,
    string SerialNumber,
    string LineCode,
    string Station,
    string Criticality,
    DateOnly CommissionedOn);

internal sealed record SeedPart(
    string PartNumber,
    string Name,
    string Unit,
    string BinLocation,
    int ReorderLevel,
    int InitialOnHand);

internal sealed record SeedPmSchedule(
    string AssetTag,
    string Title,
    string Instructions,
    int IntervalDays,
    int LeadDays,
    string Priority,
    DateOnly NextDueOn,
    IReadOnlyList<DateOnly> PastDueDates,
    string? PartNumber,
    int PartQuantity);

internal enum SeedWorkOrderStatus
{
    Submitted,
    Approved,
    Assigned,
    InProgress,
    Completed,
    Closed,
    Rejected,
    Cancelled,
}

internal sealed record SeedPartUse(string PartNumber, int Quantity, DateTimeOffset At);

internal sealed record SeedWorkOrder(
    int Key,
    string AssetTag,
    string Title,
    string Description,
    string Priority,
    bool AssetDown,
    int? PmScheduleIndex,
    DateOnly? PmDueOn,
    string ReporterId,
    string SupervisorId,
    string TechnicianId,
    SeedWorkOrderStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? EndedAt,
    string? Reason,
    string? Resolution,
    IReadOnlyList<SeedPartUse> Parts)
{
    public bool IsPreventive => PmScheduleIndex is not null;

    /// <summary>The work reached a technician's hands: stock is held (Active) or was consumed.</summary>
    public bool UsesParts => Parts.Count > 0 && StartedAt is not null;

    public bool IsDone => Status is SeedWorkOrderStatus.Completed or SeedWorkOrderStatus.Closed;
}

internal sealed record DemoSeedPlan(
    IReadOnlyList<SeedUser> Users,
    IReadOnlyList<SeedAsset> Assets,
    IReadOnlyList<SeedPart> Parts,
    IReadOnlyList<SeedPmSchedule> Schedules,
    IReadOnlyList<SeedWorkOrder> WorkOrders)
{
    /// <summary>The factory (Penang) has no daylight saving, so a fixed offset is exact and keeps the plan free of tz data.</summary>
    public static readonly TimeSpan FactoryOffset = TimeSpan.FromHours(8);

    public const int DefaultSeed = 20261003;

    /// <summary>Stock left on the shelf (not reserved) of this part, which the E2E journey relies on.</summary>
    public const string KeyPartNumber = "NZL-CN040";

    public const int KeyPartMinimumAvailable = 20;

    public static DemoSeedPlan Generate(DateTimeOffset now, int seed = DefaultSeed) => DemoSeedGenerator.Generate(now, seed);
}

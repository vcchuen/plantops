namespace PlantOps.Api.Seeding;

// Builds the demo dataset from the catalog. Deterministic: the same (now, seed) always gives the same plan, and the
// only randomness is one System.Random with a fixed seed consumed in a fixed order. "now" is a parameter so the
// history always ends at the moment of seeding and tests can pin it.
internal static class DemoSeedGenerator
{
    // How many reactive work orders end in each status (64) plus 16 preventive ones = 80.
    private static readonly (SeedWorkOrderStatus Status, int Count)[] ReactiveMix =
    [
        (SeedWorkOrderStatus.Closed, 30),
        (SeedWorkOrderStatus.Completed, 6),
        (SeedWorkOrderStatus.Rejected, 5),
        (SeedWorkOrderStatus.Cancelled, 5),
        (SeedWorkOrderStatus.InProgress, 5),
        (SeedWorkOrderStatus.Assigned, 5),
        (SeedWorkOrderStatus.Approved, 4),
        (SeedWorkOrderStatus.Submitted, 4),
    ];

    private const int OccurrencesPerSchedule = 2;

    // Schedules whose newest occurrence is done but not yet signed off (Completed rather than Closed).
    private static readonly int[] AwaitingSignOff = [0, 2];

    public static DemoSeedPlan Generate(DateTimeOffset now, int seed)
    {
        var random = new Random(seed);
        now = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerMinute), TimeSpan.Zero);
        var today = DateOnly.FromDateTime((now + DemoSeedPlan.FactoryOffset).DateTime);

        var assets = BuildAssets(random, today);
        var schedules = BuildSchedules(random, today);

        var workOrders = new List<SeedWorkOrder>();
        foreach (var (status, count) in ReactiveMix)
        {
            for (var i = 0; i < count; i++)
            {
                workOrders.Add(BuildReactive(random, now, assets, status, i));
            }
        }

        for (var s = 0; s < schedules.Count; s++)
        {
            for (var o = 0; o < schedules[s].PastDueDates.Count; o++)
            {
                var newest = o == schedules[s].PastDueDates.Count - 1;
                var status = newest && AwaitingSignOff.Contains(s) ? SeedWorkOrderStatus.Completed : SeedWorkOrderStatus.Closed;
                workOrders.Add(BuildPreventive(random, s, schedules[s], schedules[s].PastDueDates[o], status));
            }
        }

        // Oldest first: the database hands out WO numbers at insert, so the numbering then reads chronologically.
        var ordered = workOrders
            .OrderBy(w => w.SubmittedAt)
            .Select((w, index) => w with { Key = index + 1 })
            .ToList();

        return new DemoSeedPlan(DemoSeedCatalog.Users, assets, BuildParts(random, ordered), schedules, ordered);
    }

    private static List<SeedAsset> BuildAssets(Random random, DateOnly today) =>
        DemoSeedCatalog.Assets
            .Select(a => new SeedAsset(
                a.Tag,
                a.Name,
                a.Manufacturer,
                a.Model,
                $"{new string(a.Manufacturer.Where(char.IsLetter).Take(3).ToArray()).ToUpperInvariant()}-{random.Next(100000, 1000000)}",
                a.LineCode,
                a.Station,
                a.Criticality,
                today.AddDays(-(a.YearsInService * 365 + random.Next(0, 300)))))
            .ToList();

    private static List<SeedPmSchedule> BuildSchedules(Random random, DateOnly today)
    {
        var schedules = new List<SeedPmSchedule>();
        foreach (var row in DemoSeedCatalog.PmSchedules)
        {
            // The newest occurrence was due 6+ days ago (so all of its work is in the past) and the next one is due
            // a little under one interval from today.
            var lastDue = today.AddDays(-random.Next(6, Math.Min(row.IntervalDays - 4, 40)));
            var dueDates = Enumerable.Range(0, OccurrencesPerSchedule)
                .Select(i => lastDue.AddDays(-row.IntervalDays * (OccurrencesPerSchedule - 1 - i)))
                .ToList();
            schedules.Add(new SeedPmSchedule(
                row.AssetTag,
                row.Title,
                row.Instructions,
                row.IntervalDays,
                row.LeadDays,
                row.Priority,
                lastDue.AddDays(row.IntervalDays),
                dueDates,
                row.PartNumber,
                row.PartQuantity));
        }

        return schedules;
    }

    private static SeedWorkOrder BuildReactive(Random random, DateTimeOffset now, List<SeedAsset> assets, SeedWorkOrderStatus status, int ordinal)
    {
        var asset = PickAsset(random, assets);
        var templates = DemoSeedCatalog.Issues[DemoSeedCatalog.KindOf(asset.Tag)];
        var template = templates[random.Next(templates.Length)];

        var priority = template.CanStopTheLine
            ? new[] { "P1", "P2", "P3" }[PickWeighted(random, 35, 40, 25)]
            : new[] { "P1", "P2", "P3", "P4" }[PickWeighted(random, 3, 20, 50, 27)];
        var assetDown = template.CanStopTheLine && random.NextDouble() < 0.85;

        var reporter = new[] { DemoSeedIds.Olivia, DemoSeedIds.Tom, DemoSeedIds.Sam, DemoSeedIds.Ada }[PickWeighted(random, 60, 20, 12, 8)];
        var supervisor = random.NextDouble() < 0.85 ? DemoSeedIds.Sam : DemoSeedIds.Ada;

        var late = random.NextDouble() < 0.22;
        var stages = BuildStages(random, priority, late);
        var closeDelay = Minutes(60 + random.NextDouble() * 29 * 60);

        var toApproved = stages.Approve;
        var toAssigned = toApproved + stages.Assign;
        var toStarted = toAssigned + stages.Start;
        var toCompleted = toStarted + stages.Work;

        // How long a rejection or cancellation took to be decided, drawn once so the age and the end time agree.
        var rejectDelay = Minutes(30 + random.NextDouble() * 1170);
        var cancelDelay = Minutes(30 + random.NextDouble() * 690);
        var cancelledAfterAssign = status == SeedWorkOrderStatus.Cancelled && ordinal % 2 == 0;

        // How long before "now" the work order was raised: the chain it went through, plus some slack. Closed history is
        // spread over the last ~6 months; open work is recent.
        var age = status switch
        {
            SeedWorkOrderStatus.Closed => toCompleted + closeDelay + HistoricSlack(random),
            SeedWorkOrderStatus.Completed => toCompleted + Minutes(60 + random.NextDouble() * 29 * 60),
            SeedWorkOrderStatus.Rejected => rejectDelay + HistoricSlack(random),
            SeedWorkOrderStatus.Cancelled => (cancelledAfterAssign ? toAssigned : toApproved) + cancelDelay + HistoricSlack(random),
            SeedWorkOrderStatus.InProgress => toStarted + Minutes(10 + random.NextDouble() * 1190),
            SeedWorkOrderStatus.Assigned => toAssigned + Minutes(10 + random.NextDouble() * 1790),
            SeedWorkOrderStatus.Approved => toApproved + Minutes(10 + random.NextDouble() * 2390),
            // The first one is a neglected report, so the demo has a request that is days old and long past its SLA.
            _ => ordinal == 0 ? TimeSpan.FromHours(4.5 * 24) : Minutes(20 + random.NextDouble() * 1780),
        };

        var submitted = now - age;
        var approved = submitted + toApproved;
        var assigned = submitted + toAssigned;
        var started = submitted + toStarted;
        var completed = submitted + toCompleted;
        var closed = completed + closeDelay;

        // A cancelled order was cancelled after approval or after assignment; a rejected one was refused instead of approved.
        var reached = status switch
        {
            SeedWorkOrderStatus.Submitted or SeedWorkOrderStatus.Rejected => 0,
            SeedWorkOrderStatus.Approved => 1,
            SeedWorkOrderStatus.Cancelled => cancelledAfterAssign ? 2 : 1,
            SeedWorkOrderStatus.Assigned => 2,
            _ => 3,
        };

        DateTimeOffset? endedAt = status switch
        {
            SeedWorkOrderStatus.Rejected => submitted + rejectDelay,
            SeedWorkOrderStatus.Cancelled => (cancelledAfterAssign ? assigned : approved) + cancelDelay,
            _ => null,
        };

        var finished = status is SeedWorkOrderStatus.Completed or SeedWorkOrderStatus.Closed;
        var parts = new List<SeedPartUse>();
        if (template.PartNumber is not null
            && (finished || (status == SeedWorkOrderStatus.InProgress && random.NextDouble() < 0.6)))
        {
            parts.Add(new SeedPartUse(
                template.PartNumber,
                random.Next(template.MinQuantity, template.MaxQuantity + 1),
                started + TimeSpan.FromMinutes(5)));
        }

        return new SeedWorkOrder(
            0,
            asset.Tag,
            template.Title,
            $"{template.Description} Machine: {asset.Name}, station {asset.Station}.",
            priority,
            assetDown,
            null,
            null,
            reporter,
            supervisor,
            DemoSeedIds.Tom,
            status,
            submitted,
            reached >= 1 ? approved : null,
            reached >= 2 ? assigned : null,
            reached >= 3 ? started : null,
            finished ? completed : null,
            status == SeedWorkOrderStatus.Closed ? closed : null,
            endedAt,
            status switch
            {
                SeedWorkOrderStatus.Rejected => DemoSeedCatalog.RejectionReasons[ordinal % DemoSeedCatalog.RejectionReasons.Length],
                SeedWorkOrderStatus.Cancelled => DemoSeedCatalog.CancellationReasons[ordinal % DemoSeedCatalog.CancellationReasons.Length],
                _ => null,
            },
            finished ? template.Resolution : null,
            parts);
    }

    private static SeedWorkOrder BuildPreventive(Random random, int scheduleIndex, SeedPmSchedule schedule, DateOnly dueOn, SeedWorkOrderStatus status)
    {
        // The PM runner raises the order when the lead window opens, at the start of the working day.
        var submitted = LocalTime(dueOn.AddDays(-schedule.LeadDays), 7, 0);
        var assigned = submitted + Minutes(60 + random.NextDouble() * 240);

        // One in five is done after its due date (a missed deadline in the reports).
        var late = random.NextDouble() < 0.2;
        var started = LocalTime(late ? dueOn.AddDays(random.Next(1, 4)) : dueOn, 8, 0) + Minutes(random.NextDouble() * 150);
        var completed = started + Minutes(60 + random.NextDouble() * 180);
        var closed = completed + Minutes(120 + random.NextDouble() * 1440);

        var resolutions = new[]
        {
            "Planned maintenance completed per the checklist; no abnormal findings.",
            "Checklist completed. Minor wear noted and logged for the next interval.",
            "Done per checklist; readings within specification and the machine returned to production.",
        };

        var parts = schedule.PartNumber is null
            ? []
            : new List<SeedPartUse> { new(schedule.PartNumber, schedule.PartQuantity, started + TimeSpan.FromMinutes(5)) };

        return new SeedWorkOrder(
            0,
            schedule.AssetTag,
            schedule.Title,
            schedule.Instructions,
            schedule.Priority,
            false,
            scheduleIndex,
            dueOn,
            "system",
            random.NextDouble() < 0.85 ? DemoSeedIds.Sam : DemoSeedIds.Ada,
            DemoSeedIds.Tom,
            status,
            submitted,
            submitted,
            assigned,
            started,
            completed,
            status == SeedWorkOrderStatus.Closed ? closed : null,
            null,
            null,
            resolutions[random.Next(resolutions.Length)],
            parts);
    }

    // On-time orders finish inside the SLA target; late ones overrun it by 10-70 percent, almost always while waiting to
    // be started (no technician free, part on order). Stage lengths are capped so a P4 repair is not "40 hours of wrenching".
    private static (TimeSpan Approve, TimeSpan Assign, TimeSpan Start, TimeSpan Work) BuildStages(Random random, string priority, bool late)
    {
        var target = TargetFor(priority);
        if (late)
        {
            var approve = Clamp(target * 0.1, Minutes(5), TimeSpan.FromHours(3));
            var assign = Clamp(target * 0.05, Minutes(3), TimeSpan.FromHours(1));
            var work = Clamp(target * (0.15 + random.NextDouble() * 0.25), Minutes(30), TimeSpan.FromHours(10));
            var total = target * (1.1 + random.NextDouble() * 0.6);
            return (approve, assign, Round(total - approve - assign - work), work);
        }
        else
        {
            var total = target * (0.3 + random.NextDouble() * 0.6);
            var approve = Clamp(total * 0.12, Minutes(5), TimeSpan.FromHours(3));
            var assign = Clamp(total * 0.06, Minutes(3), TimeSpan.FromHours(1));
            var start = Clamp(total * 0.25, Minutes(10), TimeSpan.FromHours(6));
            var work = Clamp(total - approve - assign - start, Minutes(20), TimeSpan.FromHours(10));
            return (approve, assign, start, work);
        }
    }

    // Parts are sized from the work orders: enough to cover everything the history consumed and everything held for
    // open work, plus a shelf amount. NZL-CN040 is the part the E2E journey reserves, so it always keeps 30 free.
    private static List<SeedPart> BuildParts(Random random, List<SeedWorkOrder> workOrders)
    {
        var consumed = Usage(workOrders.Where(w => w.IsDone));
        var held = Usage(workOrders.Where(w => w.Status == SeedWorkOrderStatus.InProgress));

        return DemoSeedCatalog.Parts
            .Select(row =>
            {
                var available = row.PartNumber == DemoSeedPlan.KeyPartNumber
                    ? 30
                    : row.Low
                        ? Math.Max(0, row.ReorderLevel - 1)
                        : row.ReorderLevel + 2 + random.Next(0, row.ReorderLevel * 2 + 1);
                var initial = consumed.GetValueOrDefault(row.PartNumber) + held.GetValueOrDefault(row.PartNumber) + available;
                return new SeedPart(row.PartNumber, row.Name, row.Unit, row.Bin, row.ReorderLevel, initial);
            })
            .ToList();

        static Dictionary<string, int> Usage(IEnumerable<SeedWorkOrder> orders) =>
            orders.SelectMany(w => w.Parts).GroupBy(p => p.PartNumber).ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));
    }

    private static SeedAsset PickAsset(Random random, List<SeedAsset> assets)
    {
        // Machines that stop a line (A) fail in the demo more often than ones with workarounds (C).
        var weights = assets.Select(a => a.Criticality switch { "A" => 3, "B" => 2, _ => 1 }).ToArray();
        return assets[PickWeighted(random, weights)];
    }

    private static int PickWeighted(Random random, params int[] weights)
    {
        var roll = random.Next(weights.Sum());
        for (var i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }

    private static TimeSpan TargetFor(string priority) => priority switch
    {
        "P1" => TimeSpan.FromHours(4),
        "P2" => TimeSpan.FromHours(8),
        "P3" => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(72),
    };

    // Closed history is spread over the last ~6 months (plus a minimum so the chain always ends in the past).
    private static TimeSpan HistoricSlack(Random random) => Minutes(60 + random.NextDouble() * 174 * 24 * 60);

    private static DateTimeOffset LocalTime(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), DemoSeedPlan.FactoryOffset).ToUniversalTime();

    private static TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(Math.Round(minutes));

    private static TimeSpan Round(TimeSpan value) => Minutes(value.TotalMinutes);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => Round(value < min ? min : value > max ? max : value);
}

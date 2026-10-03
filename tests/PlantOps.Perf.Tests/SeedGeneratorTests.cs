using PlantOps.Perf;

namespace PlantOps.Perf.Tests;

public class SeedGeneratorTests
{
    // Small, so the tests are fast; the shape rules are the same as for the 20,000-row run.
    private static readonly SeedOptions Small = new(Assets: 40, WorkOrders: 500, Facts: 500);

    [Fact]
    public void Same_seed_gives_the_same_data()
    {
        var a = SeedGenerator.Generate(Small);
        var b = SeedGenerator.Generate(Small);

        Assert.Equal(a.Assets.Select(x => x.Id), b.Assets.Select(x => x.Id));
        Assert.Equal(a.WorkOrders.Select(x => (x.Id, x.Status, x.SubmittedAt)), b.WorkOrders.Select(x => (x.Id, x.Status, x.SubmittedAt)));
        Assert.Equal(a.Facts.Select(x => (x.WorkOrderId, x.CompletedAt, x.RepairMinutes)), b.Facts.Select(x => (x.WorkOrderId, x.CompletedAt, x.RepairMinutes)));
    }

    [Fact]
    public void A_different_seed_gives_different_data()
    {
        var a = SeedGenerator.Generate(Small);
        var b = SeedGenerator.Generate(Small with { Seed = 1 });

        Assert.NotEqual(a.Facts.Select(x => x.WorkOrderId), b.Facts.Select(x => x.WorkOrderId));
    }

    [Fact]
    public void Row_counts_follow_the_options_and_keys_are_unique()
    {
        var seed = SeedGenerator.Generate(Small);

        Assert.Equal(40, seed.Assets.Count);
        Assert.Equal(500, seed.WorkOrders.Count);
        Assert.Equal(500, seed.Facts.Count);
        Assert.Equal(40, seed.Assets.Select(a => a.Tag).Distinct().Count());
        Assert.Equal(500, seed.WorkOrders.Select(w => w.Number).Distinct().Count());
        Assert.Equal(500, seed.Facts.Select(f => f.WorkOrderId).Distinct().Count());
    }

    [Fact]
    public void Assets_spread_over_the_four_lines_and_tags_are_valid()
    {
        var seed = SeedGenerator.Generate(Small);

        Assert.Equal(4, seed.Assets.Select(a => a.Line.Id).Distinct().Count());
        Assert.All(seed.Assets, a =>
        {
            Assert.InRange(a.Tag.Length, 3, 20);
            Assert.Matches("^[A-Z0-9]+(-[A-Z0-9]+)*$", a.Tag);
        });
    }

    [Fact]
    public void Facts_stay_inside_the_seeded_window_and_derive_their_numbers_from_the_real_projection()
    {
        var options = Small;
        var seed = SeedGenerator.Generate(options);

        Assert.All(seed.Facts, f =>
        {
            Assert.InRange(f.CompletedAt, options.Start, SeedOptions.End);
            Assert.True(f.StartedAt <= f.CompletedAt);
            Assert.True(f.RepairMinutes >= 0);
            Assert.Equal(f.CompletedAt <= f.DueAt, f.MetSla);
            Assert.NotNull(f.LineId);
        });
        Assert.True(seed.Facts.Select(f => f.CompletedMonth).Distinct().Count() > 12, "Facts should cover most of the 24 months.");
    }

    [Fact]
    public void Work_order_milestones_exist_only_for_statuses_that_reached_them()
    {
        var seed = SeedGenerator.Generate(Small);

        Assert.All(seed.WorkOrders, w =>
        {
            Assert.Equal(w.Status is "Closed", w.ClosedAt is not null);
            Assert.Equal(w.Status is "Completed" or "Closed", w.CompletedAt is not null);
            Assert.Equal(w.Status is "InProgress" or "Completed" or "Closed", w.StartedAt is not null);
        });
        Assert.True(seed.WorkOrders.Select(w => w.Status).Distinct().Count() >= 6, "Statuses should vary.");
    }

    [Fact]
    public void Tables_carry_every_row_and_use_null_for_missing_milestones()
    {
        var seed = SeedGenerator.Generate(Small);

        var workOrders = SeedTables.WorkOrders(seed.WorkOrders);
        var facts = SeedTables.Facts(seed.Facts);
        var assets = SeedTables.Assets(seed.Assets);

        Assert.Equal(500, workOrders.Rows.Count);
        Assert.Equal(500, facts.Rows.Count);
        Assert.Equal(40, assets.Rows.Count);
        Assert.Contains(workOrders.Rows.Cast<System.Data.DataRow>(), r => r.IsNull("ClosedAt"));
    }
}

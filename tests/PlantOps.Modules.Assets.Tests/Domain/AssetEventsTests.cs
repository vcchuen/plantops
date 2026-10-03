using PlantOps.Modules.Assets.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Tests.Domain;

public class AssetEventsTests
{
    private static readonly DateOnly Commissioned = new(2024, 3, 1);
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static Asset NewAsset(ProductionLineId? line = null) => Asset.Register(
        AssetTag.Create("SMT1-PNP-01"),
        "Pick and place",
        "Fuji",
        "NXT III",
        "SN-1",
        new Location(line ?? ProductionLineId.New(), "Station 1"),
        Criticality.B,
        Commissioned);

    // A registered asset whose pending Registered event has been drained, as after a save.
    private static Asset Saved(ProductionLineId? line = null)
    {
        var asset = NewAsset(line);
        asset.ClearDomainEvents();
        return asset;
    }

    [Fact]
    public void Register_raises_AssetRegistered_with_ids_and_initial_values()
    {
        var line = ProductionLineId.New();

        var asset = NewAsset(line);

        var raised = Assert.IsType<AssetRegistered>(Assert.Single(asset.DomainEvents));
        Assert.Equal(asset.Id.Value, raised.AssetId);
        Assert.Equal("SMT1-PNP-01", raised.Tag);
        Assert.Equal("Pick and place", raised.Name);
        Assert.Equal(line.Value, raised.LineId);
        Assert.Equal("Station 1", raised.Station);
        Assert.Equal(Criticality.B, raised.Criticality);
        Assert.Equal(asset.Id.Value.ToString(), asset.AggregateId);
    }

    [Fact]
    public void UpdateDetails_raises_AssetDetailsUpdated_with_the_new_values()
    {
        var asset = Saved();

        asset.UpdateDetails(" Oven ", "Heller", "1913", null);

        var raised = Assert.IsType<AssetDetailsUpdated>(Assert.Single(asset.DomainEvents));
        Assert.Equal(asset.Id.Value, raised.AssetId);
        Assert.Equal("Oven", raised.Name);
        Assert.Equal("Heller", raised.Manufacturer);
        Assert.Equal("1913", raised.Model);
        Assert.Null(raised.SerialNumber);
    }

    [Fact]
    public void Relocate_raises_AssetRelocated_with_from_and_to()
    {
        var from = ProductionLineId.New();
        var to = ProductionLineId.New();
        var asset = Saved(from);

        asset.Relocate(new Location(to, "Station 9"));

        var raised = Assert.IsType<AssetRelocated>(Assert.Single(asset.DomainEvents));
        Assert.Equal(from.Value, raised.FromLineId);
        Assert.Equal("Station 1", raised.FromStation);
        Assert.Equal(to.Value, raised.LineId);
        Assert.Equal("Station 9", raised.Station);
    }

    [Fact]
    public void ChangeCriticality_raises_AssetCriticalityChanged_with_from_and_to()
    {
        var asset = Saved();

        asset.ChangeCriticality(Criticality.A);

        var raised = Assert.IsType<AssetCriticalityChanged>(Assert.Single(asset.DomainEvents));
        Assert.Equal(Criticality.B, raised.From);
        Assert.Equal(Criticality.A, raised.To);
    }

    [Fact]
    public void Decommission_raises_AssetDecommissioned_with_date_and_reason()
    {
        var asset = Saved();
        var on = Commissioned.AddDays(10);

        asset.Decommission(on, " End of life ", Today);

        var raised = Assert.IsType<AssetDecommissioned>(Assert.Single(asset.DomainEvents));
        Assert.Equal(on, raised.On);
        Assert.Equal("End of life", raised.Reason);
    }

    [Fact]
    public void A_rejected_change_raises_nothing()
    {
        var asset = Saved();
        asset.Decommission(Commissioned.AddDays(10), "End of life", Today);
        asset.ClearDomainEvents();

        Assert.Throws<DomainException>(() => asset.ChangeCriticality(Criticality.A));
        Assert.Throws<DomainException>(() => asset.Relocate(new Location(ProductionLineId.New(), "S2")));
        Assert.Throws<DomainException>(() => asset.UpdateDetails("n", "m", "x", null));

        Assert.Empty(asset.DomainEvents);
    }

    [Fact]
    public void A_failed_decommission_raises_nothing()
    {
        var asset = Saved();

        Assert.Throws<DomainException>(() => asset.Decommission(Today.AddDays(1), "Future", Today));

        Assert.Empty(asset.DomainEvents);
    }
}

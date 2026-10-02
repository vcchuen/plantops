using PlantOps.Modules.Assets.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Tests.Domain;

public class AssetTests
{
    private static readonly DateOnly Commissioned = new(2024, 3, 1);
    private static readonly DateOnly Today = new(2026, 10, 3);

    private static Asset NewAsset(string? serialNumber = "SN-1") => Asset.Register(
        AssetTag.Create("SMT1-PNP-01"),
        "Pick and place",
        "Fuji",
        "NXT III",
        serialNumber,
        new Location(ProductionLineId.New(), "Station 1"),
        Criticality.A,
        Commissioned);

    private static Asset DecommissionedAsset()
    {
        var asset = NewAsset();
        asset.Decommission(Commissioned.AddDays(10), "End of life", Today);
        return asset;
    }

    [Fact]
    public void Register_creates_an_in_service_asset_with_the_given_state()
    {
        var location = new Location(ProductionLineId.New(), "Station 1");

        var asset = Asset.Register(AssetTag.Create("smt1-pnp-01"), "  Pick and place ", " Fuji ", " NXT III ", " SN-1 ", location, Criticality.B, Commissioned);

        Assert.NotEqual(default, asset.Id);
        Assert.Equal("SMT1-PNP-01", asset.Tag.Value);
        Assert.Equal("Pick and place", asset.Name);
        Assert.Equal("Fuji", asset.Manufacturer);
        Assert.Equal("NXT III", asset.Model);
        Assert.Equal("SN-1", asset.SerialNumber);
        Assert.Equal(location, asset.Location);
        Assert.Equal(Criticality.B, asset.Criticality);
        Assert.Equal(AssetStatus.InService, asset.Status);
        Assert.Equal(Commissioned, asset.CommissionedOn);
        Assert.Null(asset.DecommissionedOn);
        Assert.Null(asset.DecommissionReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_stores_a_blank_serial_number_as_null(string? serial)
    {
        Assert.Null(NewAsset(serial).SerialNumber);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("manufacturer")]
    [InlineData("model")]
    [InlineData("serial")]
    public void Register_rejects_text_longer_than_100_characters(string field)
    {
        var tooLong = new string('x', 101);
        var location = new Location(ProductionLineId.New(), "S1");

        Assert.Throws<DomainException>(() => Asset.Register(
            AssetTag.Create("ABC"),
            field == "name" ? tooLong : "n",
            field == "manufacturer" ? tooLong : "m",
            field == "model" ? tooLong : "x",
            field == "serial" ? tooLong : null,
            location,
            Criticality.C,
            Commissioned));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("manufacturer")]
    [InlineData("model")]
    public void Register_requires_name_manufacturer_and_model(string missing)
    {
        var location = new Location(ProductionLineId.New(), "S1");

        Assert.Throws<DomainException>(() => Asset.Register(
            AssetTag.Create("ABC"),
            missing == "name" ? " " : "n",
            missing == "manufacturer" ? " " : "m",
            missing == "model" ? " " : "x",
            null,
            location,
            Criticality.C,
            Commissioned));
    }

    [Fact]
    public void Register_rejects_an_undefined_criticality()
    {
        Assert.Throws<DomainException>(() => Asset.Register(
            AssetTag.Create("ABC"), "n", "m", "x", null, new Location(ProductionLineId.New(), "S1"), (Criticality)42, Commissioned));
    }

    [Fact]
    public void UpdateDetails_replaces_the_descriptive_fields()
    {
        var asset = NewAsset();

        asset.UpdateDetails(" Reflow oven ", "Heller", "1913", null);

        Assert.Equal("Reflow oven", asset.Name);
        Assert.Equal("Heller", asset.Manufacturer);
        Assert.Equal("1913", asset.Model);
        Assert.Null(asset.SerialNumber);
    }

    [Fact]
    public void UpdateDetails_validates_like_Register()
    {
        Assert.Throws<DomainException>(() => NewAsset().UpdateDetails("", "m", "x", null));
    }

    [Fact]
    public void Relocate_changes_the_location()
    {
        var asset = NewAsset();
        var target = new Location(ProductionLineId.New(), "Station 9");

        asset.Relocate(target);

        Assert.Equal(target, asset.Location);
    }

    [Fact]
    public void ChangeCriticality_changes_the_criticality()
    {
        var asset = NewAsset();

        asset.ChangeCriticality(Criticality.C);

        Assert.Equal(Criticality.C, asset.Criticality);
    }

    [Fact]
    public void Decommission_marks_the_asset_decommissioned_with_date_and_trimmed_reason()
    {
        var asset = NewAsset();

        asset.Decommission(new DateOnly(2026, 9, 30), "  Beyond economic repair ", Today);

        Assert.Equal(AssetStatus.Decommissioned, asset.Status);
        Assert.Equal(new DateOnly(2026, 9, 30), asset.DecommissionedOn);
        Assert.Equal("Beyond economic repair", asset.DecommissionReason);
    }

    [Fact]
    public void Decommission_on_the_commissioning_date_is_allowed()
    {
        var asset = NewAsset();

        asset.Decommission(Commissioned, "Dead on arrival", Today);

        Assert.Equal(Commissioned, asset.DecommissionedOn);
    }

    [Fact]
    public void Decommission_today_is_allowed()
    {
        var asset = NewAsset();

        asset.Decommission(Today, "Scrapped", Today);

        Assert.Equal(Today, asset.DecommissionedOn);
    }

    [Fact]
    public void Decommission_before_commissioning_is_rejected()
    {
        var asset = NewAsset();

        var ex = Assert.Throws<DomainException>(() => asset.Decommission(Commissioned.AddDays(-1), "Scrapped", Today));

        Assert.Contains("before the commissioning date", ex.Message);
        Assert.Equal(AssetStatus.InService, asset.Status);
    }

    [Fact]
    public void Decommission_in_the_future_is_rejected()
    {
        var asset = NewAsset();

        var ex = Assert.Throws<DomainException>(() => asset.Decommission(Today.AddDays(1), "Scrapped", Today));

        Assert.Contains("future", ex.Message);
        Assert.Equal(AssetStatus.InService, asset.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decommission_requires_a_reason(string? reason)
    {
        var asset = NewAsset();

        Assert.Throws<DomainException>(() => asset.Decommission(Today, reason!, Today));
        Assert.Equal(AssetStatus.InService, asset.Status);
    }

    [Fact]
    public void Decommission_reason_longer_than_500_characters_is_rejected()
    {
        Assert.Throws<DomainException>(() => NewAsset().Decommission(Today, new string('x', 501), Today));
    }

    [Fact]
    public void Decommissioned_asset_cannot_be_decommissioned_again()
    {
        var asset = DecommissionedAsset();

        Assert.Throws<DomainException>(() => asset.Decommission(Today, "Again", Today));
    }

    [Fact]
    public void Decommissioned_asset_cannot_be_edited()
    {
        var asset = DecommissionedAsset();

        var ex = Assert.Throws<DomainException>(() => asset.UpdateDetails("n", "m", "x", null));

        Assert.Contains("decommissioned", ex.Message);
    }

    [Fact]
    public void Decommissioned_asset_cannot_be_relocated()
    {
        var asset = DecommissionedAsset();
        var original = asset.Location;

        Assert.Throws<DomainException>(() => asset.Relocate(new Location(ProductionLineId.New(), "S2")));
        Assert.Equal(original, asset.Location);
    }

    [Fact]
    public void Decommissioned_asset_cannot_change_criticality()
    {
        var asset = DecommissionedAsset();

        Assert.Throws<DomainException>(() => asset.ChangeCriticality(Criticality.C));
        Assert.Equal(Criticality.A, asset.Criticality);
    }
}

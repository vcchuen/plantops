using PlantOps.Modules.Assets.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Tests.Domain;

public class LocationTests
{
    private static readonly ProductionLineId Line = ProductionLineId.New();

    [Fact]
    public void Station_is_trimmed()
    {
        var location = new Location(Line, "  Pick & Place 2 ");

        Assert.Equal("Pick & Place 2", location.Station);
        Assert.Equal(Line, location.LineId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Station_is_required(string? station)
    {
        Assert.Throws<DomainException>(() => new Location(Line, station!));
    }

    [Fact]
    public void Station_longer_than_50_characters_is_rejected()
    {
        Assert.Throws<DomainException>(() => new Location(Line, new string('x', 51)));
    }

    [Fact]
    public void Station_of_exactly_50_characters_is_accepted()
    {
        Assert.Equal(50, new Location(Line, new string('x', 50)).Station.Length);
    }

    [Fact]
    public void Locations_are_compared_by_value()
    {
        Assert.Equal(new Location(Line, "S1"), new Location(Line, " S1 "));
        Assert.NotEqual(new Location(Line, "S1"), new Location(Line, "S2"));
    }
}

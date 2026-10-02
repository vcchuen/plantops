using PlantOps.Modules.Assets.Domain;
using PlantOps.SharedKernel;

namespace PlantOps.Modules.Assets.Tests.Domain;

public class AssetTagTests
{
    [Theory]
    [InlineData("SMT1-PNP-01", "SMT1-PNP-01")]
    [InlineData("  smt1-pnp-01  ", "SMT1-PNP-01")]
    [InlineData("abc", "ABC")]
    [InlineData("A1B", "A1B")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    public void Create_trims_and_upper_cases_valid_tags(string input, string expected)
    {
        var tag = AssetTag.Create(input);

        Assert.Equal(expected, tag.Value);
        Assert.Equal(expected, tag.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_tags(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => AssetTag.Create(input));

        Assert.Contains("required", ex.Message);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("123456789012345678901")]
    public void Create_rejects_tags_outside_3_to_20_characters(string input)
    {
        var ex = Assert.Throws<DomainException>(() => AssetTag.Create(input));

        Assert.Contains("3-20", ex.Message);
    }

    [Theory]
    [InlineData("SMT--1")]
    [InlineData("-SMT1")]
    [InlineData("SMT1-")]
    [InlineData("SMT_1")]
    [InlineData("SMT 1")]
    [InlineData("SMT/1")]
    [InlineData("SMTÉ1")]
    public void Create_rejects_tags_with_illegal_characters_or_hyphen_placement(string input)
    {
        Assert.Throws<DomainException>(() => AssetTag.Create(input));
    }

    [Fact]
    public void Tags_with_the_same_normalised_value_are_equal()
    {
        Assert.Equal(AssetTag.Create("smt-1"), AssetTag.Create(" SMT-1 "));
    }
}

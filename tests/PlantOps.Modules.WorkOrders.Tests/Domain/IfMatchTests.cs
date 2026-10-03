using Microsoft.AspNetCore.Http;
using PlantOps.Modules.WorkOrders.Endpoints;

namespace PlantOps.Modules.WorkOrders.Tests.Domain;

public class IfMatchTests
{
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0x07, 0xD1];

    private static (byte[]? Version, int? Status) Read(params string[] headerValues)
    {
        var context = new DefaultHttpContext();
        if (headerValues.Length > 0)
        {
            context.Request.Headers.IfMatch = headerValues;
        }

        var (version, problem) = IfMatch.Read(context.Request);
        return (version, problem?.StatusCode);
    }

    [Fact]
    public void Round_trips_the_etag_the_api_sends()
    {
        var (version, status) = Read(IfMatch.ETag(Version));

        Assert.Null(status);
        Assert.Equal(Version, version);
    }

    [Fact]
    public void Missing_header_is_428()
    {
        Assert.Equal(StatusCodes.Status428PreconditionRequired, Read().Status);
    }

    [Fact]
    public void Blank_header_is_428()
    {
        Assert.Equal(StatusCodes.Status428PreconditionRequired, Read(" ").Status);
    }

    [Theory]
    [InlineData("AAAAAAAAB9E=")] // not quoted
    [InlineData("W/\"AAAAAAAAB9E=\"")] // weak validator
    [InlineData("*")]
    [InlineData("\"not base64!\"")]
    [InlineData("\"AAAA\"")] // valid base64, wrong length for a rowversion
    [InlineData("\"AAAAAAAAB9E=\", \"AAAAAAAAB9E=\"")]
    [InlineData("\"\"")]
    public void Malformed_header_is_400(string value)
    {
        Assert.Equal(StatusCodes.Status400BadRequest, Read(value).Status);
    }

    [Fact]
    public void Two_header_lines_are_400()
    {
        Assert.Equal(StatusCodes.Status400BadRequest, Read(IfMatch.ETag(Version), IfMatch.ETag(Version)).Status);
    }
}

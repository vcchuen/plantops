using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace PlantOps.Modules.WorkOrders.Endpoints;

// ETag = the quoted base64 of the 8-byte SQL Server rowversion (ADR-0008).
internal static class IfMatch
{
    private const int RowVersionLength = 8;

    public static string ETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    /// <summary>Missing header: 428 (clients cannot opt out by forgetting). Unparseable: 400.</summary>
    public static (byte[]? Version, ProblemHttpResult? Problem) Read(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0]))
        {
            return (null, TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Precondition required",
                detail: "Send the ETag you last read in an If-Match header."));
        }

        // One strong, quoted validator only. A list ("a", "b"), a weak one (W/"a") and "*" all fail the checks below.
        var raw = values[0]!.Trim();
        if (values.Count == 1
            && raw.Length > 2
            && raw[0] == '"'
            && raw[^1] == '"'
            && TryDecode(raw[1..^1], out var version))
        {
            return (version, null);
        }

        return (null, TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad request",
            detail: "If-Match must be a single quoted ETag, exactly as returned by the API."));
    }

    private static bool TryDecode(string base64, out byte[] version)
    {
        var buffer = new byte[RowVersionLength];
        // A destination of exactly 8 bytes rejects anything that decodes to a different length.
        var ok = Convert.TryFromBase64String(base64, buffer, out var written) && written == RowVersionLength;
        version = buffer;
        return ok;
    }
}

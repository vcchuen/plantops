using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;

namespace PlantOps.Modules.Identity;

// The built-in MapJsonKey/MapUniqueJsonKey copy a JSON array as ONE claim whose value is the raw text
// '["supervisor","admin"]', so IsInRole("admin") would be false. This emits one claim per element.
// It skips values already present because the same roles also arrive via the id_token.
internal sealed class JsonArrayClaimAction(string claimType, string jsonKey) : ClaimAction(claimType, ClaimValueTypes.String)
{
    public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
    {
        if (!userData.TryGetProperty(jsonKey, out var value))
        {
            return;
        }

        // A single string is accepted too: some providers send one role as a scalar.
        var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } text)
            {
                continue;
            }

            if (!identity.HasClaim(c => c.Type == ClaimType && c.Value == text))
            {
                identity.AddClaim(new Claim(ClaimType, text, ValueType, issuer));
            }
        }
    }
}

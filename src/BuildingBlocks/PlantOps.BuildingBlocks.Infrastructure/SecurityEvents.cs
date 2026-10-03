using Microsoft.Extensions.Logging;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// The security-relevant log events, with fixed <see cref="EventId"/>s so an alert rule (M9: Application Insights)
/// can key on the id instead of grepping message text. Ids are a contract: never renumber, only append.
/// Rules for every message: nothing beyond the signed-in user's subject id (no names, emails, IPs), and never a
/// token, cookie or request body.
/// </summary>
public static partial class SecurityEvents
{
    /// <summary>Logger category shared by every security event, so one filter selects them all.</summary>
    public const string Category = "PlantOps.Security";

    public const int SignInSucceededId = 1001;
    public const int SignInFailedId = 1002;
    public const int AccessDeniedId = 1003;
    public const int CsrfRejectedId = 1004;
    public const int RateLimitedId = 1005;
    public const int OutboxMessageParkedId = 1006;

    [LoggerMessage(EventId = SignInSucceededId, Level = LogLevel.Information, Message = "Sign-in succeeded for subject {Subject}")]
    public static partial void SignInSucceeded(ILogger logger, string subject);

    // The reason is an exception TYPE name or a fixed phrase, never the exception message: remote-failure messages can
    // echo parts of the IdP response.
    [LoggerMessage(EventId = SignInFailedId, Level = LogLevel.Warning, Message = "Sign-in failed: {Reason}")]
    public static partial void SignInFailed(ILogger logger, string reason);

    // The path is the route, not the query string (which may carry anything).
    [LoggerMessage(EventId = AccessDeniedId, Level = LogLevel.Warning, Message = "Access denied to {Path} for {User}")]
    public static partial void AccessDenied(ILogger logger, string path, string user);

    [LoggerMessage(EventId = CsrfRejectedId, Level = LogLevel.Warning, Message = "Request to {Path} rejected: missing CSRF header")]
    public static partial void CsrfRejected(ILogger logger, string path);

    [LoggerMessage(EventId = RateLimitedId, Level = LogLevel.Warning, Message = "Rate limit '{Policy}' rejected a request (partition {Partition})")]
    public static partial void RateLimited(ILogger logger, string policy, string partition);

    [LoggerMessage(EventId = OutboxMessageParkedId, Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) parked and needs an operator")]
    public static partial void OutboxMessageParked(ILogger logger, Guid messageId, string type);
}

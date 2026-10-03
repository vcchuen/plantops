using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlantOps.SharedKernel;

namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// Re-runs a whole command after an optimistic-concurrency conflict. The policy differs from the work-order
/// endpoints (ADR-0008): there a human decided from stale data, so we answer 412 and make them look again. Here the
/// decision belongs to the server ("is there stock?"), so the server simply re-decides on fresh data.
/// </summary>
public static class ConcurrencyRetry
{
    public const int DefaultAttempts = 3;

    /// <param name="operation">
    /// Loads, decides and saves using only the DbContext it resolves from the given provider. Each attempt gets a fresh
    /// scope, so no stale tracked entity (with its old RowVersion) leaks into the next attempt.
    /// </param>
    public static async Task<T> RunAsync<T>(
        IServiceScopeFactory scopes,
        Func<IServiceProvider, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        int maxAttempts = DefaultAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                return await operation(scope.ServiceProvider, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Lost the race; loop and try again from a fresh read.
            }
        }

        throw new ConflictException("The request conflicted with other concurrent updates. Please try again.");
    }
}

namespace PlantOps.Modules.Identity.Contracts;

/// <summary>
/// The people who have signed in at least once (just-in-time provisioning, design 04 Decision 5). Other modules
/// use it to validate "is this a technician?" and to snapshot display names.
/// </summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(string id, CancellationToken cancellationToken);

    /// <param name="role">One of <see cref="Roles"/>.</param>
    Task<IReadOnlyList<UserSummary>> ListByRoleAsync(string role, CancellationToken cancellationToken);
}

public sealed record UserSummary(string Id, string Name, string? Email, IReadOnlyList<string> Roles);

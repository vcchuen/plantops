namespace PlantOps.BuildingBlocks.Infrastructure;

/// <summary>
/// The signed-in user as the persistence and audit layers see it. Lives here, not in Identity.Contracts, because
/// the audit interceptor needs it and a building block must not depend on a module. Identity implements it.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The IdP's stable subject id; null when no user is signed in (background work).</summary>
    string? Id { get; }

    string? Name { get; }
}

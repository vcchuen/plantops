namespace PlantOps.SharedKernel;

/// <summary>
/// Who performed a command. Passed into aggregate methods (like the current time) so the domain never reads
/// HttpContext or a clock. Name is a snapshot: it stays readable on old records even if the user is renamed.
/// </summary>
public sealed record Actor
{
    public Actor(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DomainException("Actor id is required.");
        }

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
    }

    public string Id { get; }

    public string Name { get; }
}

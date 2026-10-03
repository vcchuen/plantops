namespace PlantOps.SharedKernel;

/// <summary>
/// Base class for aggregates that record what happened to them. Deliberately EF-free: a persistence interceptor
/// (BuildingBlocks.Infrastructure) drains <see cref="DomainEvents"/> when the aggregate is saved.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];

    /// <summary>Stable text form of the aggregate's key; stored on audit rows so history can be looked up by it.</summary>
    public abstract string AggregateId { get; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _events.Add(domainEvent);
    }
}

namespace ElectCrm.Domain.Common;

public abstract record DomainEvent
{
    protected DomainEvent()
    {
        EventId = Guid.CreateVersion7();
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid EventId { get; }
    public DateTimeOffset OccurredAt { get; }
}

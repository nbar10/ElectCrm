namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserUpdatedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    Guid UpdatedByUserId,
    DateTimeOffset UpdatedAt) : DomainEvent;

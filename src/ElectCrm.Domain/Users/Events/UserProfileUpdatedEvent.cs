namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserProfileUpdatedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    DateTimeOffset UpdatedAt) : DomainEvent;

namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserReactivatedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    Guid ReactivatedByUserId,
    DateTimeOffset ReactivatedAt) : DomainEvent;

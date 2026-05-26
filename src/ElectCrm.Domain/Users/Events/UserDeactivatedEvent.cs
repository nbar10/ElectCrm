namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserDeactivatedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    string? Reason,
    Guid DeactivatedByUserId,
    DateTimeOffset DeactivatedAt) : DomainEvent;

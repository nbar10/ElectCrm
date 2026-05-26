namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserPasswordChangedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    DateTimeOffset ChangedAt) : DomainEvent;

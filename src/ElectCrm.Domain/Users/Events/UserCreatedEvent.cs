namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserCreatedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    string DisplayName,
    string Email,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt) : DomainEvent;

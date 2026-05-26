namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserRoleAssignedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    string Role,
    Guid GrantedByUserId,
    DateTimeOffset GrantedAt,
    string? Reason) : DomainEvent;

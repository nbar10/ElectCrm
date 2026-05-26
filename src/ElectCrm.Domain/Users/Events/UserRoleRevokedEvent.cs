namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserRoleRevokedEvent(
    Guid UserId,
    Guid AgencyBrandId,
    string Role,
    Guid RevokedByUserId,
    DateTimeOffset RevokedAt,
    string? Reason) : DomainEvent;

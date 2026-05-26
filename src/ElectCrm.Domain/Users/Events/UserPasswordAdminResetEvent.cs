namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;

public sealed record UserPasswordAdminResetEvent(
    Guid UserId,
    Guid AgencyBrandId,
    Guid ResetByUserId,
    DateTimeOffset ResetAt) : DomainEvent;

namespace ElectCrm.Domain.Users.Events;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;

public sealed record UserInvitedEvent(
    Guid InviteId,
    TenantId TenantId,
    string InvitedEmail,
    Guid Token) : DomainEvent;

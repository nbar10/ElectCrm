namespace ElectCrm.Application.Features.Users;

public sealed record RevokeRoleCommand(
    string RoleClaimValue,
    string? Reason);

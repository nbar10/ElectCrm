namespace ElectCrm.Application.Features.Users;

public sealed record AssignRoleCommand(
    string Role,
    string RoleScope,
    Guid ScopeId,
    string? Reason);

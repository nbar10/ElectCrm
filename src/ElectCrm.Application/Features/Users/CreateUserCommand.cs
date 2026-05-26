namespace ElectCrm.Application.Features.Users;

public sealed record CreateUserCommand(
    string DisplayName,
    string Email,
    string? JobTitle,
    Guid? PrimaryBranchId,
    string InitialRole,
    Guid InitialRoleScopeId);

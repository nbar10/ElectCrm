namespace ElectCrm.Application.Features.Users;

public sealed record UpdateUserCommand(
    string DisplayName,
    string? JobTitle,
    Guid? PrimaryBranchId,
    string Email);

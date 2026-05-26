namespace ElectCrm.Application.Features.Users;

public sealed record UserProfileDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string? JobTitle,
    string? PhoneNumber,
    string? PrimaryBranchName,
    string AgencyBrandName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool RequirePasswordChange);

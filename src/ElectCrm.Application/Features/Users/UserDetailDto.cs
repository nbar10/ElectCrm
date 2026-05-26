namespace ElectCrm.Application.Features.Users;

public sealed record UserDetailDto(
    Guid UserId,
    Guid DomainUserId,
    string DisplayName,
    string Email,
    string? JobTitle,
    string? PhoneNumber,
    Guid? PrimaryBranchId,
    string? PrimaryBranchName,
    Guid AgencyBrandId,
    string AgencyBrandName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool RequirePasswordChange,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastModifiedByDisplayName);

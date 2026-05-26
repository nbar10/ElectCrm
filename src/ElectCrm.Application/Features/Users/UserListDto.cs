namespace ElectCrm.Application.Features.Users;

public sealed record UserListDto(
    Guid UserId,
    Guid DomainUserId,
    string DisplayName,
    string Email,
    string? JobTitle,
    Guid? PrimaryBranchId,
    string? PrimaryBranchName,
    Guid AgencyBrandId,
    string AgencyBrandName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTimeOffset CreatedAt);

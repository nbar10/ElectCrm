namespace ElectCrm.Application.Features.Users;

public sealed record UserSearchQuery(
    string? SearchTerm,
    Guid? AgencyBrandId,
    Guid? BranchId,
    bool? IsActive,
    string? Role,
    int Page,
    int PageSize);

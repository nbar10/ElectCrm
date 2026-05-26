namespace ElectCrm.Application.Common;

public interface ICurrentUserContext
{
    Guid CurrentUserId { get; }
    string CurrentUserDisplayName { get; }
    bool IsGroupAdmin { get; }
    bool IsBrandAdmin { get; }
    Guid? BrandAdminScope { get; }  // null if not BrandAdmin
}

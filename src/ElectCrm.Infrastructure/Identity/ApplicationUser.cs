namespace ElectCrm.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>
    /// FK to the Domain User entity. Set at registration; used to load domain profile.
    /// </summary>
    public Guid DomainUserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public Guid? PrimaryBranchId { get; set; }
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequirePasswordChange { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LastModifiedById { get; set; }
}

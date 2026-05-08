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
}

namespace ElectCrm.Infrastructure.Identity;

using Microsoft.AspNetCore.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
        Id = Guid.CreateVersion7();
    }
}

namespace ElectCrm.Presentation.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed class UserAdminRequirementHandler : AuthorizationHandler<UserAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        UserAdminRequirement requirement)
    {
        var claims = context.User.FindAll(ElectClaimTypes.Role);
        if (claims.Any(c => c.Value.StartsWith("GroupAdmin:") || c.Value.StartsWith("BrandAdmin:")))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

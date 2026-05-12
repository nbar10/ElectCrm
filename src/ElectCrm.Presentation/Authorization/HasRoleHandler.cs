namespace ElectCrm.Presentation.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed class HasRoleHandler : AuthorizationHandler<HasRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HasRoleRequirement requirement)
    {
        // Claim value format: "RoleName:Scope:ScopeId" e.g. "BrandAdmin:Brand:3fa8..."
        // Split on ':' and compare the first segment with Ordinal to prevent prefix-collision
        // between role names that share a prefix (e.g. "BrandAdmin" vs "BrandAdminPlus").
        var hasRole = context.User.Claims.Any(c =>
            c.Type == ElectClaimTypes.Role &&
            c.Value.Split(':')[0].Equals(requirement.RoleName, StringComparison.Ordinal));

        if (hasRole)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

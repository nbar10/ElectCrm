namespace ElectCrm.Presentation.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed class HasRoleHandler : AuthorizationHandler<HasRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        HasRoleRequirement requirement)
    {
        // Claim value format: "RoleName:Scope:ScopeId" e.g. "BrandAdmin:Brand:3fa8..."
        var hasRole = context.User.Claims.Any(c =>
            c.Type == ElectClaimTypes.Role &&
            c.Value.StartsWith(requirement.RoleName + ":", StringComparison.OrdinalIgnoreCase));

        if (hasRole)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

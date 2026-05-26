namespace ElectCrm.Presentation.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed class WorkerRequirementHandler : AuthorizationHandler<WorkerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkerRequirement requirement)
    {
        // Succeeds ONLY for claims in format "Worker:Brand:{BrandId}".
        // Staff role claims (Consultant, BrandAdmin, GroupAdmin) never start with "Worker".
        // Mutual exclusion: a user with only staff claims will not satisfy this requirement.
        var isWorker = context.User.Claims.Any(c =>
            c.Type == ElectClaimTypes.Role &&
            c.Value.StartsWith("Worker:", StringComparison.Ordinal));

        if (isWorker)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

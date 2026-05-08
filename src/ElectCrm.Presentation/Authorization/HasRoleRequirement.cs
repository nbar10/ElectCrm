namespace ElectCrm.Presentation.Authorization;

using Microsoft.AspNetCore.Authorization;

public sealed record HasRoleRequirement(string RoleName) : IAuthorizationRequirement;

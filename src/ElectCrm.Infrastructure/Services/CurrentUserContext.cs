namespace ElectCrm.Infrastructure.Services;

using System.Security.Claims;
using ElectCrm.Application.Common;
using Microsoft.AspNetCore.Http;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HttpContext is available.");

    public Guid CurrentUserId
    {
        get
        {
            var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("The current user does not have a valid NameIdentifier claim.");
        }
    }

    public string CurrentUserDisplayName
    {
        get
        {
            return User.FindFirst("DisplayName")?.Value
                ?? User.FindFirst(ClaimTypes.Email)?.Value
                ?? string.Empty;
        }
    }

    public bool IsGroupAdmin =>
        User.FindAll("elect_role").Any(c => c.Value.StartsWith("GroupAdmin:"));

    public bool IsBrandAdmin =>
        User.FindAll("elect_role").Any(c => c.Value.StartsWith("BrandAdmin:"));

    public Guid? BrandAdminScope
    {
        get
        {
            var claim = User.FindAll("elect_role")
                .FirstOrDefault(c => c.Value.StartsWith("BrandAdmin:Brand:"));

            if (claim is null)
                return null;

            // Claim value format: "BrandAdmin:Brand:{Guid}"
            var segments = claim.Value.Split(':');
            return segments.Length >= 3 && Guid.TryParse(segments[2], out var brandId)
                ? brandId
                : null;
        }
    }
}

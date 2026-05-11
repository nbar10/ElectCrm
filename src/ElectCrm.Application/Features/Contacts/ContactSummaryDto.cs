namespace ElectCrm.Application.Features.Contacts;

using ElectCrm.Domain.Contacts;

public sealed record ContactSummaryDto(
    Guid Id,
    string FullName,
    string? RoleTitle,
    string? Email,
    string? Phone,
    ContactStatus Status);

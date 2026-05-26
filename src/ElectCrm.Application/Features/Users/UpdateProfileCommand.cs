namespace ElectCrm.Application.Features.Users;

public sealed record UpdateProfileCommand(
    string DisplayName,
    string? JobTitle,
    string? PhoneNumber,
    string Email);

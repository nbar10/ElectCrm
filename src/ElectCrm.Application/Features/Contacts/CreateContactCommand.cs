namespace ElectCrm.Application.Features.Contacts;

using System.ComponentModel.DataAnnotations;
using ElectCrm.Domain.Contacts;

public sealed record CreateContactCommand
{
    [Required]
    public Guid ClientId { get; init; }

    [Required, MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? RoleTitle { get; init; }

    [MaxLength(200), EmailAddress]
    public string? Email { get; init; }

    [MaxLength(30)]
    public string? Phone { get; init; }

    public List<ContactCategory> PrimaryForCategories { get; init; } = [];

    public ChannelPrefsCommand? CommunicationPreferences { get; init; }
}

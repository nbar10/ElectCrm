namespace ElectCrm.Application.Features.Contacts;

using ElectCrm.Domain.Contacts;

public sealed record ContactDto(
    Guid Id,
    Guid ClientId,
    Guid AgencyBrandId,
    string FullName,
    string? RoleTitle,
    string? Email,
    string? Phone,
    IReadOnlyList<ContactCategory> PrimaryForCategories,
    ChannelPrefsDto? CommunicationPreferences,
    ContactStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ContactDto FromEntity(Contact contact) => new(
        contact.Id,
        contact.ClientId,
        contact.AgencyBrandId,
        contact.FullName,
        contact.RoleTitle,
        contact.Email,
        contact.Phone,
        contact.PrimaryForCategories,
        contact.CommunicationPreferences is null ? null : new ChannelPrefsDto(
            contact.CommunicationPreferences.Channel,
            contact.CommunicationPreferences.PreferredDays,
            contact.CommunicationPreferences.QuietHoursStart,
            contact.CommunicationPreferences.QuietHoursEnd),
        contact.Status,
        contact.CreatedAt,
        contact.UpdatedAt);
}

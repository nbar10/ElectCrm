namespace ElectCrm.Application.Features.Contacts;

using ElectCrm.Domain.Contacts;

public sealed record ChannelPrefsDto(
    PreferredChannel Channel,
    string? PreferredDays,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd);

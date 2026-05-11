namespace ElectCrm.Application.Features.Contacts;

using ElectCrm.Domain.Contacts;

public sealed record ChannelPrefsCommand(
    PreferredChannel Channel,
    string? PreferredDays = null,
    TimeOnly? QuietHoursStart = null,
    TimeOnly? QuietHoursEnd = null);

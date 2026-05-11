namespace ElectCrm.Domain.Contacts;

public sealed class ChannelPrefs
{
    private ChannelPrefs()
    {
    }

    public ChannelPrefs(
        PreferredChannel channel,
        DayOfWeek[]? preferredDays = null,
        TimeOnly? quietHoursStart = null,
        TimeOnly? quietHoursEnd = null)
    {
        Channel = channel;
        PreferredDays = preferredDays is { Length: > 0 }
            ? string.Join(",", preferredDays.Select(d => d.ToString()))
            : null;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
    }

    public PreferredChannel Channel { get; private set; }
    public string? PreferredDays { get; private set; }
    public TimeOnly? QuietHoursStart { get; private set; }
    public TimeOnly? QuietHoursEnd { get; private set; }

    public static DayOfWeek[] Parse(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return [];

        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => Enum.TryParse<DayOfWeek>(s, out _))
            .Select(s => Enum.Parse<DayOfWeek>(s))
            .ToArray();
    }
}

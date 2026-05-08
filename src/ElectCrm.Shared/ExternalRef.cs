namespace ElectCrm.Shared;

public static class ExternalRef
{
    public static string Generate(string entityPrefix, Guid id)
    {
        var year = DateTime.UtcNow.Year;
        var shortId = id.ToString("N")[..4].ToUpperInvariant();
        return $"{entityPrefix}-{year}-{shortId}";
    }
}

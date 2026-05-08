namespace ElectCrm.Domain.Common.ValueObjects;

public sealed class GeoArea
{
    private GeoArea()
    {
        PostcodePrefixes = [];
    }

    private GeoArea(IReadOnlyList<string> postcodePrefixes)
    {
        PostcodePrefixes = postcodePrefixes;
    }

    public IReadOnlyList<string> PostcodePrefixes { get; private set; }

    public static GeoArea Empty => new([]);

    public static GeoArea FromPrefixes(IEnumerable<string> prefixes)
    {
        var normalised = prefixes
            .Select(p => p.Trim().ToUpperInvariant())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Order()
            .ToList();

        return new GeoArea(normalised);
    }

    public bool CoversPostcode(string postcode)
    {
        var normalised = postcode.Trim().ToUpperInvariant();
        return PostcodePrefixes.Any(prefix => normalised.StartsWith(prefix, StringComparison.Ordinal));
    }
}

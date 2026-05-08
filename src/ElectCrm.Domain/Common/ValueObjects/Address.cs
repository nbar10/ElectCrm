namespace ElectCrm.Domain.Common.ValueObjects;

public sealed class Address
{
    private Address()
    {
        Line1 = string.Empty;
        City = string.Empty;
        Postcode = string.Empty;
        Country = "GB";
    }

    public Address(
        string line1,
        string city,
        string postcode,
        string? line2 = null,
        string? county = null,
        string country = "GB")
    {
        if (string.IsNullOrWhiteSpace(line1))
            throw new ArgumentException("Address line 1 is required.", nameof(line1));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(postcode))
            throw new ArgumentException("Postcode is required.", nameof(postcode));

        Line1 = line1.Trim();
        Line2 = line2?.Trim();
        City = city.Trim();
        County = county?.Trim();
        Postcode = postcode.Trim().ToUpperInvariant();
        Country = string.IsNullOrWhiteSpace(country) ? "GB" : country.Trim().ToUpperInvariant();
    }

    public string Line1 { get; private set; }
    public string? Line2 { get; private set; }
    public string City { get; private set; }
    public string? County { get; private set; }
    public string Postcode { get; private set; }
    public string Country { get; private set; }
}

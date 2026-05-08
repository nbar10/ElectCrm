namespace ElectCrm.Infrastructure.Persistence.Converters;

using System.Text.Json;
using ElectCrm.Domain.Common.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public sealed class GeoAreaConverter : ValueConverter<GeoArea, string>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public GeoAreaConverter() : base(
        geoArea => JsonSerializer.Serialize(geoArea.PostcodePrefixes, JsonOptions),
        json => GeoArea.FromPrefixes(
            JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>()))
    {
    }
}

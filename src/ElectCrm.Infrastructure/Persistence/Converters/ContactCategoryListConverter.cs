namespace ElectCrm.Infrastructure.Persistence.Converters;

using System.Text.Json;
using ElectCrm.Domain.Contacts;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public sealed class ContactCategoryListConverter : ValueConverter<IReadOnlyList<ContactCategory>, string>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ContactCategoryListConverter() : base(
        list => JsonSerializer.Serialize(list.Select(c => c.ToString()).ToList(), JsonOptions),
        json => (IReadOnlyList<ContactCategory>)
            (JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>())
            .Select(s => Enum.Parse<ContactCategory>(s))
            .ToList())
    {
    }
}

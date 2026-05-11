namespace ElectCrm.Application.Features.Persons;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class PersonNameNormaliser
{
    private static readonly Regex MultipleSpaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonAlphaOrSpace = new(@"[^\w\s]", RegexOptions.Compiled);

    public static string Normalise(string displayName)
    {
        var lower = displayName.ToLowerInvariant().Trim();

        var collapsed = MultipleSpaces.Replace(lower, " ");

        var noPunct = NonAlphaOrSpace.Replace(collapsed, string.Empty);

        var decomposed = noPunct.Normalize(NormalizationForm.FormD);

        var stripped = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                stripped.Append(c);
        }

        return stripped.ToString().Trim();
    }
}

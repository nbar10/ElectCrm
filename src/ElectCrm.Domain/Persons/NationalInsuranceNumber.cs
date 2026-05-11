namespace ElectCrm.Domain.Persons;

using System.Text.RegularExpressions;
using ElectCrm.Shared;

public sealed class NationalInsuranceNumber
{
    public const string HashAlgorithmVersion = "SHA256-v1";

    private static readonly Regex ValidFormat =
        new(@"^[A-CEGHJ-PR-TW-Z]{2}\d{6}[A-D]$", RegexOptions.Compiled);

    private NationalInsuranceNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<NationalInsuranceNumber> TryCreate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result<NationalInsuranceNumber>.Failure(Error.Validation("National Insurance number is required."));

        var normalised = Normalise(raw);

        if (!ValidFormat.IsMatch(normalised))
            return Result<NationalInsuranceNumber>.Failure(
                Error.Validation("National Insurance number is not valid. Expected format: two letters, six digits, one letter A–D (e.g. AB123456C)."));

        return Result<NationalInsuranceNumber>.Success(new NationalInsuranceNumber(normalised));
    }

    private static string Normalise(string raw) =>
        raw.Replace(" ", string.Empty).ToUpperInvariant();
}

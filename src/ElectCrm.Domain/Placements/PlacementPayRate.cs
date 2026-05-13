namespace ElectCrm.Domain.Placements;

using ElectCrm.Domain.Vacancies;
using ElectCrm.Shared;

public sealed class PlacementPayRate
{
    // EF constructor
    private PlacementPayRate()
    {
        Currency = "GBP";
    }

    private PlacementPayRate(
        decimal amount,
        string currency,
        EngagementType engagementType,
        bool holidayPayInclusive,
        decimal? holidayPayRate)
    {
        Amount = amount;
        Currency = currency;
        EngagementType = engagementType;
        HolidayPayInclusive = holidayPayInclusive;
        HolidayPayRate = holidayPayRate;
    }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public EngagementType EngagementType { get; private set; }
    public bool HolidayPayInclusive { get; private set; }
    public decimal? HolidayPayRate { get; private set; }

    public static Result<PlacementPayRate> Create(
        decimal amount,
        string currency,
        EngagementType engagementType,
        bool holidayPayInclusive,
        decimal? holidayPayRate)
    {
        if (amount <= 0)
            return Result<PlacementPayRate>.Failure(Error.Validation("Pay rate amount must be greater than zero."));

        if (string.IsNullOrWhiteSpace(currency))
            return Result<PlacementPayRate>.Failure(Error.Validation("Currency is required."));

        if (currency.Length > 3)
            return Result<PlacementPayRate>.Failure(Error.Validation("Currency must not exceed 3 characters."));

        if (holidayPayRate.HasValue && holidayPayRate.Value <= 0)
            return Result<PlacementPayRate>.Failure(Error.Validation("Holiday pay rate must be greater than zero when specified."));

        return Result<PlacementPayRate>.Success(new PlacementPayRate(
            amount,
            currency.Trim().ToUpperInvariant(),
            engagementType,
            holidayPayInclusive,
            holidayPayRate));
    }

    public static PlacementPayRate FromPayRate(PayRate source)
    {
        return new PlacementPayRate(
            source.Amount,
            source.Currency,
            source.EngagementType,
            source.HolidayPayInclusive,
            source.HolidayPayRate);
    }
}

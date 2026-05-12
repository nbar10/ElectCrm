namespace ElectCrm.Domain.Vacancies;

using ElectCrm.Shared;

public sealed class PayRate
{
    // EF constructor
    private PayRate()
    {
        Currency = "GBP";
    }

    private PayRate(
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

    public static Result<PayRate> Create(
        decimal amount,
        string currency,
        EngagementType engagementType,
        bool holidayPayInclusive,
        decimal? holidayPayRate)
    {
        if (amount <= 0)
            return Result<PayRate>.Failure(Error.Validation("Pay rate amount must be greater than zero."));

        if (string.IsNullOrWhiteSpace(currency))
            return Result<PayRate>.Failure(Error.Validation("Currency is required."));

        if (currency.Length > 3)
            return Result<PayRate>.Failure(Error.Validation("Currency must not exceed 3 characters."));

        if (holidayPayRate.HasValue && holidayPayRate.Value <= 0)
            return Result<PayRate>.Failure(Error.Validation("Holiday pay rate must be greater than zero when specified."));

        return Result<PayRate>.Success(new PayRate(
            amount,
            currency.Trim().ToUpperInvariant(),
            engagementType,
            holidayPayInclusive,
            holidayPayRate));
    }
}

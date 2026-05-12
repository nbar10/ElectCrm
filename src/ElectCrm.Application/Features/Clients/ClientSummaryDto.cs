namespace ElectCrm.Application.Features.Clients;

using ElectCrm.Domain.Clients;

public sealed record ClientSummaryDto(
    Guid Id,
    string LegalName,
    string? TradingName,
    ClientStatus Status,
    string PrimaryBranchName,
    int ActiveVacancyCount,
    DateTimeOffset CreatedAt);

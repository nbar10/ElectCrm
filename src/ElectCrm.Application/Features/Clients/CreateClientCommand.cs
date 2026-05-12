namespace ElectCrm.Application.Features.Clients;

public sealed record CreateClientCommand(
    Guid PrimaryBranchId,
    string LegalName,
    string? TradingName);

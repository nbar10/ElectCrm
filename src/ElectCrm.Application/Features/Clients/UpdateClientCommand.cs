namespace ElectCrm.Application.Features.Clients;

public sealed record UpdateClientCommand(
    string LegalName,
    string? TradingName,
    Guid PrimaryBranchId);

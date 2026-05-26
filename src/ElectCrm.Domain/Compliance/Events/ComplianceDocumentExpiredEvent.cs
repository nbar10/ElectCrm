namespace ElectCrm.Domain.Compliance.Events;

using ElectCrm.Domain.Common;

public sealed record ComplianceDocumentExpiredEvent(
    Guid DocumentId,
    Guid PersonId,
    ComplianceDocumentType DocumentType,
    DateTimeOffset ExpiredAt) : DomainEvent;

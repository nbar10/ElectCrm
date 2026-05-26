namespace ElectCrm.Domain.Compliance.Events;

using ElectCrm.Domain.Common;

public sealed record ComplianceDocumentRejectedEvent(
    Guid DocumentId,
    Guid PersonId,
    ComplianceDocumentType DocumentType,
    Guid RejectedByUserId,
    string RejectionReason,
    DateTimeOffset RejectedAt) : DomainEvent;

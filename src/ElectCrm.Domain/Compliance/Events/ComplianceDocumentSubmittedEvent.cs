namespace ElectCrm.Domain.Compliance.Events;

using ElectCrm.Domain.Common;

public sealed record ComplianceDocumentSubmittedEvent(
    Guid DocumentId,
    Guid PersonId,
    ComplianceDocumentType DocumentType,
    Guid SubmittedByUserId,
    DateTimeOffset SubmittedAt) : DomainEvent;

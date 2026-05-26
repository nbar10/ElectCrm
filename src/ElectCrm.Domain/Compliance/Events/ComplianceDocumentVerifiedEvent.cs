namespace ElectCrm.Domain.Compliance.Events;

using ElectCrm.Domain.Common;

public sealed record ComplianceDocumentVerifiedEvent(
    Guid DocumentId,
    Guid PersonId,
    ComplianceDocumentType DocumentType,
    Guid VerifiedByUserId,
    DateTimeOffset VerifiedAt) : DomainEvent;

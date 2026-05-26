namespace ElectCrm.Domain.Compliance;

using ElectCrm.Domain.Common;
using ElectCrm.Domain.Compliance.Events;
using ElectCrm.Shared;

public sealed class ComplianceDocument : IHasDomainEvents
{
    private readonly List<DomainEvent> _domainEvents = [];

    // EF constructor
    private ComplianceDocument()
    {
    }

    private ComplianceDocument(
        Guid id,
        Guid personId,
        ComplianceDocumentType documentType,
        string? otherDescription,
        string? documentReference,
        string? notes,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        Guid createdByUserId)
    {
        Id = id;
        PersonId = personId;
        DocumentType = documentType;
        OtherDescription = otherDescription;
        DocumentReference = documentReference;
        Notes = notes;
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
        Status = ComplianceDocumentStatus.Unverified;
        LastModifiedById = createdByUserId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid PersonId { get; private set; }
    public ComplianceDocumentType DocumentType { get; private set; }
    public string? OtherDescription { get; private set; }
    public string? DocumentReference { get; private set; }
    public ComplianceDocumentStatus Status { get; private set; }
    public Guid? VerifiedByUserId { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Notes { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public Guid LastModifiedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<ComplianceDocument> Create(
        Guid personId,
        ComplianceDocumentType documentType,
        string? otherDescription,
        string? documentReference,
        string? notes,
        DateOnly? issueDate,
        DateOnly? expiryDate,
        Guid createdByUserId)
    {
        if (personId == Guid.Empty)
            return Result<ComplianceDocument>.Failure(Error.Validation("Person is required."));

        if (createdByUserId == Guid.Empty)
            return Result<ComplianceDocument>.Failure(Error.Validation("Created by user is required."));

        if (documentType == ComplianceDocumentType.Other && string.IsNullOrWhiteSpace(otherDescription))
            return Result<ComplianceDocument>.Failure(Error.Validation("Other description is required when document type is Other."));

        var document = new ComplianceDocument(
            Guid.CreateVersion7(),
            personId,
            documentType,
            otherDescription,
            documentReference,
            notes,
            issueDate,
            expiryDate,
            createdByUserId);

        document._domainEvents.Add(new ComplianceDocumentSubmittedEvent(
            document.Id,
            personId,
            documentType,
            createdByUserId,
            document.CreatedAt));

        return Result<ComplianceDocument>.Success(document);
    }

    public Result Verify(Guid verifiedByUserId, string? notes)
    {
        if (Status != ComplianceDocumentStatus.Unverified)
            return Result.Failure(Error.Validation("Only unverified documents can be verified."));

        Status = ComplianceDocumentStatus.Verified;
        VerifiedByUserId = verifiedByUserId;
        VerifiedAt = DateTimeOffset.UtcNow;
        LastModifiedById = verifiedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (notes is not null)
            Notes = notes;

        _domainEvents.Add(new ComplianceDocumentVerifiedEvent(
            Id, PersonId, DocumentType, verifiedByUserId, VerifiedAt.Value));

        return Result.Success();
    }

    public Result Reject(Guid rejectedByUserId, string rejectionReason)
    {
        if (Status != ComplianceDocumentStatus.Unverified)
            return Result.Failure(Error.Validation("Only unverified documents can be rejected."));

        if (string.IsNullOrWhiteSpace(rejectionReason))
            return Result.Failure(Error.Validation("Rejection reason is required."));

        Status = ComplianceDocumentStatus.Rejected;
        RejectionReason = rejectionReason;
        LastModifiedById = rejectedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ComplianceDocumentRejectedEvent(
            Id, PersonId, DocumentType, rejectedByUserId, rejectionReason, UpdatedAt));

        return Result.Success();
    }

    public Result MarkExpired()
    {
        if (Status != ComplianceDocumentStatus.Verified)
            return Result.Failure(Error.Validation("Only verified documents can be marked as expired."));

        Status = ComplianceDocumentStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new ComplianceDocumentExpiredEvent(Id, PersonId, DocumentType, UpdatedAt));

        return Result.Success();
    }
}

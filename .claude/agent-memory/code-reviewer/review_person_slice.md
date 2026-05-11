---
name: Person slice review findings
description: Issues found during 2026-05-11 code review of the Person feature slice (plan 03)
type: project
---

Review date: 2026-05-11. Build: 0 errors, 0 warnings.

## Confirmed passes
- No TenantId/AgencyBrandId on Person entity. No HasQueryFilter in DbContext. Correct.
- SearchAsync is fully server-side IQueryable with Where/Skip/Take before ToListAsync. Correct.
- All four Person pages carry `[Authorize(Policy = PolicyNames.GroupAdmin)]`. Policy exists and is wired to HasRoleRequirement. Correct.
- Candidate placeholder comment present in PersonDetail.razor (line 88). Correct.
- No hard-delete endpoint or UI. SoftDelete() implemented on entity; SoftDeleteAsync() in PersonService. Correct (but not exposed in UI this slice — acceptable).
- Migration has all 7 required indexes. Correct.
- PersonService registered as Scoped in Infrastructure DI (same pattern as ContactService). Correct.

## Issues requiring action

**WARNING — policy constant name mismatch (all Person pages)**
Plan specifies `PolicyNames.GroupAdminOnly`; the constant actually defined is `PolicyNames.GroupAdmin`. The pages use `PolicyNames.GroupAdmin` which is correct relative to the defined constant. The plan name was wrong; implementation is consistent. No bug, but note for future plan authoring.

**WARNING — phone digit-count validation missing**
Plan: "If PrimaryPhoneRaw provided: strip spaces, must result in ≥ 7 digits." PersonService only strips spaces and passes to hash — no digit count check. A string like "+44" or "abc" with spaces stripped passes validation.

**WARNING — passport number hashed without normalisation**
PassportNumberRaw is hashed and encrypted as-is (raw user input, no case normalisation). Two entries of the same passport with different casing will produce different hashes, defeating the matching index.

**WARNING — PersonDeactivatedEvent missing DeactivatedAt**
Plan payload: `PersonId, DeactivatedAt, Reason`. Actual record: `(Guid PersonId, string Reason)`. DeactivatedAt is absent. The matching slice will need to add it or won't have the timestamp.

**WARNING — Status filter dropdown absent from PersonList**
Plan: "Status filter dropdown: All / Active / Retired". PersonList.razor has no status filter dropdown and always passes `null` for Status in PersonSearchQuery. The SearchAsync method supports the filter but the UI does not expose it.

**WARNING — PersonErasedEvent missing ErasedAt**
Plan payload: `PersonId, ErasedAt`. Actual record: `(Guid PersonId)` only. The GDPR slice will be missing the erasure timestamp in the event payload.

**WARNING — ErasedAt does not suppress PII fields in PersonDetail**
Plan: "If ErasedAt is set, show a read-only notice and suppress all PII fields." PersonDetail renders the FullNameNormalised, DateOfBirth, and identifier presence rows unconditionally — the alert is shown but PII grid rows remain visible. For erased persons the FullNameNormalised will be "[erased]" and DateOfBirth null, so it is partially mitigated, but the suppression logic is not explicit.

**SUGGESTION — NonAlphaOrSpace regex retains underscores**
`[^\w\s]` matches non-word, non-space — but `\w` includes underscore. "O_Brien" would normalise to "o_brien" rather than "obrien". Unlikely in practice for names but technically incorrect per the plan description ("Strip punctuation").

**SUGGESTION — HashAlgorithmVersion constant only on NationalInsuranceNumber, not on PersonHashingService**
Plan: `public const string HashAlgorithmVersion = "SHA256-v1"` on PersonHashingService. It exists only on NationalInsuranceNumber. Should be on the service for future key-rotation support.

**SUGGESTION — SoftDelete() does not raise a domain event**
Deactivate() raises PersonDeactivatedEvent; SoftDelete() raises nothing. Future audit slice will have no event to subscribe to for soft-delete audit trail.

**SUGGESTION — MergeFrom() shell method absent from Person entity**
Plan: "PersonMergedEvent — MergeFrom(survivorId) — shell only". Neither MergeFrom() nor its event raise point exists on the entity. The event record (PersonMergedEvent) is defined but is unreachable without the shell method.

## Recurring patterns observed
- Inner FormModel class in PersonForm.razor — same pattern as ContactForm. The OnParametersSet ReferenceEquals guard is present (improvement over ContactForm).
- PersonService registered as concrete type in DI (not via interface) — same as ContactService; consistent with project pattern.
- Validation duplicated between PersonService.CreateAsync and Person.Create(). Service validates DisplayName and DateOfBirth, then Create() validates them again. Double validation is harmless but noisy.

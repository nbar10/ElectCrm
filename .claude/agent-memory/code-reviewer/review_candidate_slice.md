---
name: Candidate slice review findings
description: Findings from 2026-05-11 code review of the Candidate feature slice (plan 04)
type: project
---

Review of plan 04 (Candidate slice). Build: clean (0 errors, 0 warnings).

**Critical findings:**
1. `SourceLegacyId` is silently dropped on edit — `UpdateProfile` on `Candidate` does not accept it, `CandidateService.UpdateAsync` does not pass it, and `CandidateDetailsForm` shows an editable field for it that writes nothing to the database. The plan states `UpdateCandidateCommand` includes `SourceLegacyId` but the domain method does not mutate it.
2. `elect-badge-warning` CSS class used in `PersonSearchPanel.razor` for AlreadyCandidateAtThisBrand rows is not defined in `elect.css` (only `elect-badge-active` and `elect-badge-retired` exist). Badge will render without intended visual style.
3. Conflict error link in `CreateCandidate.razor` is constructed as an HTML string assigned to `_errorMessage` and rendered via `@_errorMessage` — Blazor HTML-encodes `@` expressions, so the anchor tag renders as literal text. The "View existing candidate" link is never clickable.

**Warning findings:**
4. `PersonList.razor` is missing the `POST_CANDIDATE_SLICE` comment required by the plan (only `PersonService.SearchAsync` has the comment).
5. `PersonDetail.razor` Candidates table is missing the Brand name column — the plan specifies columns: Brand name, Status, Registration Date, link. `CandidateSummaryDto` does not carry `AgencyBrandId` or a brand name, and `GetByPersonIdAsync` does not join `AgencyBrands`. This is a cross-brand GroupAdmin view so brand name is load-bearing.
6. `CreateCandidate.razor` does not reset `_isSubmitting = false` on the success path (NavigateTo is called via early return before the reset on line 205 — the reset is only reached on error paths). In practice NavigateTo triggers a page transition before the state matters, but the pattern is fragile.

**Note findings:**
- `SourceLegacyId` is present on `UpdateCandidateCommand` per the plan, but the plan states "immutable after creation" applies to `RegistrationDate`, not `SourceLegacyId`. The plan's intent was that `SourceLegacyId` IS mutable via edit. The domain method omission is a bug, not a deliberate deferral.
- All four future-slice placeholder comments are present in `CandidateDetail.razor`.
- Global query filter for Candidate correctly matches the Contact pattern.
- Migration correctly uses `date` type for RegistrationDate, Restrict/SetNull FKs, filtered unique index, ValueGeneratedNever.
- `PersonSearchPanel` correctly uses `IgnoreQueryFilters` on Candidates join scoped to current brand only.

**Why it matters:** The SourceLegacyId silent-drop is data loss — users can type a value and submit the edit form believing it was saved, when it was not. The badge CSS gap means duplicate-detection affordance is invisible. The broken Conflict link means users cannot navigate to the existing candidate from the error state.

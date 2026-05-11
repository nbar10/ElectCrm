---
name: Contacts slice review findings
description: Issues found during 2026-05-09 code review of the Contacts feature slice (plan 02)
type: project
---

Review date: 2026-05-09. Build: 0 errors, 0 warnings.

Key recurring patterns to watch in future slices:

**ContactService is registered in Infrastructure DI, not Application DI.** This is documented as justified (circular reference). The Application DI extension stays empty. Watch for this pattern in future slices.

**FormModel inner class pattern in ContactForm.razor.** The form uses an internal `FormModel` class rather than binding directly to the command record. `OnParametersSet` re-initialises `_form` from `Model` on every parent re-render — fragile if parent re-renders during submission. Worth raising in future form components.

**`[Required]` on `FormModel.ClientId` is missing.** The `CreateContactCommand` has `[Required]` on `ClientId`, but the `FormModel` inner class does not. Since `DataAnnotationsValidator` validates `_form`, not the command, `Guid.Empty` passes client-side validation and the error only surfaces via the service `Result.Failure` banner.

**Email validation mismatch between `[EmailAddress]` attribute and domain logic.** `[EmailAddress]` accepts `"user@example"` (no dot after @), but `Contact.Create` rejects it. Domain errors surface in the generic error banner, not inline.

**`EditContact` shows an inline not-found message instead of redirecting.** The plan says redirect to `/app/contacts` on not-found; the implementation sets `_notFound = true` and renders an inline error.

**`ChannelPrefs_Channel` is `IsRequired()` in config but generates `nullable: true` in migration DDL.** EF Core silently overrides `IsRequired()` on owned-type properties when the navigation is optional. The nullable column is correct behaviour.

**`ContactService` registered as Scoped in Infrastructure DI** — correct, matches DbContext lifetime.

**`ClearDomainEvents()` is called before `SaveChangesAsync()` in seeder** — correct.

**Seeder idempotency check uses the filtered `DbSet<Contact>`** — the `IsDeleted` filter remains active (TenantId.Empty only bypasses tenant filter). If seeded contacts are retired, they would be re-seeded on next run.

# Test Pass 08 — User Management Slice

**Date:** _fill in when running_
**Tester:** Neil
**App URL:** _fill in (typically https://localhost:5001)_
**Plan reference:** `docs/plans/08-user-management-slice.md`

---

## Purpose

This is the manual end-to-end test pass for the User Management slice. It serves two purposes:

1. **Verification** that the slice meets its functional and security requirements before commit
2. **Specification** for the automated tests that will be built in the Test Infrastructure slice

For each scenario, capture brief observations alongside pass/fail. The observations become the source-of-truth assertions for future Playwright tests — note what you actually saw (specific text, URL, response time, visual cues), not just whether it matched.

---

## Test users

Have these credentials accessible before starting. Passwords are in user-secrets under `DevSeed:AdminPassword`, `DevSeed:BrandAdminPassword`, `DevSeed:ConsultantPassword`.

| Email | Role | Brand | Notes |
|---|---|---|---|
| admin@elect.group | GroupAdmin | Brand 1 (also has BrandAdmin claim) | Existing seed |
| admin2@elect.group | GroupAdmin | Brand 2 | Existing seed |
| brandadmin1@elect.group | BrandAdmin | Brand 1 | Newly seeded (Sarah Chen) |
| brandadmin2@elect.group | BrandAdmin | Brand 2 | Newly seeded (Marcus Webb) |
| consultant1a@elect.group | Consultant | Brand 1, London HQ | Newly seeded (Tom Hughes) |
| consultant1b@elect.group | Consultant | Brand 1, London HQ | Newly seeded (Emily Rodriguez) |
| user@midlands-industrial.test | Consultant | Brand 4 (paused) | Cannot log in due to BrandInactiveException |

---

## SQL tool checklist before starting

Have a SQL window open. Quick reference queries:

```sql
-- User snapshot
SELECT Email, DisplayName, PrimaryBranchId, IsActive, RequirePasswordChange, UpdatedAt
FROM AspNetUsers
ORDER BY Email;

-- Role claims for a specific user
SELECT u.Email, c.ClaimType, c.ClaimValue
FROM AspNetUsers u
JOIN AspNetUserClaims c ON c.UserId = u.Id
WHERE u.Email = '<email>'
  AND c.ClaimType = 'elect_role';

-- Force-change flag toggle (for testing)
UPDATE AspNetUsers SET RequirePasswordChange = 1 WHERE Email = '<email>';
UPDATE AspNetUsers SET RequirePasswordChange = 0 WHERE Email = '<email>';
```

---

# SECTION 1 — GroupAdmin: unrestricted access

**Logged in as:** `admin@elect.group`

### 1.1 — User list shows all users across all brands

**Action:** Navigate to `/admin/users`.

**Expected:** All 15 users visible across Brand 1, 2, 3, 4.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 1.2 — User detail page shows full profile and actions

**Action:** Click into any user's detail page.

**Expected:** Full profile, role claims, audit info, and action buttons (Edit, Deactivate, Reset Password, Assign Role) all visible.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 1.3 — Brand filter shows all brands

**Action:** Open the user filter for Brand.

**Expected:** All brands available as filter options. (BrandAdmin will not see this — it's a GroupAdmin-only filter.)

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 1.4 — Filter by Consultant role

**Action:** Filter the list by Role = Consultant.

**Expected:** All 10 Consultants visible (1 Midlands user + 9 newly seeded).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 1.5 — Filter by IsActive = Inactive

**Action:** Filter by IsActive = Inactive.

**Expected:** Zero results (no users deactivated yet).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 1.6 — Free-text search

**Action:** Search for "Tom".

**Expected:** Tom Hughes (consultant1a) visible in results.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 2 — GroupAdmin: user creation and the OTP modal

**Logged in as:** `admin@elect.group`

### 2.1 — Create form loads with all expected fields

**Action:** Navigate to `/admin/users/new`.

**Expected:** Form shows: Email, DisplayName, JobTitle, PrimaryBranch dropdown, Role checkboxes (Consultant, BrandAdmin, GroupAdmin all visible).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.2 — Branch dropdown groups by brand

**Action:** Inspect the PrimaryBranch dropdown.

**Expected:** Branches grouped by brand via `<optgroup>` or equivalent visual grouping.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.3 — Validation: empty Email rejected

**Action:** Submit form with empty Email.

**Expected:** Inline validation error, no modal shown.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.4 — Validation: duplicate Email rejected

**Action:** Submit with Email = `admin@elect.group` (existing user).

**Expected:** "Email already in use" error (Error.Conflict), no modal.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.5 — Successful create shows OTP modal with security properties

**Action:** Create a new user:
- Email: `testuser1@elect.group`
- DisplayName: "Test User One"
- JobTitle: "Test Consultant"
- PrimaryBranch: London HQ (Brand 1)
- Roles: [Consultant only]

**Expected:**
- OTP modal appears
- Password shown in monospace font
- "Copy to clipboard" button present
- NO X button to close
- NO click-outside-to-dismiss
- Only dismissal is the "I have copied this password" confirm button

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

**Note:** Copy the password before continuing — you'll need it in Section 3.

---

### 2.6 — Confirm dismissal redirects to detail page

**Action:** Click the confirm button on the OTP modal.

**Expected:** Redirected to `/admin/users/{newUserId}`.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.7 — SQL verification of new user

**Action:** Run:
```sql
SELECT Email, DisplayName, PrimaryBranchId, RequirePasswordChange
FROM AspNetUsers
WHERE Email = 'testuser1@elect.group';
```

**Expected:** DisplayName = "Test User One", PrimaryBranchId points to London HQ, RequirePasswordChange = 1.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 2.8 — SQL verification of role claim

**Action:** Run:
```sql
SELECT ClaimType, ClaimValue
FROM AspNetUserClaims
WHERE UserId = (SELECT Id FROM AspNetUsers WHERE Email = 'testuser1@elect.group');
```

**Expected:** Exactly one `elect_role` claim of format `Consultant:Brand:{Brand1Id}`.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 3 — New user force-change flow

**Logged in as:** `testuser1@elect.group` (the one you just created)

Log out of admin@elect.group first. Then log in with the password you copied from scenario 2.5.

### 3.1 — Force-change redirect on first login

**Action:** Complete login as testuser1.

**Expected:** Immediate redirect to `/profile/change-password?required=true`.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 3.2 — Required banner visible

**Action:** Inspect the page header.

**Expected:** Banner visible saying "You must set a new password before continuing" (or similar wording).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 3.3 — No Cancel button

**Action:** Inspect the form actions.

**Expected:** No Cancel button. Only confirm/submit and (somewhere accessible) Sign Out.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 3.4 — URL manipulation bypass blocked

**Action:** Manually type `/app` into the address bar and hit Enter.

**Expected:** Redirected back to `/profile/change-password?required=true`. The `/app` page does NOT render even briefly.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

**Note:** This is the security-critical scenario. If you see the `/app` page even briefly, that's a real bug.

---

### 3.5 — Successful password change redirects to /app

**Action:** Change password to a new valid value.

**Expected:** Redirected to `/app` on success.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 3.6 — Normal navigation works after force-change cleared

**Action:** Navigate to `/app/vacancies`.

**Expected:** Page loads normally (force-change has been cleared).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 3.7 — SQL verification of flag cleared

**Action:** Run:
```sql
SELECT Email, RequirePasswordChange
FROM AspNetUsers
WHERE Email = 'testuser1@elect.group';
```

**Expected:** RequirePasswordChange = 0.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 4 — BrandAdmin: scoped access

**Logged in as:** `brandadmin1@elect.group` (BrandAdmin for Brand 1)

Log out of testuser1 first.

### 4.1 — User list shows only Brand 1 users

**Action:** Navigate to `/admin/users`.

**Expected:** Only Brand 1 users visible:
- admin@elect.group (GroupAdmin, also has Brand 1 admin claim)
- brandadmin1@elect.group (yourself)
- consultant1a, 1b, 1c (3 newly seeded Brand 1 consultants)
- testuser1@elect.group (just created in Section 2)

Brand 2, Brand 3, Brand 4 users NOT visible.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 4.2 — Brand filter hidden or pre-filtered

**Action:** Inspect filter controls.

**Expected:** Brand filter hidden, OR pre-filtered to Brand 1 with no option to change.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 4.3 — In-scope detail page accessible

**Action:** Click into consultant1a's detail page.

**Expected:** Full detail view loads correctly.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 4.4 — Out-of-scope detail page returns NotFound (information hiding)

**Action:** Find Brand 2 admin's user ID via SQL:
```sql
SELECT Id FROM AspNetUsers WHERE Email = 'admin2@elect.group';
```

Then navigate to `/admin/users/{that-id}` directly.

**Expected:** "User not found" message OR redirect. NOT a 403. NOT a detail page (even briefly). NO leakage of admin2's data.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

**Note:** This is the second security-critical scenario. Watch carefully for any flash of admin2's data — even a 200ms render before the NotFound takes over would be an information leak.

---

### 4.5 — Create form: branch dropdown scoped to Brand 1

**Action:** Navigate to `/admin/users/new`. Inspect PrimaryBranch dropdown.

**Expected:** Only Brand 1's branches visible (London HQ, Manchester). No Brand 2/3/4 branches.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 4.6 — Create form: only Consultant role assignable

**Action:** On the same create form, inspect the Role checkboxes.

**Expected:** Only "Consultant" visible. NOT BrandAdmin. NOT GroupAdmin.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 4.7 — Assign Role: only Consultant available

**Action:** On consultant1b's detail page, find the "Assign Role" action.

**Expected:** Available role to assign is "Consultant" only.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

**Cross-check:** This should match scenario 4.6 — BrandAdmins cannot grant elevated roles at create OR at assign time.

---

### 4.8 — Self-deactivation hidden

**Action:** Navigate to your own user detail page (brandadmin1).

**Expected:** "Deactivate" button HIDDEN. (No accidental self-lockout.)

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 5 — BrandAdmin: deactivation and role changes

**Logged in as:** `brandadmin1@elect.group`

### 5.1 — Deactivate flow shows confirmation modal

**Action:** Go to consultant1b's detail page. Click Deactivate.

**Expected:** Confirmation modal appears with optional reason field.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 5.2 — Deactivation succeeds

**Action:** Confirm with reason "Testing deactivation flow".

**Expected:** Action succeeds, user shown as Inactive on the detail page.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 5.3 — SQL verification of deactivation

**Action:** Run:
```sql
SELECT Email, IsActive
FROM AspNetUsers
WHERE Email = 'consultant1b@elect.group';
```

**Expected:** IsActive = 0.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 5.4 — Deactivated user cannot log in (friendly error)

**Action:** In a separate browser session (incognito), try to log in as consultant1b.

**Expected:** "Your account has been deactivated" message (or similar friendly error). NOT a generic error page. NOT a 500.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 5.5 — Reactivation succeeds

**Action:** Back in brandadmin1's session, go to consultant1b's detail and click Reactivate.

**Expected:** User shown as Active. Confirm in SQL `IsActive = 1`.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 5.6 — Role revoke

**Action:** On consultant1c's detail page, revoke the Consultant role.

**Expected:** Role removed. User shows no role claims.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

**Cleanup note:** Remember to re-add the Consultant role to consultant1c at the end of testing, otherwise subsequent test passes start with a corrupted state. (See Cleanup section.)

---

### 5.7 — GroupAdmin role not assignable by BrandAdmin

**Action:** On any user's detail page, find the "Assign Role" action and inspect available roles.

**Expected:** GroupAdmin NOT in the list. BrandAdmin NOT in the list. Only Consultant available.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 6 — Consultant: self-service and restrictions

**Logged in as:** `consultant1a@elect.group` (Tom Hughes)

Log out of brandadmin1 first.

### 6.1 — Admin route blocked

**Action:** Navigate to `/admin/users` directly.

**Expected:** 403 page OR redirect away. Route policy gate works.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.2 — Profile page accessible

**Action:** Navigate to `/profile`.

**Expected:** Page loads.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.3 — Profile shows correct editable vs read-only fields

**Action:** Inspect the form fields.

**Expected:**
- Editable: DisplayName, JobTitle, PhoneNumber, Email
- Read-only: Primary Branch, Brand, Role(s)

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.4 — Profile edit persists

**Action:** Change DisplayName to "Tom Hughes (Test)" and save.

**Expected:** Change persists; reloading the page shows the updated value.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.5 — Change Password page reachable

**Action:** Click "Change Password".

**Expected:** Reach `/profile/change-password`.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.6 — No required banner on voluntary change

**Action:** Inspect the page (you arrived here voluntarily, not forced).

**Expected:** NO `?required=true` banner. This is voluntary, not forced.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.7 — Cancel button present

**Action:** Inspect the form actions.

**Expected:** Cancel button visible, returns to `/profile` when clicked.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.8 — Voluntary password change succeeds

**Action:** Change password using current password as input.

**Expected:** Success. Confirm via SQL that `UpdatedAt` is a recent timestamp.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 6.9 — Re-login with new password works

**Action:** Log out, log back in with the new password.

**Expected:** Login succeeds.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# SECTION 7 — Audit trail and role change propagation

**Logged in as:** `admin@elect.group`

Log back in as admin@elect.group.

### 7.1 — Audit info shows recent changes

**Action:** Go to consultant1a's detail page. Inspect audit section.

**Expected:** Shows CreatedAt (from seeding), UpdatedAt (recent — from Tom's profile edit), LastModifiedBy (should resolve to Tom).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.2 — Assign BrandAdmin role to a Consultant

**Action:** On consultant1a's detail page, Assign Role: BrandAdmin (for Brand 1). Reason: "Promoting to BrandAdmin for testing".

**Expected:** Role appears in the list. UI shows a note about 5-minute propagation via security stamp.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.3 — Role change UI feedback

**Action:** Confirm the page refreshes and shows the new role.

**Expected:** Both Consultant and BrandAdmin claims visible.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.4 — SQL verification of new role claim

**Action:** Run:
```sql
SELECT c.ClaimType, c.ClaimValue
FROM AspNetUserClaims c
JOIN AspNetUsers u ON u.Id = c.UserId
WHERE u.Email = 'consultant1a@elect.group'
  AND c.ClaimType = 'elect_role';
```

**Expected:** Both `Consultant:Brand:{Brand1Id}` and `BrandAdmin:Brand:{Brand1Id}` present.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.5 — Security stamp exists (presence check)

**Action:** Run:
```sql
SELECT SecurityStamp
FROM AspNetUsers
WHERE Email = 'consultant1a@elect.group';
```

**Expected:** Non-null SecurityStamp value. (Without before/after capture you can't easily verify it changed, but the next scenario proves the role propagation works.)

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.6 — Newly-promoted user can access admin pages

**Action:** Log out. Log in as consultant1a.

**Expected:** `/admin/users` is now accessible (BrandAdmin gate satisfied via the new claim).

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

### 7.7 — Newly-promoted user sees Brand 1 users only

**Action:** Inspect the user list.

**Expected:** Brand 1 users only — matches brandadmin1's view from Section 4.

**Result:** ⬜ PASS / ⬜ FAIL

**Observation:**

---

# CLEANUP

**Logged in as:** `admin@elect.group`

### C.1 — Revoke the BrandAdmin role from consultant1a

**Action:** Revoke the BrandAdmin role granted in scenario 7.2.

**Expected:** Only Consultant:Brand:{Brand1Id} remains.

**Result:** ⬜ DONE

---

### C.2 — Re-assign Consultant role to consultant1c if revoked

**Action:** If you revoked consultant1c's Consultant role in scenario 5.6, re-assign it now.

**Expected:** consultant1c has Consultant:Brand:{Brand1Id} again.

**Result:** ⬜ DONE / ⬜ N/A

---

### C.3 — Restore consultant1a's DisplayName

**Action:** If you changed Tom's DisplayName to "Tom Hughes (Test)" in scenario 6.4, change it back to "Tom Hughes".

**Expected:** consultant1a's DisplayName is "Tom Hughes".

**Result:** ⬜ DONE / ⬜ N/A

---

### C.4 — testuser1 remains in the system

**Note:** testuser1@elect.group is NOT deleted. There is no hard delete by design. They become part of the seeded test population.

**Result:** ⬜ ACKNOWLEDGED

---

# Summary

| Section | Total | Pass | Fail |
|---|---|---|---|
| 1. GroupAdmin unrestricted access | 6 | | |
| 2. User creation and OTP modal | 8 | | |
| 3. Force-change flow | 7 | | |
| 4. BrandAdmin scoped access | 8 | | |
| 5. BrandAdmin deactivation and roles | 7 | | |
| 6. Consultant self-service | 9 | | |
| 7. Audit trail and propagation | 7 | | |
| **Total** | **52** | | |

**Note:** I see I'd quoted "40 scenarios" earlier; the actual structured count is closer to 52 with the SQL verifications included. Most are quick.

---

## Notes for the Test Infrastructure slice

As you go through the test pass, capture anything relevant to future automation here:

**Scenarios that felt fragile or timing-sensitive:**

_e.g. "scenario 3.4 — redirect happens fast but I had to be precise about the order of tab focus and Enter press"_

**Scenarios where the UI behaviour is surprisingly hard to assert:**

_e.g. "the OTP modal's monospace font is visual — how would Playwright check this?"_

**Scenarios that feel like the highest-value automated tests:**

_The starter suite for the test infrastructure slice will probably draw from these._

**Scenarios where manual testing is genuinely easier than automation could ever be:**

_Some things stay manual. Capture them here so the automation scope is honest._

---

## Overall result

⬜ All scenarios pass — cleared to commit
⬜ Failures encountered — see notes below

**Failures requiring triage:**

_List failed scenarios with brief notes here. Then ping Claude for help triaging._

**Commit hash after slice closure:**

_Filled in when committed._

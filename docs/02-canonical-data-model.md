**Technical Specification**

**Canonical Data Model**

*Entities, relationships, multi-tenancy, audit, and migration*

Companion document to: UROP Project Brief v1.2

Audience: All engineering teams; foundation for every other module

Version: 1.0

# 1. Purpose and Scope

This document specifies the canonical data model — the central schema that every module reads from and writes to. It is the foundation document for the platform; the AI Engagement Layer, Distribution Engine, CRM, ATS, Compliance, and Sales Intelligence modules all depend on the entities defined here.

Read this document first. The other specifications assume familiarity with the entities below.

Scope: entity definitions, relationships, multi-tenancy strategy, audit and event sourcing approach, identifier conventions, and migration strategy from acquired-agency systems. Out of scope: physical database schema (indexes, partitioning, view definitions), API surface contracts, UI representations.

# 2. Modelling Principles

**One model, many tenants. **Every operational entity is multi-tenanted by AgencyBrand. The same person can exist as a Candidate under multiple brands; the data model handles this explicitly rather than papering over it.

**Stable identity is paramount. **Internal IDs are platform-generated UUIDs and never change. External identifiers (legacy IDs, government references) are stored as attributes, never as primary keys.

**Truth flows from canonical entities, not from channels. **Pay rates, vacancy details, and candidate attributes live on canonical records. Channel-specific representations (a Find a Job listing, a WhatsApp message) are projections, not sources of truth.

**Time matters. **Critical entities — Vacancies, Placements, Compliance documents, Pay rates — are temporal. We track when something was true, not just what is true now. Tribunals, audits, and AWR calculations all require this.

**Events are first-class. **Financial, compliance, and AI agent activity are recorded as immutable events alongside the mutable canonical records. The current state can be reconstructed from events; the events explain how it got there.

**Soft delete by default. **Records are deactivated, not destroyed. Hard delete only for GDPR right-to-erasure requests, performed via a controlled erasure workflow that preserves audit lineage where lawful.

# 3. Core Entities

Each entity below carries a stable UUID, created/updated timestamps, soft-delete flag, and tenancy reference (AgencyBrand). Those fields are common to all entities and are not repeated in the per-entity tables. Where fields are listed in tables, they are the entity-specific fields beyond the common set.

## 3.1 AgencyBrand

Represents one of the acquired (or future) agencies operating on the platform. Customer-facing identity, branding, regulatory registrations, and tenancy boundary.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **legal_name** | string | Registered company name as it appears at Companies House |
| **trading_name** | string | Customer-facing brand name (may differ from legal name) |
| **companies_house_number** | string | UK company number |
| **vat_number** | string | VAT registration |
| **glaa_licence_number** | string? | Where applicable to sectors operated |
| **registered_address** | Address | Required for Conduct Regs 2003 advert identification |
| **primary_contact_email** | string |  |
| **dwp_account_id** | string? | Find a Job employer account |
| **status** | enum | active, paused, retired |
| **onboarded_at** | timestamp | When migrated onto the platform |
| **parent_group_id** | uuid? | If part of an internal grouping (e.g. brands sharing a P&L) |

## 3.2 Branch

A physical or virtual branch within an AgencyBrand. Branches own consultants, geographies, and (often) clients.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid | FK to AgencyBrand |
| **name** | string | e.g. "London Construction" |
| **geography** | GeoArea | Postcode prefixes or shapefile served |
| **address** | Address |  |
| **status** | enum | active, retired |

## 3.3 User

Internal user — consultant, manager, compliance officer, finance, etc. Identity for authentication, authorisation, and accountability.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **full_name** | string |  |
| **email** | string | SSO identifier |
| **agency_brand_id** | uuid | Primary brand |
| **branch_id** | uuid? | Primary branch |
| **roles** | Role[] | Branch-scoped, brand-scoped, or group-scoped |
| **status** | enum | active, suspended, retired |
| **last_active_at** | timestamp |  |

## 3.4 Candidate

A worker available for placement. The most heavily referenced entity in the platform. Held per AgencyBrand, with cross-brand linking via PersonIdentity (§3.5).

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid | Tenancy |
| **person_identity_id** | uuid | FK to cross-brand PersonIdentity |
| **full_name** | string |  |
| **preferred_name** | string? |  |
| **preferred_language** | enum | en, pl, ro, bg, lt, uk, pt, ... |
| **primary_phone** | phone | E.164 |
| **secondary_phone** | phone? |  |
| **email** | string? |  |
| **address** | Address? |  |
| **right_to_work** | RightToWork | Status, evidence document refs, expiry |
| **trades** | Trade[] | Tagged trades with self-reported and verified levels |
| **cards** | TicketCard[] | CSCS, CPCS, NPORS, etc. with expiry and document refs |
| **other_certifications** | Certification[] | First aid, asbestos awareness, etc. |
| **transport** | TransportProfile | Own vehicle, driving licence categories, willingness to travel |
| **availability** | AvailabilityCalendar | Pattern + exceptions |
| **preferred_geographies** | GeoArea[] | Postcode preferences and radius |
| **pay_preferences** | PayPreference | PAYE / CIS / Umbrella / Ltd preferences |
| **consent_flags** | ConsentSet | Per channel: SMS, WhatsApp, email, marketing, share with clients |
| **reliability_score** | score | Derived; see §6 |
| **source** | string | How acquired (Find a Job, referral, walk-in, migrated) |
| **source_legacy_id** | string? | External ID from legacy system at migration |
| **status** | enum | active, dormant, suspended, opted_out, retired |

## 3.5 PersonIdentity

Cross-brand reference for the same human individual. A candidate appearing under three acquired brands shares one PersonIdentity. Used for de-duplication, AWR aggregation across brands, and modern slavery red-flag detection.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **primary_phone_hash** | string | Hashed for matching without exposing PII unnecessarily |
| **national_insurance_number_hash** | string? | Where collected; hashed |
| **passport_number_hash** | string? | Where collected; hashed |
| **full_name_normalised** | string | Normalised form for matching |
| **dob** | date? | Where collected |
| **candidate_links** | CandidateLink[] | All Candidate records linked to this identity |
| **status** | enum | active, retired |

Note: some candidates legitimately work under multiple brands (different sectors, different relationships). PersonIdentity makes this visible without forcing consolidation.

## 3.6 Client

An end customer engaging the agency to supply temp workers.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid | Tenancy |
| **company_identity_id** | uuid | FK to cross-brand CompanyIdentity |
| **legal_name** | string |  |
| **trading_name** | string? |  |
| **companies_house_number** | string? |  |
| **sector** | Sector | Main contractor / sub-contractor / civils / M&E / FM / etc. |
| **tier** | enum | tier_1, tier_2, sme, sole_trader |
| **primary_branch_id** | uuid | Owning branch |
| **addresses** | Address[] | Registered + sites |
| **sites** | Site[] | Operational sites worked |
| **psl_status** | enum | preferred, approved, untiered, blacklisted |
| **credit_status** | CreditStatus | Limit, terms, current exposure |
| **rate_card_default** | RateCard? | Default per-trade pay/bill schedule |
| **compliance_pack_required** | DocPackSpec? | What compliance docs the client requires from us |
| **status** | enum | active, paused, retired |

## 3.7 CompanyIdentity

Cross-brand reference for the same end company. Mirrors PersonIdentity but for clients. Critical when the same main contractor is worked by multiple acquired brands.

## 3.8 Contact

A named human at a Client — site managers, agency liaisons, finance contacts.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **client_id** | uuid |  |
| **full_name** | string |  |
| **role_title** | string? |  |
| **email** | string? |  |
| **phone** | phone? |  |
| **primary_for_categories** | ContactCategory[] | site_manager, accounts_payable, compliance, etc. |
| **communication_preferences** | ChannelPrefs | Preferred contact channel and times |
| **status** | enum | active, retired |

## 3.9 Vacancy

The single source of truth for a role to be filled. The Distribution Engine and AI Content Agent project from this; nothing they do creates new truth that does not exist here.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid |  |
| **branch_id** | uuid |  |
| **client_id** | uuid |  |
| **site_id** | uuid? | Specific site within client |
| **role_title_canonical** | string | From canonical job-title taxonomy |
| **trade_required** | Trade | Primary trade |
| **additional_skills** | Skill[] |  |
| **cards_required** | CardRequirement[] | CSCS level required, etc. |
| **pay_rate** | PayRate | Numeric, with explicit treatment of holiday pay under AWR |
| **bill_rate** | BillRate | Internal only — never exposed externally |
| **pay_basis** | enum | paye, cis, umbrella, ltd |
| **start_date** | date |  |
| **expected_end_date** | date? |  |
| **shift_pattern** | ShiftPattern | Days, hours, breaks |
| **headcount_required** | integer | Multiple candidates per Vacancy supported |
| **location** | Location | Postcode + descriptive — must be specific for Find a Job |
| **consultant_owner_id** | uuid | User |
| **sourcing_owner_id** | uuid? | User |
| **created_from** | enum | manual, ai_brief_intake, sales_intelligence_lead |
| **status** | VacancyStatus | draft, validated, live, paused, filled, closed_unfilled, cancelled |
| **compliance_validation** | ValidationResult | Result of last compliance check, with timestamps |
| **distribution_targets** | ChannelSelection[] | Which channels selected for distribution |
| **public_advert_drafts** | AdvertDraft[] | Per-channel drafts produced by the AI agent |
| **sensitive_flags** | Flag[] | high_pay, sensitive_sector, new_client, requires_human_approval |

## 3.10 Site

A specific location within a Client where work is performed. Distinct from Client because one client typically has many sites, each with its own postcode, contact, induction process, and rate card variations.

## 3.11 Placement

A Candidate confirmed for a Vacancy. Placements are temporal, with daily granularity for shift-based work.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **vacancy_id** | uuid |  |
| **candidate_id** | uuid |  |
| **start_date** | date |  |
| **end_date** | date? | Null while active |
| **pay_rate_at_start** | PayRate | Snapshotted from Vacancy at confirmation |
| **bill_rate_at_start** | BillRate | Snapshotted |
| **awr_qualifying_clock** | AwrClock | Tracks 12-week qualifying period across breaks |
| **status** | enum | confirmed, started, completed, terminated_early, no_show |
| **confirmed_at** | timestamp |  |
| **confirmed_by** | uuid | User or AgentAction reference |

## 3.12 Shift

A single shift instance under a Placement. Daily granularity supports per-day timesheets, no-shows, and partial-day attendance.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **placement_id** | uuid |  |
| **scheduled_start** | timestamp |  |
| **scheduled_end** | timestamp |  |
| **actual_start** | timestamp? | From clock-in |
| **actual_end** | timestamp? | From clock-out |
| **clock_in_geo** | GeoPoint? | Validated against site geofence |
| **clock_out_geo** | GeoPoint? |  |
| **status** | enum | scheduled, attended, no_show, cancelled, in_dispute |
| **timesheet_id** | uuid? | Link to Timesheet entry |

## 3.13 Timesheet

Aggregated record of hours worked over a pay period, used as the basis for both pay and bill calculations.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **candidate_id** | uuid |  |
| **client_id** | uuid |  |
| **period_start** | date |  |
| **period_end** | date |  |
| **entries** | TimesheetEntry[] | Per-shift hours, breaks, codes (basic, OT1, OT2, bank holiday, etc.) |
| **client_approval** | Approval? | Approver, timestamp, channel (email/portal/WhatsApp) |
| **status** | enum | draft, submitted, approved, rejected, in_dispute, paid, billed |
| **pay_calculation** | PayCalculation? | Resulting gross pay; references PayRate snapshot |
| **bill_calculation** | BillCalculation? | Resulting client invoice line |

## 3.14 Invoice

Outbound bill to a Client, typically self-bill for temp recruitment.

## 3.15 PayRun

A batch of pay calculations sent to the configured payroll provider. Holds the integration's run identifier and reconciliation status.

## 3.16 Lead

A sales opportunity surfaced by the Sales Intelligence Engine or entered manually. Distinct from Client until conversion.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid |  |
| **assigned_branch_id** | uuid? | Routing destination |
| **assigned_user_id** | uuid? | Sales consultant |
| **source** | enum | planning_portal, contracts_finder, companies_house, trade_press, manual, referral |
| **source_record_ref** | string | External ID for traceability |
| **company_identity_id** | uuid? | Linked if known |
| **project_details** | ProjectDetails | Value, scheme, location, expected start, anticipated trades |
| **enrichment** | Enrichment | Geocoding, company linkage, predicted labour requirements |
| **pipeline_stage** | enum | new, qualified, contacted, opportunity, won, lost, dormant |
| **status** | enum | active, retired |

## 3.17 Channel

A configured outbound channel (per agency brand, per channel type). Holds credentials reference, ToS version, rate limit profile, and feature flags.

## 3.18 Post

A specific instance of a vacancy distributed via a channel. Holds the channel-side identifier (post URL, message ID, feed entry ID), the rendered draft, dispatch timestamp, and metrics.

## 3.19 Conversation

A thread of messages between a candidate (or contact) and an agent or human, scoped to a channel. Messages within a Conversation are time-ordered Message records.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agency_brand_id** | uuid |  |
| **channel** | enum | whatsapp, telegram, sms, email, messenger, instagram_dm, ... |
| **candidate_id** | uuid? | Or contact_id for client conversations |
| **contact_id** | uuid? |  |
| **linked_vacancy_id** | uuid? | Where the conversation was triggered by a specific vacancy |
| **assigned_user_id** | uuid? | Human owner if escalated |
| **assigned_agent** | AgentRef? | AI agent owning while autonomous |
| **disclosure_state** | enum | ai_disclosed, human_handover, n_a |
| **status** | enum | active, awaiting_candidate, awaiting_human, resolved, archived |
| **last_message_at** | timestamp |  |

## 3.20 Message

A single inbound or outbound message within a Conversation. Carries channel-specific identifiers, content (with sensitive data redaction applied), attachments, and the AgentAction reference if produced by an agent.

## 3.21 Application

An expression of interest in a specific Vacancy from a Candidate, regardless of channel. Conversations and CV emails alike normalise into Applications.

## 3.22 ComplianceDocument

A piece of evidence held against a Candidate or Client — right-to-work documents, CSCS card scans, insurance certificates, etc. Versioned, expirable, and audit-trailed.

## 3.23 AgentAction

Every action taken by an AI agent — message drafted, message sent, screening decision, escalation, candidate field updated, advert generated. Immutable. The audit log of the AI layer.

| **Field** | **Type** | **Notes** |
| --- | --- | --- |
| **agent_family** | enum | advert_authoring, candidate_engagement, client_communication, sourcing_matching |
| **agent_version** | string | Semantic version of the agent at time of action |
| **model_identifier** | string | Which underlying model was invoked |
| **prompt_version** | string | Version of the prompt template |
| **trust_level_at_action** | integer | 0–3, recorded for audit |
| **inputs_hash** | string | Hash of structured inputs (full content stored separately) |
| **tool_calls** | ToolCall[] | Sequence of tool invocations made |
| **outputs** | ActionOutput | Generated content, decisions, escalation |
| **guardrail_decisions** | GuardrailRecord[] | Which guardrails ran, results |
| **outcome** | enum | auto_executed, queued_for_approval, escalated, blocked |
| **downstream_signals** | Signal[]? | Subsequent acceptance, edit, candidate response, etc. |

## 3.24 ConsentRecord

Versioned record of a candidate's consent for each communication channel and purpose. Mutations create new versions; the prior consent state is preserved for audit.

# 4. Key Relationships

The diagram below describes the principal relationships in prose; the engineering team will produce the visual ERD as part of detailed design.

AgencyBrand ─< Branch ─< User
AgencyBrand ─< Candidate >─ PersonIdentity (cross-brand)
AgencyBrand ─< Client    >─ CompanyIdentity (cross-brand)
Client ─< Contact
Client ─< Site
Client ─< Vacancy ─< Placement ─< Shift ─< Timesheet
Vacancy ─< Application
Vacancy ─< Post (per Channel)
Channel ─< Post
Candidate ─< Application
Candidate ─< Conversation ─< Message
Candidate ─< ComplianceDocument
Candidate ─< Placement
Lead ── (conversion) ──> Client
AgentAction is referenced by Message, Vacancy.public_advert_drafts,
Conversation.assigned_agent, Application (where AI screened), etc.

# 5. Multi-Tenancy and Cross-Brand Behaviour

Most data is owned by exactly one AgencyBrand. The exceptions are PersonIdentity, CompanyIdentity, and certain group-level reporting structures.

**Default isolation. **A consultant working for Brand A sees Brand A's candidates, clients, and vacancies. A search returning zero matches must not leak the existence of a record under Brand B.

**Cross-brand visibility (controlled). **Group-level roles can see across brands for reporting, compliance, and sales-intelligence purposes. Cross-brand visibility into PII is logged as an access event.

**Cross-brand candidate visibility. **PersonIdentity makes it possible to see that the same human is registered under two brands. Whether a consultant in Brand A can see the Brand B Candidate record is governed by role and by candidate consent. Default: no cross-brand candidate detail without explicit consent.

**Cross-brand AWR aggregation. **AWR's 12-week qualifying period applies to the same individual irrespective of which brand booked them. The platform aggregates qualifying weeks at PersonIdentity level for compliance — this is one of the few cases where cross-brand data merging is not optional.

**Modern slavery detection. **PersonIdentity-level red flags (same bank account across multiple workers, same controlling phone number, same recruiter introducing many candidates) are evaluated cross-brand. The platform must not have blind spots here just because brands are isolated by default.

# 6. Derived Fields and Scores

Several fields on canonical entities are derived from underlying events, not entered directly. Their derivation must be reproducible and the inputs must be auditable.

- **Candidate.reliability_score. **Derived from completed Shifts vs scheduled Shifts, no-show rate, last-minute cancellations, client feedback signals, document responsiveness. Method documented and versioned; changes to the method are themselves versioned events.

- **Vacancy.compliance_validation. **Result of running the validation suite at last save; recomputed on any field change.

- **Lead.enrichment.predicted_labour_requirements. **Output of the Sales Intelligence enrichment pipeline; method versioned.

- **Placement.awr_qualifying_clock. **Computed from PersonIdentity-level history with the AWR algorithm; computation must be reproducible from the underlying Shift records for audit.

# 7. Event Sourcing for Critical Domains

Three domains are event-sourced because regulators, auditors, and tribunals all may demand reconstruction of historical state.

## 7.1 Financial events

Pay calculations, invoices, credit notes, adjustments. The current state of any pay or bill record is derivable from the event stream. No financial figure is ever silently mutated.

## 7.2 Compliance events

Right-to-work checks, document additions and expiries, AWR clock progressions, Conduct Regs 2003 advert validations, GLAA-relevant events. Required for client compliance audits and for defending decisions taken on compliance grounds.

## 7.3 AI agent events

Every AgentAction is an event. The agent's behaviour over the life of any conversation, vacancy, or candidate must be reconstructible — for trust-dial review, for incident investigation, and for regulatory transparency.

Event sourcing here means: events are immutable; the canonical mutable record reflects current state; current state is reproducible from the event stream by replay; events are stored with sufficient detail to reproduce the relevant business decision.

# 8. Identifier Conventions

- Internal primary keys: UUID v7 (time-sortable). Never exposed externally where avoidable.

- External-facing references: short, human-friendly identifiers per entity type — e.g. VAC-2026-A4F2 — generated from the UUID and a per-tenant prefix.

- Legacy IDs from migration: stored as a structured external_references map on the entity, never as primary keys. Multiple legacy systems supported per entity.

- Government identifiers (Companies House, NI numbers, passport numbers): hashed for matching where used; full values stored only where lawful basis requires.

- Channel-side identifiers (WhatsApp message IDs, Find a Job feed entry IDs, Indeed posting IDs): stored on Post and Message records; never primary keys.

# 9. PII Handling, Retention, and Erasure

The data model is designed with GDPR / UK Data Protection Act 2018 obligations as constraints, not afterthoughts.

**Lawful basis is recorded per data category. **Candidate operational data: contract performance. Marketing communications: consent. Compliance documents: legal obligation. Sales intelligence on company contacts: legitimate interests with assessment recorded.

**Retention periods are enforced. **Default Candidate retention: 24 months from last contact (configurable per agency, with regulatory minima respected). ComplianceDocuments: longer where legally required. ConversationRecords: matched to the underlying entity's retention.

**Right to erasure. **Implemented as a controlled workflow: PII fields are scrubbed from the canonical record; lawful-basis-required records (e.g. payroll history) are retained but pseudonymised; AgentAction events are retained with PII redacted; PersonIdentity is severed but retained for AWR aggregation continuity if required by law. The erasure workflow is itself an event.

**Subject access requests. **Supported by a standard export covering all entities, conversations, agent actions, and consent history pertaining to a PersonIdentity, scoped per brand.

**Cross-border processing. **Where AI model calls cross borders, the data category and lawful basis are checked against the model layer's configured allowed-regions list. High-sensitivity content is routed only to in-region models.

# 10. Migration from Legacy Systems

Each acquired agency arrives with a different legacy system — Bullhorn, RDB, Itris, Mercury, Access Recruitment, custom-built tools, spreadsheets in some cases. The canonical data model is the migration target; legacy schemas are the source.

## 10.1 Migration approach

- **Discovery via MCP. **A read-only MCP connection to the legacy system allows Claude Code to inspect actual record shapes, not just documented ones. Real systems always diverge from their documentation.

- **Mapping document. **Per legacy system, a mapping document records the source-to-canonical field mapping, transformation rules, defaults for missing fields, and known gaps.

- **Staged migration. **Stage 1: read-only mirror of legacy data into the platform alongside its existing system. Stage 2: dual-write while operations validate parity. Stage 3: cutover. Stage 4: legacy decommission.

- **Identity reconciliation. **Before cutover, the migration tooling identifies cross-brand PersonIdentity matches, presents them for review, and creates the cross-brand links. This is critical and must not be done silently.

- **Compliance documents. **Legacy compliance documents are migrated with their original timestamps and source references; expiry dates trigger immediate re-validation if past or approaching.

## 10.2 Per-acquisition migration playbook

Every acquisition uses the same playbook, executed via Claude Code in plan mode:

- Stand up MCP read access; produce data inventory.

- Generate mapping document; review with operational lead from acquired agency.

- Generate migration scripts via the "generate a migration script from legacy schema X" skill.

- Run dry-run migration to UAT; reconcile counts and spot-check records.

- Identity reconciliation review.

- Dual-write stage.

- Cutover with rollback plan.

- Legacy decommission after 90-day quiet period.

# 11. Engineering Practices in Claude Code

**Schema as the contract. **The canonical entities are defined as code in a /schema directory at the repository root. Every module imports from this single source. Schema changes require a versioned migration and a paired sign-off.

**Schema-aware sub-agents. **Module sub-agents in Claude Code reference the same schema files. The root CLAUDE.md points to the schema; module CLAUDE.mds inherit.

**Migration skill. **A custom skill produces migration script scaffolding given a legacy schema description and an MCP connection to the legacy system.

**Schema change hook. **A pre-commit hook detects changes to /schema and requires a paired migration file plus impact assessment. Schema changes never merge without a migration script.

**Event store as background concern. **Event sourcing for financial, compliance, and agent domains is implemented behind a clean interface. Background agents handle event projections to read models without blocking the main request flow.

**Plan mode for migrations. **Each acquired-agency migration begins in plan mode; the plan is the migration playbook.

# 12. Open Questions for Resolution Before Build

- Confirm the canonical job-title taxonomy — bought-in (e.g. SOC codes), industry-standard (e.g. CITB), or platform-defined.

- Decide event store technology — application-level (e.g. EventStoreDB), database-native (Postgres + outbox), or workflow-engine native (Temporal histories where applicable).

- Resolve PersonIdentity matching policy — automatic exact match only, or fuzzy match with human review threshold.

- Confirm legal advice on cross-brand AWR aggregation as a lawful basis for cross-brand data linkage.

- Decide retention defaults per data category, with operational and legal sign-off.

- Choose the first acquired agency's legacy system as the reference migration; this dictates the order of MCP wrappers and migration skills built in Phase 1.

*End of specification — Canonical Data Model v1.0*
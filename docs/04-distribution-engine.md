**Technical Specification**

**Multi-Channel Distribution Engine**

*including Find a Job (DWP) integration*

*Adapter architecture, channel-by-channel implementation, response routing*

Companion document to: UROP Project Brief v1.2

Audience: Engineering leads and Distribution module team

Version: 1.0

# 1. Purpose and Scope

This document specifies the architecture and per-channel implementation of the Multi-Channel Distribution Engine described in §3.5 of the main project brief. It covers the adapter pattern that lets us add and retire channels without disturbing the rest of the platform, the deep specification for the Find a Job (DWP) feed which is the highest-priority channel for our worker demographic, and the response router that funnels applications back into the ATS.

Read alongside the AI Content & Engagement Layer spec (which produces the content this engine distributes) and the canonical data model spec.

# 2. Design Principles

**One canonical Vacancy, many channel projections. **The Vacancy record in the ATS is the single source of truth. Adapters project it into channel-specific formats. The Vacancy never holds channel-specific copy.

**Adapters are isolated, swappable, and individually owned. **Each channel adapter is a self-contained module with its own credentials, rate limiter, content rules, ToS document, and test suite. Retiring a channel means removing one folder; adding one means scaffolding from a template.

**Compliance is enforced before dispatch, not after. **A vacancy that fails AWR pay disclosure or Conduct Regulations 2003 identification cannot leave the platform on any channel.

**Platform terms of service are first-class artifacts. **Each adapter folder contains a TOS.md documenting the platform's automation rules, last reviewed date, and named owner. Reviewed quarterly.

**All inbound responses route to the same place. **Regardless of which channel a candidate replies on, the response lands in the unified inbox attached to the relevant vacancy and candidate record.

# 3. Architecture

## 3.1 Components

- **Distribution Orchestrator. **Receives publish requests, fans out to selected adapters, manages scheduling, throttling, and retries.

- **Channel Adapters. **One per channel. Implements a stable internal interface — see §3.3.

- **Content Validation Service. **Runs compliance checks before any adapter is invoked.

- **Asset Generation Service. **Renders branded images for visual channels (Instagram, LinkedIn, Facebook). Server-side templated rendering.

- **Response Router. **Inbound webhooks and polling endpoints from each channel; matches replies to candidates and vacancies, drops them into the unified inbox.

- **Channel Analytics Pipeline. **Captures impressions, clicks, applies, and downstream placements per channel for ROI reporting.

## 3.2 Publish flow

1. Vacancy created/updated in ATS
2. Distribution Orchestrator receives publish request with target channels
3. Content Validation Service runs compliance gates
   ↳ failure → return errors to AI agent or consultant; nothing dispatched
4. AI Content Agent produces channel-specific drafts
5. Each draft passes through the adapter's local validators
6. Asset Generation Service produces images/media as needed
7. Adapters are invoked (parallel where rate limits permit)
8. Each adapter returns post identifiers and timestamps
9. Channel-side post records created, linked to Vacancy
10. Response Router begins listening for replies/applications

## 3.3 Adapter Interface

Every adapter implements the same internal interface, expressed here in pseudo-TypeScript:

interface ChannelAdapter {
  readonly channelId: string;
  readonly tosVersion: string;

  validate(draft: ChannelDraft): ValidationResult;
  publish(draft: ChannelDraft): Promise<PublishResult>;
  unpublish(postId: string): Promise<void>;
  getMetrics(postId: string): Promise<PostMetrics>;
  getRateLimits(): RateLimitConfig;
  getContentConstraints(): ContentConstraints;
}

The interface is intentionally narrow. Adapters do not see the canonical Vacancy directly — they receive a ChannelDraft prepared by the AI Content Agent and the Asset Generation Service.

# 4. Channel Catalogue

This section is a reference table. Each channel has its own adapter folder containing implementation, TOS document, content constraints, rate limit configuration, and tests.

| **Channel** | **Mechanism** | **Format** | **Automation posture** | **Priority** |
| --- | --- | --- | --- | --- |
| **Find a Job (DWP)** | XML/CSV bulk feed | Long-form structured listing | Fully automated | P0 — launch with platform |
| Indeed | XML feed + sponsored API | Structured listing | Fully automated | P0 |
| Reed / CV-Library / Totaljobs | Direct API or multiposter | Structured listing | Fully automated | P1 |
| WhatsApp Business | Cloud API | Templates / broadcast lists / Channels | Strict template + 24h window | P0 |
| Telegram | Bot API | Channel and group posts | Fully automated (own channels) | P1 |
| Facebook Pages | Meta Graph API | Page posts + image | Fully automated (own pages) | P1 |
| Facebook Groups | Meta Graph API | Group posts + image | Owned/admin'd groups only | P2 |
| Instagram | Meta Graph API | Image post with caption | Fully automated (business accounts) | P1 |
| LinkedIn Pages | Marketing API | Page post | Fully automated (company pages) | P1 |
| LinkedIn personal | Draft for one-tap publish | Post drafts in consultant queue | Manual publish required by ToS | P2 |
| TikTok Business / X | Native APIs | Short video / short post | Fully automated | P3 — evaluate later |
| SMS | Twilio or similar | 160-char message | Fully automated, consent-gated | P0 |
| Email | Transactional ESP | Templated email | Fully automated, consent-gated | P0 |

# 5. Find a Job (DWP) — Deep Specification

Find a Job is the UK government's free job board, operated by DWP. It is the single highest-leverage distribution channel for our blue-collar temp demographic because it is the destination JobCentre Plus work coaches direct Universal Credit claimants to. Vacancies posted there reach candidates we struggle to reach commercially, at zero cost per advert.

## 5.1 Mechanism

Find a Job accepts vacancies from third-party recruiters either through manual entry or through a bulk upload XML/CSV feed hosted at a stable URL that DWP polls on a published schedule. We will use the feed mechanism. Each acquired agency holds its own DWP account; the platform generates one feed per agency brand, hosted at a per-brand stable URL.

Note for engineering: the exact technical contract — feed schema, polling cadence, accepted field values — is defined by DWP and must be confirmed against current DWP documentation at build time. The team should make first contact with DWP's employer support during Phase 1 to register the agency accounts and obtain current feed specifications. This is on the critical path.

## 5.2 Feed Adapter Components

- **Feed Generator. **On a configurable schedule (default every 30 minutes), queries the Distribution Orchestrator for vacancies marked for Find a Job publication, transforms each into the DWP-required schema, and writes the feed to a stable URL.

- **Feed Validator. **Validates every generated feed against the DWP schema before it is published. A failed validation blocks publication and raises an alert; it does not silently produce a broken feed.

- **Feed History Store. **Every published feed is archived. This is essential for audit and for diagnosing rejections from DWP.

- **DWP Status Listener. **If DWP exposes a callback or status mechanism on individual vacancies (acceptance, rejection, expiry), the adapter consumes those and updates the canonical Vacancy with the current Find a Job status.

- **Application Inbox Adapter. **Find a Job applications typically arrive by email to a configured address. The Application Inbox Adapter consumes that mailbox, parses applicant details (name, contact, CV attachment), and routes them to the Response Router.

## 5.3 Compliance Gates Specific to Find a Job

Find a Job has stricter content rules than commercial boards. The Content Validation Service applies these gates before a vacancy can be queued for the feed.

- **Pay disclosure. **Pay must be specified accurately, including holiday pay treatment where relevant under AWR. "Competitive" or "DOE" without numeric value is rejected.

- **Location accuracy. **Site location must be specific enough to be searchable; postcode-area or town required, vague entries ("Various", "South East") rejected.

- **Discriminatory language. **Equality Act 2010 banned-phrase list applied; common construction-industry phrasing risks ("young energetic team", "strong lad") explicitly screened.

- **Agency identification. **Conduct Regulations 2003 — agency must be clearly identified. The compliance module holds the canonical identifier per brand.

- **No keyword stuffing. **Job title must be a recognisable trade/role; titles like "Groundworker / Labourer / CSCS / £20ph / Start ASAP / Various Sites" are stripped to "Groundworker" and supporting detail moved to body.

- **Single vacancy per record. **If the underlying need is for 20 groundworkers across 4 sites, the Distribution Orchestrator splits it into the Find a Job feed appropriately rather than concatenating sites in one listing.

## 5.4 Operational Considerations

- Account onboarding with DWP can take longer than the technical build. Open this conversation in Phase 1.

- Each agency brand maintains its own DWP relationship; the platform serves one feed per brand.

- Feed availability is part of platform uptime — a feed URL that returns errors causes DWP to skip that polling cycle.

- Application volume from Find a Job for blue-collar trades can be very high; the Application Inbox Adapter must be designed for scale and for de-duplication (the same candidate often applies to multiple of our vacancies).

# 6. WhatsApp Business — Implementation Notes

WhatsApp is the highest-traffic channel for candidate engagement and the most operationally constrained. The adapter must be built carefully because mistakes risk number suspension that would be commercially severe.

## 6.1 Mechanism

Use the WhatsApp Business Cloud API. Each agency brand operates one or more business numbers, each registered through the platform's Meta Business account.

## 6.2 Outbound rules

- **Outside the 24-hour window. **Outbound messages outside the customer-service window must use approved Message Templates. Templates are versioned and per-brand.

- **Inside the 24-hour window. **Free-form messages are permitted. The Candidate Engagement Agent operates here.

- **Group posting. **Automated posting into community WhatsApp groups is restricted by Meta. The architecture favours broadcast lists and WhatsApp Channels (one-to-many) over scraping group invite links.

- **Opt-out compliance. **Candidate replies of STOP/UNSUBSCRIBE/equivalent are honoured immediately and persist across the platform.

## 6.3 Template Catalogue

Templates are stored in /distribution/whatsapp/templates/, versioned, and reviewed for compliance. Indicative starter set:

- vacancy_alert — outbound new vacancy alert with apply CTA

- shift_confirmation — day-before reminder with reply-YES confirmation

- document_request — request for missing right-to-work / card document

- re_engagement — dormant candidate re-activation with relevant vacancy

- compliance_followup — card expiry warning with renewal nudge

# 7. Response Router

The Response Router is the inbound side of the Distribution Engine. Every channel produces replies in different formats; the router normalises them into Application and Conversation events in the unified inbox.

## 7.1 Inputs

- Find a Job — emailed applications with CV attachment

- Indeed — apply-by-email or webhook depending on configuration

- Job board direct integrations — webhook-based

- WhatsApp / Telegram / Messenger / IG DMs — webhook-based

- Facebook Page comments and Instagram comments — Graph API webhooks

- LinkedIn — InMail forwarding inbox

- SMS — Twilio webhook

- Email — IMAP / ESP webhook

## 7.2 Processing

1. Inbound message received via webhook or polling
2. Channel-specific parser extracts: sender identity, content, attachments
3. Candidate matching:
   ↳ exact phone/email match → existing Candidate record
   ↳ fuzzy match (name + partial details) → human review queue
   ↳ no match → create Candidate stub flagged for enrichment
4. Vacancy attribution:
   ↳ if reply contains tracking token → linked to specific Vacancy
   ↳ otherwise → linked to most recent outbound from this candidate
   ↳ ambiguous → human review queue
5. Conversation event created in unified inbox
6. Routed to Candidate Engagement Agent or human consultant per trust dial

## 7.3 De-duplication

Candidates routinely apply to several of our vacancies and reply across multiple channels. The router de-duplicates at three levels: identical message in same conversation within 5 minutes (suppressed), same candidate replying to same vacancy across channels (consolidated), and same candidate creating multiple stubs (merged via the candidate-matching pipeline).

# 8. Scheduling and Throttling

Every channel imposes rate limits, both technical and reputational. The orchestrator's scheduling layer manages both.

## 8.1 Per-channel rate limits

Each adapter declares its rate limits. The orchestrator throttles dispatch to stay within them with margin to spare. Hitting platform rate limits risks account suspension; staying well clear is a deliberate cost.

## 8.2 Per-candidate frequency caps

A candidate should not receive the same vacancy across five channels in the same hour. The orchestrator applies cross-channel frequency caps per candidate per vacancy and per candidate per 24h, configurable per agency.

## 8.3 Time-of-day rules

SMS and WhatsApp outbound respects per-agency quiet hours (default 8pm–8am local time). Marketing-style email shots respect time-zone-appropriate send windows. Adverts to public job boards run continuously — no quiet hours.

## 8.4 Burst protection

If an AI agent or a bulk operation requests an unusually large number of dispatches in a short window, the orchestrator queues with backpressure and alerts the on-call engineer. This protects against runaway loops or misconfigured campaigns.

# 9. Channel Analytics

The Channel Analytics Pipeline captures the metrics needed to make commercial decisions about which channels to invest in.

- Impressions / reach (where channels expose it)

- Click-through to apply

- Applications received

- Application-to-shortlist conversion

- Shortlist-to-placement conversion

- Cost per application by channel (free channels = 0; sponsored slots tracked against spend)

- Time-to-first-application by channel

- Placement gross profit attributed to channel

These feed both the operational reporting dashboards and the Distribution Orchestrator's own optimisation logic — over time the orchestrator can learn which channels are most effective for each trade and region, and weight dispatch accordingly.

# 10. Engineering Practices in Claude Code

**Per-channel sub-agents. **Each channel adapter has a dedicated Claude Code sub-agent with its own CLAUDE.md, capturing the platform's API contract, ToS constraints, content rules, and rate limits. The Find a Job sub-agent in particular has the longest CLAUDE.md given DWP's specific requirements.

**MCP wrappers around platform APIs. **Development MCP servers for the WhatsApp Cloud API, Telegram Bot API, Meta Graph API, and Find a Job feed validator allow each adapter to be developed and tested with realistic responses inside the Claude Code loop.

**Adapter scaffolding skill. **A custom skill — "scaffold a new channel adapter" — produces the standard adapter folder structure (implementation stub, TOS.md, content_constraints.json, rate_limits.json, tests/, fixtures/) with the right interface boilerplate.

**Plan mode for new adapters. **Adding a new channel begins in plan mode, documenting the platform's terms of service, rate limits, and authentication model before any code is written. The plan is reviewed against legal and compliance before implementation begins.

**Hooks for protected paths. **Pre-commit hooks block changes to TOS.md without paired sign-off, and block any direct write to live channel credentials without the on-call engineer's approval.

**Background feed generation agent. **The Find a Job feed generator is a background agent that runs on schedule, with full observability piped to the team's monitoring channel.

# 11. Open Questions for Resolution Before Build

- Confirm DWP feed specification against current Find a Job documentation; book the introductory call with DWP employer support.

- Decide on multiposter dependency vs direct integrations for paid job boards. Multiposter accelerates time-to-market; direct integrations give better data and lower per-post cost at scale.

- Decide on Meta Business account topology — one umbrella account with brands as sub-accounts, or one Meta Business account per acquired agency. Affects WhatsApp number registration and Page management.

- Confirm the supported list of umbrella WhatsApp numbers per agency at launch. Each number requires Meta Business verification.

- Define the canonical job-title taxonomy. Cross-channel consistency depends on it.

- Agree the per-agency frequency caps and quiet hours with operational leadership.

*End of specification — Multi-Channel Distribution Engine v1.0*
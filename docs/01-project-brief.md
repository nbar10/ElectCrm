**Project Brief**

**Unified Recruitment Operations Platform (UROP)**

*A bespoke, AI-augmented operating platform for a consolidated blue-collar temp recruitment group*

Prepared for: Internal Tech Team

Build Environment: Claude Code (with advanced agentic features)

Document Type: Foundational Project Brief

Version: 1.2

# 1. Executive Summary

We are consolidating multiple acquired blue-collar temporary recruitment agencies onto a single, bespoke operating platform. Each acquired business currently runs disparate tooling — different CRMs, ATSs, timesheet systems, and payroll integrations — creating duplicated back-office headcount, fragmented data, and lost commercial opportunity.

This project will deliver a unified, modular, AI-augmented platform that replaces the patchwork of legacy tools with one coherent system covering CRM, ATS, timesheets, payroll integration, compliance, multi-channel job distribution, AI-driven candidate engagement, and a proprietary sales intelligence engine that scrapes and enriches public construction and project data (in the spirit of Glenigan and Barbour ABI) to put our sales teams ahead of competitors on lead generation.

A defining principle of the platform is that routine, repetitive consultant work — writing job adverts, replying to candidate messages, screening for basic qualifying criteria, chasing for documents, confirming shifts — is performed by AI agents, with humans intervening only for exceptions and for the high-value relationship moments that justify a consultant's time. This is the single biggest lever for reducing back-office headcount per £m of revenue.

The platform will be built using Claude Code as the primary development environment, leveraging its agentic capabilities, sub-agents, MCP integrations, and parallel task orchestration to compress build timelines and reduce engineering overhead.

# 2. Strategic Objectives

The platform must deliver against four commercial priorities.

**Back-office cost reduction. **We expect a measurable decrease in administrative and consultant headcount per £m of revenue placed, achieved through automation of timesheet collection, payroll prep, compliance checking, job advert authoring, candidate communications, and job advertising.

**Data unification. **Candidates, clients, placements, and margins must be reportable consistently across the group, regardless of which acquired agency originated the record.

**Sales acceleration. **Proprietary lead generation should surface construction and infrastructure projects before competitors have them on their radar.

**Scalability for future acquisitions. **Onboarding a newly acquired agency onto the platform should take weeks, not quarters.

# 3. Core Functional Modules

## 3.1 Unified CRM

A single client and prospect database covering main contractors, sub-contractors, civils firms, M&E specialists, facilities managers, and end clients. Must support multi-branch ownership (so London branch and Manchester branch can both work the same parent client without conflict), activity logging, pipeline stages tailored to temp recruitment sales cycles, and contact intelligence enriched from the lead-generation engine described in §3.7.

## 3.2 Applicant Tracking System (ATS)

Candidate records with right-to-work documentation, CSCS/CPCS/NPORS card tracking with expiry alerts, trade skills tagging, availability calendars, geographic radius preferences, and historic placement performance. Must handle high-volume temp workflows — bulk SMS and WhatsApp outreach for shift fills, one-tap candidate confirmation, and automated re-engagement of dormant workers. AI-assisted candidate-to-vacancy matching should rank workers by skill fit, distance, reliability score, and current availability.

## 3.3 Timesheet Capture & Approval

Mobile-first timesheet submission for workers (with geofencing on clock-in to validate site presence), client-side approval workflows (email link, WhatsApp, or portal), exception handling for disputes, and automatic conversion of approved hours into pay and bill calculations using configurable rate cards per assignment.

## 3.4 Payroll & Finance Integration

Out-of-the-box integrations with the major UK temp payroll providers (Sage, Xero, Brightpay, plus umbrella company APIs such as Paystream, Brookson, and similar) via a normalised internal schema. Pay/bill must handle PAYE, CIS, umbrella, and Ltd company workers. Self-bill invoicing and credit control workflows feed back into the CRM.

## 3.5 Multi-Channel Job Distribution Engine

A consultant should write a vacancy once in the ATS — or, increasingly, simply describe it in a sentence — and have it distributed automatically, in the right format, to every relevant channel.

**Channels in scope:**

- **Find a Job (DWP) — highest priority. **The UK government's official free job board. Posting must be automated via Find a Job's bulk upload feed (XML/CSV multi-vacancy feed submitted on a scheduled basis). The platform must generate a compliant feed, host it at a stable URL, and manage agency account relationships with DWP. Compliance with Find a Job's content rules (no discriminatory language, accurate pay rates including holiday pay disclosure for AWR, clear location, no agency name-stuffing) must be enforced at validation time.

- **Paid job boards **— Indeed, Reed, CV-Library, Totaljobs via multiposting or direct XML feeds.

- **WhatsApp Business **— broadcast lists and Channels segmented by trade and region, via the WhatsApp Business Cloud API.

- **Telegram **— channels and groups via the Telegram Bot API; particularly valuable for Eastern European trades.

- **Facebook Pages and owned Groups **via the Meta Graph API.

- **LinkedIn Pages **via the Marketing API; personal posts via a draft-and-one-tap-publish flow rather than full automation.

- **Instagram **— auto-generated branded vacancy images via the Meta Graph API.

- **TikTok and X **— scheduled posting from business accounts where worthwhile.

- **SMS and email shots **to consented candidate segments.

The Distribution Engine is fed directly by the AI Content & Engagement Agent described in §3.6, which authors and tailors copy per channel.

## 3.6 AI Content & Engagement Layer

*This module replaces meaningful portions of the work currently done by consultants and resourcers. It is ****not**** an AI assistant sitting alongside humans — it is a set of autonomous agents that own specific workflows end to end, with a human-in-the-loop only at defined escalation points. There are four agent families.*

### Job Advert Authoring Agent

A consultant or branch manager creates a vacancy by entering only the structured essentials — trade, site location, start date, duration, pay rate, shift pattern, required cards/tickets, client (internal only). The agent produces channel-specific adverts: a long-form, AWR-compliant Find a Job listing; a punchy Indeed listing tuned to its algorithm; a WhatsApp broadcast under the character limit with a clear call-to-action; a Telegram channel post with appropriate emoji conventions; a LinkedIn post in the right professional register; an Instagram caption with a generated branded image; an SMS shot under 160 characters.

The agent enforces compliance rules at draft time (AWR pay disclosure, Conduct Regulations 2003 agency identification, no discriminatory language under the Equality Act 2010, GLAA-relevant phrasing where applicable). Outputs are queued for posting; for the first weeks of operation a human approves each one, after which an agency-by-agency confidence threshold permits auto-publishing of routine vacancies while flagging unusual ones (high pay rate, sensitive sectors, new client) for human review. The agent learns from edits — if consultants consistently rewrite a particular phrasing, future drafts adapt.

### Candidate Engagement Agent

This is the largest single headcount lever. The agent handles inbound and outbound candidate conversations across WhatsApp, Telegram, Facebook Messenger, Instagram DMs, SMS, and email, using a single unified inbox internally. It conducts first-line conversations end to end — answering questions about a vacancy ("is the rate CIS or PAYE?", "do I need my own PPE?", "how do I get to the site?"), screening candidates against the vacancy's must-haves (right to work, valid card, distance, availability), collecting missing documents by asking for them and accepting photo uploads back through the same channel, confirming shifts the day before with a "reply YES to confirm" flow, and re-engaging dormant candidates with relevant new vacancies.

The agent operates with clear escalation rules: anything involving a pay dispute, a complaint, a safeguarding concern, an accident or near-miss on site, suspected modern slavery indicators, or any message expressing distress is routed instantly to a human consultant with the conversation context attached. The agent never fabricates information it does not have — if a candidate asks something it cannot verify, it says so and escalates.

It is multilingual by default, which matters: Polish, Romanian, Bulgarian, Lithuanian, Ukrainian, and Portuguese speakers form a significant share of the UK construction temp workforce, and conversations should happen in the candidate's preferred language with translations available to the consultant on escalation.

### Client Communication Agent

A more conservative variant of the engagement agent, handling routine client-side admin: sending shift confirmations, chasing timesheet approvals, acknowledging vacancy briefs and converting them into structured Vacancy records, sending compliance documentation packs on request. Anything that touches commercial terms, complaints, or new business is escalated to the named consultant. Tone is calibrated per client — a tier-1 main contractor expects different register from a small sub-contractor.

### Sourcing & Matching Agent

When a vacancy is created, this agent proactively shortlists candidates from the ATS using semantic matching, ranks them, and instructs the Candidate Engagement Agent to reach out — typically within minutes of the vacancy going live. It can also identify candidates whose CSCS/CPCS cards are about to expire and trigger renewal nudges before the expiry breaks their availability.

### Cross-cutting design principles for the AI layer

All agent actions are logged, auditable, and reversible where possible. Every outbound message is stored against the candidate or client record with the model version, prompt template version, and reasoning summary.

The platform maintains a trust dial per agent per workflow — initially every action requires human approval, and as confidence builds (measured by approval rates, candidate complaint rates, conversion rates) the dial moves toward autonomy, per agency and per workflow independently.

**Hard guardrails **that never relax regardless of trust level: no agent ever sends a message making a commitment on rate, start date, or terms beyond what is in the canonical Vacancy record; no agent ever asks for bank details, ID document numbers, or other sensitive information through unsecured channels — those are handled via secure document upload links into the ATS; no agent ever posts to a channel where the platform's terms of service prohibit automation.

Candidates must be informed clearly that they may be speaking with an AI, with a one-tap "speak to a human" path always available — this is both an ethical commitment and a likely regulatory direction under the EU AI Act and emerging UK guidance.

## 3.7 Sales Intelligence Engine

The strategic crown jewel. It replicates and extends what Glenigan and Barbour ABI offer, but tuned specifically for our temp recruitment sales motion. It will ingest data from UK and devolved planning portals, the Planning Inspectorate, Companies House, contract award notices (Contracts Finder, Find a Tender, Sell2Wales, eTendersNI), trade press, industry awards, and public infrastructure announcements (HS2, National Highways, Network Rail, AMP cycles for water utilities, etc.).

Data is normalised, deduplicated, geocoded, and enriched with predicted labour requirements (e.g. a £40m residential scheme reaching groundworks in Q3 implies demand for groundworkers, dumper drivers, and steel fixers in a specific postcode radius). Leads are routed to the relevant branch and salesperson based on geography, sector, and existing relationships in the CRM. AI agents in this engine also draft outbound prospecting messages for sales consultants to review and send.

## 3.8 Compliance & Audit

Centralised storage of right-to-work checks, AWR tracking (12-week qualifying period), GLAA licensing where relevant, modern slavery flags, and audit trails for client compliance reviews — including full audit trails of every AI agent action. Critical for retaining tier-1 contractor PSL status. The compliance module gates both the Distribution Engine and the AI Content & Engagement Layer — a vacancy that fails AWR pay disclosure cannot be drafted, posted, or sent to candidates.

## 3.9 Reporting & Analytics

Cross-agency dashboards covering GP per consultant, fill rates, time-to-fill, candidate churn, client concentration, margin erosion alerts, channel ROI, and AI agent effectiveness metrics — auto-resolution rate, escalation rate, candidate satisfaction signals, advert performance by AI-drafted versus human-drafted (during the transition period). Board-level rollups plus branch-level drilldowns.

# 4. Architectural Principles

The system should be built as a modular monolith initially, with clear service boundaries so modules can be extracted into microservices as load demands. PostgreSQL as primary store with event sourcing for critical financial, compliance, and AI agent action events. All inter-module communication via well-defined internal APIs.

**A canonical data model **sits at the centre — Candidate, Client, Contact, Vacancy, Placement, Shift, Timesheet, Invoice, Lead, Channel, Post, Application, Conversation, AgentAction — and every legacy system migration maps into this model.

The AI Content & Engagement Layer is architected as a set of agent services sharing a common framework: a tools layer (read/write to ATS, send to channel, escalate to human, request document, schedule follow-up), a memory layer (per-candidate conversational history, per-agency style preferences), a guardrail layer (compliance checks, hard rules, tone enforcement), and an observability layer (every action traced and replayable). The Distribution Engine is the agent layer's primary outbound tool; the unified messaging inbox is its primary inbound tool.

Authentication via SSO with role-based access control scoped by branch, agency brand, and function. Multi-tenant by design so each acquired brand can retain its customer-facing identity — including its own AI agent persona, tone, and signature — while running on shared infrastructure.

# 5. Why Claude Code, and How to Use It

This build is unusually well-suited to Claude Code's agentic features, and the AI Content & Engagement Layer reinforces that fit because the production AI agents share architectural DNA with the development sub-agents.

**Use sub-agents for parallel module development. **Spin up dedicated sub-agents per module (CRM, ATS, Timesheets, Distribution, AI Engagement, Sales Intelligence, Compliance) each with their own scoped context, conventions document, and test suite. Within the AI Engagement module, further sub-agents per agent family (Advert Authoring, Candidate Engagement, Client Communication, Sourcing & Matching).

**Use MCP servers to connect Claude Code to live systems during development. **MCP wrappers around legacy CRMs and ATSs for migration, around Companies House and planning portals for sales intelligence, and around the WhatsApp Cloud API, Telegram Bot API, Meta Graph API, and Find a Job feed validators for the Distribution and Engagement layers. During development, MCP servers can also wrap the production AI agents themselves so that Claude Code can introspect their behaviour, replay conversations, and tune prompts and guardrails inside the development loop.

**Use plan mode for migration playbooks, adapter rollouts, and trust-dial increases on AI agents. **Moving an agent from "draft for human review" to "auto-publish for routine cases" is a structured change that benefits from a written plan, defined rollback criteria, and human checkpoints.

**Use skills and CLAUDE.md files aggressively. **Maintain a root CLAUDE.md covering coding standards and the canonical schema. Per-module CLAUDE.md files capture module-specific conventions. The AI Engagement module needs a particularly detailed CLAUDE.md covering agent design principles, escalation rules, prompt versioning conventions, and the hard guardrails that must never be edited without explicit sign-off. Build custom skills for: scaffolding a new channel adapter, scaffolding a new AI agent with guardrails, generating a migration script from a legacy schema, adding a new lead source to the sales intelligence engine, and tuning an existing agent's tone for a specific brand.

**Use background agents for long-running tasks **— scraping pipelines, large data migrations, the Find a Job feed generator, and the continuous evaluation harness that replays sample conversations against new agent prompt versions.

**Use git worktrees with parallel Claude Code sessions **so module teams move independently.

**Use hooks for safety rails. **Pre-commit hooks for linting and type-checking, plus custom hooks that block any change to AI agent guardrails without a paired sign-off file, and any direct write to production data sources or live social and messaging accounts unless explicitly approved.

# 6. Recommended Technology Stack

Backend in TypeScript (Node.js) or Python — pick one and stick to it across modules. PostgreSQL as primary store. Redis for queues and caching. A workflow engine (Temporal or similar) for long-running processes including multi-step agent workflows. A vector database (pgvector is sufficient initially) for semantic candidate-to-vacancy matching, lead-to-client matching, and retrieval over historic conversations to inform agent responses.

A model layer that abstracts which LLM is called per task — Claude for high-stakes drafting and engagement, smaller and cheaper models for high-volume classification and routing — so the team can swap providers as the market evolves. An evaluation harness (Promptfoo or a bespoke equivalent) that runs every prompt change against a held-out set of real conversations before deployment.

Frontend in React. Mobile timesheet app as React Native or PWA. Server-side image generation for branded social posts via Puppeteer or similar. Infrastructure as code via Terraform on AWS or Azure. Secrets vault for the many third-party credentials.

# 7. Phasing

**Phase 1 — Foundation (weeks 1–8). **Canonical data model, authentication, multi-tenancy, core CRM, core ATS, first agency migrated as a proof point.

**Phase 2 — Operational core (weeks 9–16). **Timesheets, payroll integrations, compliance module, next two agencies migrated, Find a Job adapter live, Job Advert Authoring Agent live in human-approval mode so consultants experience AI drafting from day one of operational use.

**Phase 3 — Distribution and Engagement (weeks 13–24, overlapping Phase 2). **Remaining channel adapters; Candidate Engagement Agent rolled out per channel, starting with SMS and WhatsApp where conversation patterns are best understood, then Telegram, then Meta DMs. Unified inbox, escalation routing, and the trust-dial framework.

**Phase 4 — Sales intelligence (weeks 17–28). **Lead generation engine and sales-side AI drafting.

**Phase 5 — Trust expansion and optimisation (weeks 25+). **Move agents from human-approval to supervised autonomy on routine workflows, agency by agency. Predicted labour demand modelling. Client Communication Agent rollout, which is more conservative because client trust is harder won than candidate trust. Continuous evaluation and tuning.

# 8. Legal and Ethical Considerations

The AI Content & Engagement Layer carries the heaviest regulatory and reputational load in this platform and must be designed accordingly.

**Transparency. **Candidates and clients must be told clearly when they are communicating with an AI agent, with frictionless escalation to a human at any point. This aligns with emerging UK regulator expectations and the EU AI Act's transparency obligations for AI systems interacting with natural persons.

**Equality Act 2010. **AI-drafted adverts and AI-conducted screening must not introduce discriminatory language or biased filtering. Screening logic must be explainable and auditable, and protected characteristics must never be used as filtering criteria. The compliance module enforces this at draft time and at screening time, and bias testing should be part of the evaluation harness.

**Conduct of Employment Agencies and Employment Businesses Regulations 2003. **Every advert must clearly identify the agency and accurately represent the role and pay. The Job Advert Authoring Agent's guardrails enforce this.

**AWR (Agency Workers Regulations 2010). **Pay disclosure rules apply to adverts and to candidate conversations — the agent must not misstate or evade.

**GDPR / UK Data Protection Act 2018. **Conversations contain personal data; lawful basis, retention periods, and data subject rights (access, erasure, rectification) must be designed in. Cross-border model calls must be assessed for international transfer compliance.

**Online Safety Act and platform terms of service. **WhatsApp, Meta, LinkedIn, and TikTok all have specific rules about automated messaging. The Engagement Agent operates within those rules, not around them — for example, on WhatsApp it uses the Business Platform's approved template messages for outbound and respects the 24-hour customer service window for free-form replies.

**Modern Slavery Act 2015. **Construction temp recruitment is a higher-risk sector. Agent training data, escalation triggers, and red-flag detection (workers controlled by third parties, identical bank details across multiple workers, signs of distress in messages) must support — not undermine — the agency's modern slavery obligations.

**Sales Intelligence terms of service. **Public planning and government contract data is reusable; commercial aggregators are not scraping targets — primary sources only.

**Acquisition data (TUPE-style) GDPR. **Lawful basis for processing migrated candidate data must be confirmed with counsel before each migration.

# 9. Success Metrics

Back-office and consultant FTE per £1m turnover (the headline metric); time-to-fill; GP per consultant; lead-to-placement conversion from the Sales Intelligence engine; applications-per-vacancy and cost-per-application by channel; AI agent auto-resolution rate, escalation rate, candidate-reported satisfaction, complaint rate, and edit-rate on AI-drafted adverts; agency onboarding time per acquisition; system uptime.

Targets to be set with operational leadership once baseline data from the first migrated agency is available.

# 10. Immediate Next Steps for the Tech Team

- Stand up the repository with a root CLAUDE.md and module skeletons, including an AI Engagement module skeleton with sub-folders for each agent family and a guardrails/ directory protected by hooks.

- Pick the first acquired agency for migration — ideally one with a moderately sized, reasonably clean dataset rather than the largest or messiest.

- Stand up MCP connections to that agency's existing CRM and ATS for read-only discovery.

- Draft the canonical data model — including Vacancy, Channel, Post, Application, Conversation, and AgentAction entities — and circulate for review before any code is written against it.

- Open the conversation with DWP about Find a Job feed setup early; account onboarding and feed validation can take longer than the technical build.

- Begin assembling a corpus of real (anonymised) past candidate conversations and consultant-written adverts from the first acquired agency to use as training and evaluation data for the AI agents — this is on the critical path and depends on legal sign-off, so start now.

- Schedule a kickoff to walk through this brief, agree the stack, and assign module ownership including a named owner for the AI Engagement layer specifically, given how much of the commercial thesis rests on it.

*End of brief*
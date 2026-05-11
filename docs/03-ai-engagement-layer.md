**Technical Specification**

**AI Content & Engagement Layer**

*Architecture, agent design, guardrails, and rollout protocol*

Companion document to: UROP Project Brief v1.2

Audience: Engineering leads and AI Engagement module team

Version: 1.0

# 1. Purpose and Scope

This document specifies the architecture, behavioural design, guardrails, and operational protocol for the AI Content & Engagement Layer described in §3.6 of the main project brief. It is the most commercially load-bearing module in the platform — back-office and consultant headcount reduction depends largely on this layer working safely and reliably at scale.

The audience is the engineering team building the module, the compliance officer signing off on guardrails, and operational leaders accountable for the consequences of agent behaviour. Read alongside the canonical data model spec and the Distribution Engine spec.

# 2. Design Philosophy

Five principles govern every design decision in this layer.

**Agents own workflows, not tasks. **An assistant that drafts a message and waits for a human to send it saves seconds; an agent that owns the entire "shift confirmation for tomorrow" workflow saves a job. Build for the latter.

**Trust is earned per workflow, not granted to the system. **Each agent has its own trust dial per workflow per agency. New agents start in human-approval mode. Autonomy expands only when measurable evidence justifies it, and can be revoked at any time.

**Guardrails are code, not prompts. **Soft guardrails (tone, register, conventions) live in prompts. Hard guardrails (no commitments outside the Vacancy record, no requesting bank details over WhatsApp, no posting where ToS forbids automation) live in deterministic code that runs before every outbound action and cannot be talked around.

**Every action is auditable, replayable, and reversible where possible. **Agent actions are first-class events in the platform's event store. A regulator, a tribunal, or an operations director must be able to reconstruct any conversation, including which model and prompt version produced each message.

**Transparency to candidates and clients is non-negotiable. **Anyone interacting with an agent must know they are doing so and must be able to reach a human in one tap.

# 3. Agent Families

Four agent families, each with distinct workflows, escalation rules, and trust profiles.

## 3.1 Job Advert Authoring Agent

**Inputs: **structured Vacancy record (trade, location, start date, duration, pay rate, shift pattern, required cards, client — internal only). Optional consultant freeform notes.

**Outputs: **channel-specific draft adverts queued in the Distribution Engine, each tagged with the prompt version, model, and validation results.

**Workflow ownership: **from vacancy creation to draft-ready-for-publish (and, at higher trust levels, to actual publish).

**Escalation triggers: **pay rate above agency-configured threshold; new client (no prior placements); sensitive sector (e.g. nuclear, asbestos); freeform consultant notes containing flagged terms; compliance validation failure that the agent cannot self-correct in two attempts.

**Trust dial milestones: **Level 0 — every draft requires human approval. Level 1 — auto-publish to internal preview channels (Slack, Teams) for spot-check; human approves external publish. Level 2 — auto-publish to free channels (Find a Job, owned social) for routine vacancies; human approves paid channels. Level 3 — auto-publish to all channels for routine vacancies; escalations only.

## 3.2 Candidate Engagement Agent

**Inputs: **inbound messages across WhatsApp, Telegram, Facebook Messenger, Instagram DMs, SMS, email; outbound triggers from Sourcing & Matching Agent or scheduled workflows (shift reminders, document expiry chases, re-engagement).

**Outputs: **outbound messages on the same channel; updates to the candidate record (availability, document uploads, screening answers); placements proposed for human confirmation; escalations.

**Workflow ownership: **first-line conversation about specific vacancies; document collection; shift confirmation; dormant-candidate re-engagement; basic compliance screening (right to work, valid card, distance, availability) against the Vacancy's must-haves.

**Hard escalation triggers (instant, non-negotiable): **any pay dispute or unpaid-wages mention; complaints about a client or site; safeguarding language; reports of accidents, injuries, or near-misses; modern slavery red flags (third-party control of communications, identical bank details across multiple workers, signs of coercion); messages indicating distress, mental-health crisis, or self-harm; legal threats; press enquiries; any message the agent's confidence model rates below threshold.

**Soft escalation triggers (queued for human review within 4 hours): **candidate asks about something outside the Vacancy record; candidate negotiates rate or terms; candidate withdraws; same candidate appears across multiple vacancies in conflict.

**Multilingual: **Polish, Romanian, Bulgarian, Lithuanian, Ukrainian, and Portuguese supported at launch. Conversations happen in the candidate's preferred language; escalations to consultants include a side-by-side English translation.

## 3.3 Client Communication Agent

**Scope: **deliberately narrower than the Candidate Engagement Agent. Handles routine, low-risk client admin only.

**In scope: **shift confirmations to site contacts; chasing timesheet approvals; acknowledging vacancy briefs and converting them into structured Vacancy records; sending compliance documentation packs on request; routine status updates on open vacancies.

**Out of scope (always escalates): **anything touching commercial terms (rates, margins, contract); complaints; new business; credit issues; any message from a contact not previously seen; tier-1 main contractors during the first 90 days post-onboarding.

**Tone: **calibrated per client tier and per agency brand. Sample client messages are used to tune register during onboarding.

## 3.4 Sourcing & Matching Agent

**Trigger: **Vacancy created or reopened; scheduled re-runs as candidates' availability changes.

**Behaviour: **shortlists candidates from the ATS using semantic and structured matching; ranks by skill fit, distance, reliability score, prior placements with the client, current availability, and compliance status (cards in date, right-to-work confirmed). Hands ranked shortlist to the Candidate Engagement Agent for outreach.

**Secondary workflows: **card-expiry detection and renewal nudges; AWR 12-week clock monitoring; cross-vacancy conflict detection (same candidate proposed for two clashing assignments).

**Bias controls: **ranking features are explicitly enumerated and reviewable. Protected characteristics under the Equality Act 2010 are excluded from features and from any derived signal. Bias testing on shortlist outputs is part of the evaluation harness — see §8.

# 4. Architecture

All agents share a common framework with five layers.

## 4.1 Tools Layer

Deterministic, well-defined functions the agent can call. Every tool call is logged. Tool surface area is intentionally small — agents do not get raw database access.

- read_candidate, read_vacancy, read_conversation_history

- update_candidate_field (whitelisted fields only)

- send_message (channel, recipient, body) — runs through guardrails before dispatch

- request_document (candidate, document_type) — generates a secure upload link

- schedule_followup (when, action)

- escalate_to_human (reason, severity, context)

- propose_placement (candidate, vacancy) — never confirms; queues for human or higher-trust workflow

## 4.2 Memory Layer

Three memory tiers, each with explicit scope and retention.

- **Conversation memory **— full message history per candidate-agency pair. Retained per GDPR retention policy (default 24 months from last contact, configurable per agency).

- **Candidate profile memory **— structured facts derived from conversations (preferred shifts, transport, language, reliability signals). Updated only via the update_candidate_field tool, never by free-form agent inference.

- **Agency style memory **— per-brand tone, signatures, banned phrases, preferred terminology. Maintained by the consultant team, versioned, and applied at prompt-construction time.

## 4.3 Guardrail Layer

Every outbound action passes through this layer before dispatch. The layer is deterministic code, not LLM-evaluated.

See §6 for the full guardrail catalogue.

## 4.4 Model Routing Layer

Different tasks route to different models for cost and quality optimisation. Routing decisions are recorded with each action.

| **Task type** | **Default model class** | **Latency budget** | **Notes** |
| --- | --- | --- | --- |
| High-stakes drafting (adverts, escalation summaries) | Frontier (Claude Sonnet/Opus class) | ≤ 30s | Quality matters more than latency |
| Live candidate conversation | Frontier | ≤ 8s | Falls back to canned holding response if exceeded |
| Intent classification, routing, language detection | Small/cheap (Haiku class) | ≤ 1s | High volume; cost dominates |
| Embedding for matching/retrieval | Embedding model | Batch | Re-embed on schema change |
| Compliance pre-check on draft adverts | Small + rules engine | ≤ 2s | Fast, deterministic where possible |

## 4.5 Observability Layer

Every agent action emits a structured event recording: timestamp, agent identity, model and prompt versions, tool calls made, inputs and outputs, guardrail decisions, escalation outcomes, and (where available) downstream success signals (candidate replied positively, shift confirmed, advert produced applications). These events feed both the audit log and the evaluation harness.

# 5. Conversation Control Flow

A simplified view of how the Candidate Engagement Agent processes a single inbound message:

1. Inbound message arrives via channel adapter
2. Identify candidate (or create stub record if unknown number)
3. Disclose AI identity if first contact this conversation
4. Classify intent (small model)
5. Check escalation triggers (deterministic rules)
   ↳ if triggered → escalate_to_human, send holding message
6. Retrieve relevant context (vacancy, recent history, profile)
7. Generate response (frontier model)
8. Run guardrail layer on proposed response
   ↳ if guardrail fails → either auto-correct or escalate
9. Send via channel adapter
10. Log event with full context

# 6. Guardrail Catalogue

Every guardrail below is implemented as deterministic code that runs on every outbound action. Failures are non-overridable by the model. A guardrail change requires a paired sign-off file and triggers the protected-path hook described in §10.

## 6.1 Hard guardrails (never relaxed)

- **No commitment beyond the Vacancy record. **Pay rates, start dates, durations, locations, and terms in outbound messages must match the canonical Vacancy. The guardrail extracts numeric and date claims from drafts and verifies them against the record.

- **No sensitive data over unsecured channels. **Bank details, full ID document numbers, NI numbers, passport numbers, and dates of birth must never appear in outbound messages. If a candidate volunteers them, the agent acknowledges receipt and directs them to a secure upload link; the message is redacted before storage in the conversation log.

- **No prohibited automation. **Each channel adapter publishes its automation policy (e.g. WhatsApp Business: outbound outside the 24-hour window must use approved templates only). The guardrail enforces these at dispatch.

- **No discriminatory content. **Adverts and screening questions are checked against an Equality Act 2010 banned-phrase list and a model-based bias classifier. Both must pass.

- **Identity disclosure. **First message in any new conversation must disclose AI identity and offer the human escalation path.

- **Off-platform redirection ban. **Agents never direct candidates to channels outside the platform's recorded set, never share personal phone numbers or non-business emails.

## 6.2 Soft guardrails (tunable per agency)

- Tone and register match agency brand profile.

- Message length within channel norms (SMS ≤ 160 chars, WhatsApp ≤ 1024 chars for templates).

- Response time targets — agent posts a holding message if it expects to exceed.

- Maximum outbound messages per candidate per 24h to prevent over-contact across agents.

## 6.3 Compliance gates

Compliance gates sit between agent output and channel dispatch. They block dispatch entirely if violated.

- **AWR pay disclosure. **Adverts must specify pay rate including holiday pay treatment for assignments crossing the 12-week threshold.

- **Conduct Regulations 2003 identification. **Adverts must clearly identify the agency. The compliance module maintains the canonical agency identifier per brand.

- **Right-to-work integrity. **The agent never proposes a candidate for placement without confirmed right-to-work documentation.

- **GLAA scope checks. **For sectors falling under GLAA licensing, additional checks apply.

# 7. The Trust Dial

Each agent operates at a trust level per workflow per agency brand. Trust is granted only by named individuals (Operations Director plus Compliance Officer) and is revocable instantly by either.

| **Level** | **Behaviour** | **Promotion criteria (rolling 30 days)** |
| --- | --- | --- |
| **0** | Every action requires human approval before execution. | Default for all new agents and new agency brands. |
| **1** | Internal preview only. Outputs visible to consultants in shadow mode; humans still send. | ≥ 200 actions reviewed; ≥ 95% approval rate; zero compliance violations; ≥ 90% intent-classification accuracy. |
| **2** | Auto-execute routine workflows (low-risk channels, established candidates/clients). Higher-risk paths still human-approved. | ≥ 1,000 actions; ≥ 97% approval rate; zero compliance violations; complaint rate ≤ baseline; auto-resolution ≥ 60%. |
| **3** | Auto-execute all configured workflows. Escalations only. | ≥ 5,000 actions at Level 2; ≥ 98% approval; zero compliance violations; sustained quality metrics; explicit OD + CO sign-off. |

**Automatic demotion: **any compliance violation, any candidate complaint reaching a defined severity, or any cluster of negative outcomes detected by the evaluation harness triggers automatic demotion to Level 0 pending review.

# 8. Evaluation Harness

Continuous evaluation is what makes the trust dial trustworthy. The harness runs on every prompt change, model version change, and on a nightly schedule.

## 8.1 Evaluation suites

- **Golden conversations. **A curated set of real (anonymised) historical conversations with consultant-rated ideal responses. New prompts must match or exceed prior rating.

- **Adversarial set. **Engineered cases probing each guardrail — pay disputes, sensitive data requests, jailbreak attempts, ambiguous escalation triggers, off-platform redirection attempts.

- **Bias suite. **Paired conversations differing only in protected-characteristic signals; agent behaviour must not differ in any commercially material way.

- **Multilingual suite. **Coverage across all supported languages with native-speaker review baselines.

- **Compliance suite. **Generated adverts checked against AWR, Conduct Regs 2003, Equality Act, channel ToS.

## 8.2 Production telemetry

- Auto-resolution rate (conversations completed without escalation)

- Escalation rate by trigger category

- Candidate satisfaction proxy (sentiment, completion of requested actions, response cadence)

- Complaint rate per 1,000 conversations

- Edit rate on AI-drafted adverts (during transition periods)

- Application-per-vacancy lift versus pre-AI baseline

- Time-to-first-response and time-to-fill versus pre-AI baseline

# 9. Rollout Protocol

Each new agent, each new channel, and each new agency brand follows the same rollout protocol.

- **Stage 1 — Shadow. **Agent runs on live inputs producing outputs that no one but the engineering team sees. Used to calibrate prompts and find edge cases.

- **Stage 2 — Co-pilot. **Outputs surfaced to consultants in their existing tools. Consultant accepts, edits, or rejects. Edit rate and rejection reasons feed back into prompt tuning.

- **Stage 3 — Supervised autonomy. **Agent acts on routine workflows; consultants review a sample. Trust dial moves Level 0 → 1 → 2.

- **Stage 4 — Autonomy with exception handling. **Agent owns workflows end to end except for triggered escalations. Trust dial moves to Level 3.

A new agency joining the group always begins at Stage 1 for every workflow, regardless of how mature those workflows are at sister agencies. Brand voice, client expectations, and candidate populations differ enough that prior trust does not transfer automatically.

# 10. Engineering Practices in Claude Code

This module benefits more than any other from Claude Code's agentic features.

**Per-agent sub-agents. **Each agent family has a dedicated Claude Code sub-agent during development with its own CLAUDE.md, prompt history, and test corpus. This mirrors the production agent topology and keeps context clean.

**Protected-path hooks. **The guardrails/ directory is protected by a pre-commit hook that requires a paired sign-off file (signed-off by Compliance Officer) before any change is permitted. The hook also runs the adversarial evaluation suite and blocks the commit on any new failure.

**Prompt versioning. **Prompts are first-class versioned artifacts with semantic versioning. Major version bumps require a full evaluation harness pass. The model routing layer pins prompt versions per environment.

**MCP-wrapped agent introspection. **A development MCP server exposes the production agents to Claude Code in read-only mode, allowing the team to replay conversations, inspect tool calls, and propose prompt changes inside the Claude Code loop.

**Background evaluation agent. **A persistent background agent runs the full evaluation harness on every push and posts results to the team's Slack/Teams channel before review.

**Plan mode for trust-dial changes. **Promoting an agent up the trust dial is treated as a structured change. Plan mode produces the change document covering scope, criteria met, monitoring window, rollback trigger, and named approvers.

# 11. Open Questions for Resolution Before Build

- Which initial agency brand provides the corpus for prompt tuning? Decision driven by data quality and legal sign-off readiness.

- What is the headline candidate-facing identity for the agent — branded per agency, group-branded, or named ("Sam from [Agency]")? Affects trust signals and disclosure phrasing.

- Where does final commercial sign-off live for guardrail changes — Compliance Officer alone or Compliance Officer + Operations Director jointly?

- What is the SLA for escalations to a human during out-of-hours periods? Affects channel choices and holding-message phrasing.

- What is the legal advice on cross-border model calls and candidate data? Likely to constrain model routing.

*End of specification — AI Content & Engagement Layer v1.0*
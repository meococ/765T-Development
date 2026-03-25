# Assumption Register — BIM765T Revit Agent

| Field | Value |
|-------|-------|
| **Purpose** | Track all major product/market/technical assumptions that still need validation or explicit disposition. |
| **Inputs** | `765T_BLUEPRINT.md`, `765T_CRITICAL_REVIEW.md`, `PRODUCT_REVIEW.md`, `docs/assistant/USE_CASE_MATRIX.md`, `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md`, persona files. |
| **Outputs** | Validation queue, ownership, target phase, and disposition for each assumption. |
| **Status** | Pass 1 baseline complete; Pass 2 evidence validation pending. |
| **Owner** | Product + Engineering |
| **Source refs** | See `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md`. |
| **Last updated** | 2026-03-24 |

> Claim labels used in this register: `assumption`, `validated`, `deferred`, `removed`, `convert-to-scope`.

## Summary

| Metric | Value |
|--------|-------|
| **Total assumptions extracted** | 47 |
| **Critical** | 9 |
| **High** | 17 |
| **Medium** | 18 |
| **Low** | 3 |

### By Category

| Category | Count |
|----------|-------|
| Market | 3 |
| User Behavior | 12 |
| Technical | 15 |
| Business | 6 |
| Competitive | 5 |
| Adoption | 3 |
| Resource | 3 |

### By Evidence Status

| Status | Count |
|--------|-------|
| Unvalidated | 30 |
| Partially Validated | 14 |
| Contradicted | 3 |

### By Disposition

| Disposition | Count |
|-------------|-------|
| validate | 29 |
| remove | 3 |
| defer | 15 |

### By Target Phase

| Target phase | Count |
|--------------|-------|
| phase-1 | 8 |
| phase-2 | 12 |
| phase-4 | 15 |
| phase-5 | 12 |

## Main Register

| ID | Category | Assumption Statement | Source Doc | Criticality | Evidence Status | Disposition | Validation Owner | Target Phase | Validation Method | Risk if Wrong |
|----|----------|----------------------|------------|-------------|-----------------|-------------|------------------|--------------|-------------------|---------------|
| ASM-001 | Market | "BIM khong gioi han, nang cao hieu suat" — BIM professionals need/want an AI agent inside Revit to improve productivity | `docs/765T_BLUEPRINT.md` (Mission) | Critical | Unvalidated | validate | Product Manager | phase-1 | User interviews with 20+ BIM professionals | Building a product nobody asked for; zero adoption |
| ASM-002 | User Behavior | Drafters "khong biet code, chi muon noi va duoc lam" — Drafters want natural-language interaction over visual scripting (Dynamo) | `docs/765T_BLUEPRINT.md` §8 | Critical | Unvalidated | validate | Product + UX Research | phase-2 | User interviews, A/B test NL-chat vs Dynamo for same tasks | Drafters may prefer visual scripting they already know; chat UX rejected |
| ASM-003 | User Behavior | BIM Managers want automated QC audit with health scores instead of manual View-by-View inspection | `docs/765T_BLUEPRINT.md` §9 | High | Partially Validated | validate | Product + UX Research | phase-2 | User interviews with BIM Managers; observe current QC workflow | BIM Managers may not trust AI-generated scores; prefer manual review |
| ASM-004 | Technical | 765T Scan can complete full project scan in 15-30 seconds | `docs/765T_BLUEPRINT.md` §2b | High | Unvalidated | validate | Principal Engineer | phase-4 | Technical PoC with real projects (small/medium/large) | Scan takes minutes on large models; blocks UI thread; terrible first impression |
| ASM-005 | Technical | "Multi-agent quet song song" — Multiple agents can scan Revit model in parallel | `docs/765T_BLUEPRINT.md` §9a | Critical | Contradicted | remove | Principal Engineer | phase-4 | Technical PoC | Revit API is single-threaded; parallel scan is impossible as described. Critical Review confirms this is an illusion. Must redesign as sequential data extraction + parallel analysis outside Revit |
| ASM-006 | Technical | AI can generate Python/C# code and execute it safely inside Revit | `docs/765T_BLUEPRINT.md` §10, §11 | Critical | Partially Validated | validate | Principal Engineer | phase-4 | Security review, sandboxing PoC | AI-generated code can corrupt/delete entire model. Critical Review rates this as most dangerous feature. IronPython is dead. No sandbox exists for Revit API |
| ASM-007 | Business | Average cost per interaction is ~$0.03 via LLM Router | `docs/765T_BLUEPRINT.md` §12c | High | Partially Validated | validate | Product Manager | phase-5 | Measure actual costs over 1000+ real interactions | Critical Review estimates real cost at $0.04-0.08 (2x higher). Pricing model may be unviable at projected cost |
| ASM-008 | Technical | Rule-based Intent Classifier can accurately route prompts to correct LLM tier | `docs/765T_BLUEPRINT.md` §12a | High | Partially Validated | validate | Principal Engineer | phase-4 | Test classifier on 500+ real BIM prompts, measure misroute rate | Critical Review notes rule-based classification works for only ~30% of queries. BIM users don't speak in fixed patterns. Misrouting = bad UX or wasted cost |
| ASM-009 | Technical | Vector DB (Qdrant/Chroma) is needed for semantic search of BEP/standards | `docs/765T_BLUEPRINT.md` §3a (knowledge/) | Medium | Contradicted | remove | Principal Engineer | phase-4 | Compare Vector DB RAG vs full-context window for typical BEP sizes | Critical Review shows BEP is 20-50 pages (~25K tokens), fits in Gemini Flash context window. HashEmbedding in codebase produces meaningless vectors (P1-3 in Roadmap). Unnecessary complexity |
| ASM-010 | Market | There is no existing product that does "AI + safe mutation in Revit" | `docs/765T_BLUEPRINT.md` §13 | High | Partially Validated | validate | Product Manager | phase-1 | Competitive landscape scan every quarter | If Autodesk ships native AI agent or OpenClaw adds mutation safety, moat evaporates. Product Review acknowledges Autodesk native AI as high long-term threat |
| ASM-011 | User Behavior | Users want a "Project Brief" (AI narrative summary) rather than raw statistics | `docs/765T_BLUEPRINT.md` §4c | Medium | Unvalidated | defer | Product + UX Research | phase-2 | A/B test Brief vs traditional statistics dashboard | Users may want both; or may not trust AI-generated narrative without raw data backup |
| ASM-012 | Competitive | 765T safety pipeline (dry-run/approve/execute) is a "genuine competitive moat" — cannot be easily copied | `docs/PRODUCT_REVIEW.md` §1.1 | High | Partially Validated | validate | Product Strategy | phase-1 | Expert review of implementation complexity | OpenClaw/competitors could implement similar safety in 1-3 months. Moat is temporary unless paired with data/network effects |
| ASM-013 | User Behavior | "Script marketplace nhu npm" — BIM community will contribute scripts to a marketplace | `docs/765T_BLUEPRINT.md` §14, `docs/765T_CRITICAL_REVIEW.md` §3b | Medium | Unvalidated | defer | Product + UX Research | phase-2 | Analyze pyRevit extension ecosystem size; survey BIM scripters | Critical Review explicitly calls this an illusion: BIM community is 100x smaller than dev community. Insufficient contributors for a vibrant marketplace |
| ASM-014 | Technical | "Undo 1 click" is feasible | `docs/765T_BLUEPRINT.md` §14 (Safety checklist) | Critical | Contradicted | remove | Principal Engineer | phase-4 | Technical PoC with TransactionGroup | Revit has no Undo API. Critical Review documents 3 workarounds, all with significant limitations. "Undo 1 click" as marketed is misleading |
| ASM-015 | User Behavior | Users will configure AI personas via system prompt customization | `docs/765T_BLUEPRINT.md` §10c (Custom Persona) | Low | Unvalidated | defer | Product + UX Research | phase-2 | Observe persona config usage in beta | Critical Review says users don't know how to configure AI. 3 presets (Drafter/Manager/Expert) likely sufficient. Over-engineering persona customization |
| ASM-016 | Business | Script caching reduces cost "ve 0" for repeated tasks | `docs/765T_BLUEPRINT.md` §12b | Medium | Partially Validated | validate | Product Manager | phase-5 | Measure cache hit rate on real usage patterns | Assumes users repeat identical tasks frequently. Real BIM work may be more varied than expected. Cache match rate could be low |
| ASM-017 | Adoption | Users will go through 765T Smart Onboarding flow willingly | `docs/765T_BLUEPRINT.md` §2 | High | Unvalidated | validate | Product Manager | phase-5 | Observe onboarding completion rate in beta | Users may skip/cancel if scan feels slow. "15-30 seconds" is assumption (ASM-004). Popup fatigue is real in Revit ecosystem |
| ASM-018 | Technical | 765T Connect can integrate with ACC, Unifi, Jira, Slack, SharePoint via MCP connectors | `docs/765T_BLUEPRINT.md` §7 | Medium | Unvalidated | defer | Principal Engineer | phase-4 | Build 1 connector (Excel) and measure effort; research API availability | Critical Review: each connector is a mini-product. ACC API changes constantly. Unifi API is limited. Massively underestimated scope |
| ASM-019 | User Behavior | "Proactive suggestions" (765T Suggest) when user works in Revit are welcome, not annoying | `docs/765T_BLUEPRINT.md` §8e | High | Unvalidated | validate | Product + UX Research | phase-2 | Beta test with opt-in; measure dismiss rate, satisfaction | Interruption during creative work (modeling) may frustrate users. Toast spam = uninstall. Requires very high precision to be useful |
| ASM-020 | Resource | MVP can be built in 6-8 weeks with 1 full-time + 1 part-time developer | `docs/765T_CRITICAL_REVIEW.md` §9 | Critical | Unvalidated | validate | Product + Engineering Lead | phase-5 | Retrospective on actual sprint velocity | Typical underestimation. Chat UI + Revit API + LLM integration + 5 tools + testing for beta = likely 12-16 weeks realistically |
| ASM-021 | Business | $50-100/month Gemini API cost is sufficient for MVP | `docs/765T_CRITICAL_REVIEW.md` §9 | Medium | Unvalidated | defer | Product Manager | phase-5 | Project actual API usage from beta user count × interactions/day | If 10 beta testers do 50 interactions/day at $0.04-0.08 each = $600-1200/month. 10x higher than estimated |
| ASM-022 | Market | BIM professionals are ready to trust AI to modify their Revit models | `docs/765T_BLUEPRINT.md` §5, §14 | Critical | Unvalidated | validate | Product Manager | phase-1 | User interviews; trust survey; observe beta behavior (read-only vs mutation usage ratio) | Construction/AEC industry is conservative. Model = liability. Users may use read-only features but refuse mutation. Entire write pipeline could be unused |
| ASM-023 | Technical | "Health Score 72/100" is a meaningful metric | `docs/765T_BLUEPRINT.md` §9b | Medium | Unvalidated | defer | Principal Engineer | phase-4 | Industry expert review; user research on score interpretation | Critical Review explicitly calls this out: no industry standard defines "Model Health Score." Self-defined scoring = no objective value |
| ASM-024 | Competitive | pyRevit users will switch to 765T because it's "Dynamo khong can lap trinh" | `docs/PRODUCT_REVIEW.md` §5 | High | Unvalidated | validate | Product Strategy | phase-1 | Survey pyRevit users; analyze switching friction | pyRevit is free, well-established, has large community. 765T requires API keys, subscription. Switching cost is high |
| ASM-025 | Business | Pricing at $600-900/seat/year is viable for BIM teams of 5-50 | `docs/PRODUCT_REVIEW.md` §2 (Persona 4) | High | Unvalidated | validate | Product Manager | phase-5 | Price sensitivity survey; competitor pricing analysis | No validation with actual buyers. BIM teams already pay for Revit ($2,775/yr) + add-ins. Budget fatigue is real |
| ASM-026 | Technical | Named Pipes provide adequate performance for BIM data serialization (10,000+ elements) | `docs/765T_CRITICAL_REVIEW.md` §2e | High | Partially Validated | validate | Principal Engineer | phase-4 | Benchmark serialization latency with real model data | Cross-process serialization of large BIM datasets may introduce unacceptable latency. Critical Review identifies this as "elephant in the room" |
| ASM-027 | Adoption | 5-10 beta testers (BIMer thuc te) can be recruited for MVP | `docs/765T_CRITICAL_REVIEW.md` §9 | Medium | Unvalidated | defer | Product Manager | phase-5 | Outreach plan; actual recruitment attempts | Requires BIM professionals willing to install unverified add-in on production machines with real projects. Hard to find |
| ASM-028 | Technical | Background QC scanning is feasible via Revit IdlingEvent | `docs/765T_BLUEPRINT.md` §8e | Medium | Partially Validated | validate | Principal Engineer | phase-4 | Technical PoC measuring IdlingEvent availability and time constraints | Critical Review: IdlingEvent only fires when Revit is idle, has time limits. "Background scanning" is very constrained. May not work during active modeling |
| ASM-029 | User Behavior | BIM Managers will configure policy.json to control team tool access | `docs/PRODUCT_REVIEW.md` §2 (Persona 2) | High | Unvalidated | validate | Product + UX Research | phase-2 | Observe BIM Manager setup behavior in beta | Product Review identifies JSON config as major frustration. No validation UI. Typos cause silent failures. BIM Managers are not DevOps engineers |
| ASM-030 | Technical | Overall backend score is 8.0/10 | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` (Part 0) | Medium | Partially Validated | validate | Principal Engineer | phase-4 | External code audit; define scoring rubric with industry benchmarks | Self-assessed score from AI review. Sub-scores not independently validated. ILlmClient is completely unwired (P1-2), HashEmbedding is broken (P1-3) — yet overall score is 8.0 |
| ASM-031 | Technical | Architecture compliance is 9.2/10 | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` (Part 0) | Low | Partially Validated | validate | Principal Engineer | phase-4 | External architecture review | Self-assessed by AI agent. High score but test coverage is at 7.5/10 and there are 0 unit tests for tool handlers |
| ASM-032 | Technical | Coverage scores (Family 90%, Model QC 85%, etc.) accurately reflect production readiness | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` (Part 1) | High | Partially Validated | validate | Principal Engineer | phase-4 | Map each coverage % to specific feature list; user-validate that listed features match real workflow needs | Coverage = "tools registered" not "tools working in production with real data." Annotation at 20% and Workset at 30% are production blockers |
| ASM-033 | Resource | Wave 1 backend gaps can be filled in 2-3 weeks | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` (Part 1) | High | Unvalidated | validate | Product + Engineering Lead | phase-5 | Track actual implementation time for first 3 tools | Includes workset CRUD, view crop, schedule compare, revision management. Each requires Revit API expertise + testing with real models. Likely 4-6 weeks |
| ASM-034 | Resource | Frontend can be completely redesigned in 5 sprints (10 weeks) | `docs/agent/IMPROVEMENT_ROADMAP_2026Q1.md` (Part 2, Part 4) | Medium | Unvalidated | defer | Product + Engineering Lead | phase-5 | Retrospective after Sprint 1 | Current frontend is a visual prototype with zero functionality. Full redesign (routing, state management, live chat, dashboard) is a major project. May require dedicated frontend developer |
| ASM-035 | User Behavior | Persona definitions (revit_worker, qa_reviewer, helper, etc.) match real user mental models | `docs/agent/personas/*.json` (all 8 files) | Medium | Unvalidated | defer | Product + UX Research | phase-2 | User card-sorting exercise; observe which personas users choose and how they use them | 8 personas defined without user research. Tone (pragmatic/strict/friendly) and expertise scope assumed. Users may not map to these roles |
| ASM-036 | User Behavior | Vietnamese-language AI tone ("Chao anh, em la...") is appropriate for all target markets | `docs/agent/personas/*.json` | Medium | Unvalidated | defer | Product + UX Research | phase-2 | Market segmentation; user preference survey | Current personas are Vietnamese-only. Limits market to Vietnam. No i18n plan validated. Tone assumes hierarchical workplace culture |
| ASM-037 | Technical | 8 distinct personas provide meaningfully different AI behavior (not just different system prompts) | `docs/agent/personas/*.json` | Low | Unvalidated | defer | Principal Engineer | phase-4 | Blind test: users interact with different personas, measure perceived difference | Critical Review suggests 3 presets are sufficient. 8 personas may be indistinguishable in practice. Maintenance burden for marginal value |
| ASM-038 | User Behavior | P0 use cases (Quick Command Palette, Sheet/View Package Build, Pre-Issue QA/QC) are the highest-value for users | `docs/assistant/USE_CASE_MATRIX.md` | Critical | Unvalidated | validate | Product + UX Research | phase-2 | User interview with priority ranking exercise; usage analytics from beta | P0/P1/P2 prioritization has no documented evidence basis. If wrong, team builds wrong features first. "Canonical loops" (Atlas Fast Path, etc.) are not validated with real users |
| ASM-039 | User Behavior | "Canonical loops" (Atlas Fast Path, Workflow Compose, Delivery Loop, Lesson Promotion) reflect actual user workflows | `docs/assistant/USE_CASE_MATRIX.md` | High | Unvalidated | validate | Product + UX Research | phase-2 | Workflow observation study; user journey mapping | Named concepts without user validation. May be engineering abstractions that don't match real BIM workflows |
| ASM-040 | User Behavior | UX shape distinction (simple = command palette, complex = chat + plan + preview + approval + verify + evidence) is correct | `docs/assistant/USE_CASE_MATRIX.md` | Medium | Unvalidated | defer | Product + UX Research | phase-2 | Usability testing with both UX shapes | Assumes users can distinguish "simple" from "complex" tasks the same way product team does. Real usage patterns may not split this cleanly |
| ASM-041 | Competitive | OpenClaw only has "Basic" project context capability | `docs/765T_BLUEPRINT.md` §13 | Medium | Unvalidated | defer | Product Strategy | phase-1 | Fresh competitive analysis of OpenClaw features | Market comparison table shows 765T winning every category. Likely biased self-assessment. OpenClaw may have shipped features since comparison was made |
| ASM-042 | Competitive | No competitor does "Stream activity" (765T Flow equivalent) | `docs/765T_BLUEPRINT.md` §13 | Medium | Unvalidated | defer | Product Strategy | phase-1 | Quarterly competitive scan | AI UX is evolving rapidly. Other products may have adopted streaming UX. First-mover advantage is temporary |
| ASM-043 | Competitive | "Inspector Lane — grounding AI bang domain knowledge truoc khi act" is genuine innovation no one in AEC space does | `docs/PRODUCT_REVIEW.md` §5, §6 | Medium | Partially Validated | validate | Product Strategy | phase-1 | Patent/prior art search; competitive feature audit | Claim of uniqueness without systematic competitive analysis. Autodesk Forma and other tools may ground AI differently |
| ASM-044 | Adoption | "Khi co 100 user thuc te dung hang ngay" is achievable milestone | `docs/765T_CRITICAL_REVIEW.md` §Ket luan | High | Unvalidated | validate | Product Manager | phase-5 | Go-to-market plan; user acquisition funnel analysis | No GTM strategy documented. No marketing plan. No distribution channel identified. 100 daily active BIM users is ambitious for an unproven add-in |
| ASM-045 | Business | BYOK (Bring Your Own Key) is viable pricing model for startup phase | `docs/765T_CRITICAL_REVIEW.md` §7b | Medium | Unvalidated | defer | Product Manager | phase-5 | Analyze BYOK adoption in similar products (Cursor, Continue.dev) | Shifts API cost to user but creates support burden (key management, rate limits, model compatibility). May limit features that require specific model capabilities |
| ASM-046 | Technical | Revit 2025+ target is correct (drop 2024 support) to simplify architecture | `docs/765T_CRITICAL_REVIEW.md` §2e | High | Partially Validated | validate | Principal Engineer | phase-4 | Survey target user Revit version distribution | If majority of target users are on Revit 2024 (common in AEC firms slow to upgrade), dropping support loses primary market. Need version distribution data |
| ASM-047 | Business | Autodesk ToS permits AI agent add-ins that modify models | `docs/765T_CRITICAL_REVIEW.md` §7e | Critical | Unvalidated | validate | Product Manager | phase-5 | Legal review of Autodesk ToS, Partner Program terms; consult IP lawyer | If Autodesk prohibits or restricts AI agents that execute code / modify models, entire product concept may be illegal. No legal analysis performed |

## Priority Validation Queue (Pass 2)

| Priority | ID | Assumption | Disposition | Owner | Target Phase | Why Urgent |
|----------|----|------------|-------------|-------|--------------|-----------|
| 1 | ASM-005 | "Multi-agent quet song song" — Multiple agents can scan Revit model in parallel | remove | Principal Engineer | phase-4 | Conflicting claim that must be explicitly removed from active scope |
| 2 | ASM-014 | "Undo 1 click" is feasible | remove | Principal Engineer | phase-4 | Conflicting claim that must be explicitly removed from active scope |
| 3 | ASM-001 | "BIM khong gioi han, nang cao hieu suat" — BIM professionals need/want an AI agent inside Revit to improve productivity | validate | Product Manager | phase-1 | Critical assumption with high evidence gap |
| 4 | ASM-002 | Drafters "khong biet code, chi muon noi va duoc lam" — Drafters want natural-language interaction over visual scripting (Dynamo) | validate | Product + UX Research | phase-2 | Critical assumption with high evidence gap |
| 5 | ASM-020 | MVP can be built in 6-8 weeks with 1 full-time + 1 part-time developer | validate | Product + Engineering Lead | phase-5 | Critical assumption with high evidence gap |
| 6 | ASM-022 | BIM professionals are ready to trust AI to modify their Revit models | validate | Product Manager | phase-1 | Critical assumption with high evidence gap |
| 7 | ASM-038 | P0 use cases (Quick Command Palette, Sheet/View Package Build, Pre-Issue QA/QC) are the highest-value for users | validate | Product + UX Research | phase-2 | Critical assumption with high evidence gap |
| 8 | ASM-047 | Autodesk ToS permits AI agent add-ins that modify models | validate | Product Manager | phase-5 | Critical assumption with high evidence gap |
| 9 | ASM-004 | 765T Scan can complete full project scan in 15-30 seconds | validate | Principal Engineer | phase-4 | Critical assumption with high evidence gap |
| 10 | ASM-017 | Users will go through 765T Smart Onboarding flow willingly | validate | Product Manager | phase-5 | Critical assumption with high evidence gap |

## Notes

- `validate` = must be actively tested in Pass 2 before expanding scope or making commercial claims.
- `convert-to-scope` = assumption is sufficiently supported to be translated into MVP/pilot scope.
- `defer` = keep visible, but do not let it drive MVP decisions yet.
- `remove` = contradicted or unsafe claim that should not survive into active BA scope.

*This register is the control point for assumption debt. Update it whenever an interview, benchmark, legal review, or pilot adds evidence.*

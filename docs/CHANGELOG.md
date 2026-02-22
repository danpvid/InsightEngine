# Changelog

## 2026-02-22 — Insights v2 rollout (7 commits)

### Summary
Insights v2 was delivered in seven incremental commits, moving from a basic semantic pack flow to an evidence-resolved, output-mode-aware, confidence-scored, anti-causality-safe experience across API, application services, domain models, frontend UI, and docs.

### Commit sequence
1. `d6cda95` — Introduce InsightPack v2 with richer target story, relationships, data quality, and evidence anchors.
2. `5ead9af` — Compute InsightPack v2 metrics: time bins, deltas, offenders, drivers, seasonality, and relationship summaries.
3. `e3d3984` — Add InsightPromptBuilder v2 with structured JSON output, schema grounding, and evidence-anchor citations.
4. `4e7de57` — Wire insights endpoints to InsightPack v2 and prompt v2 with evidence-resolved responses and output modes.
5. `75f1b99` — Add confidence scoring and anti-causality sanitizer for insight-pack answers.
6. `bc90721` — Add frontend output mode selector with insight-pack confidence and evidence panel.
7. `a007056` — Document Insights v2 architecture, contracts, and usage examples.

### What changed

#### Backend / Domain
- Added InsightPack v2 domain model with richer analytical structure and evidence anchors.
- Extended deep insights and ask contracts with:
  - timeframe inputs (`month`, `dateFrom`, `dateTo`),
  - segmentation inputs (`segmentColumn`, `segmentValue`),
  - `outputMode` (`DeepDive`, `Executive`).
- Added metadata fields for observability and compatibility:
  - `packVersion`,
  - `confidenceScore`,
  - stronger `validationStatus` semantics (`ok`, `ok_sanitized`, `fallback`).

#### Application services
- Expanded evidence computation:
  - adaptive bins,
  - deltas and change points,
  - offenders/boosters,
  - driver candidates,
  - seasonality hints,
  - compute limits and timing metadata.
- Introduced structured ask flow:
  - strict JSON schema response,
  - one-pass repair retry for invalid JSON,
  - evidence anchor validation and resolution.
- Added anti-causality sanitizer for ask text fragments to enforce association/correlation wording.
- Added aggregated confidence scoring based on evidence coverage + declared confidence + resolution quality.

#### API
- Enhanced insights endpoints:
  - `POST /api/v1/datasets/{id}/insights/pack`
  - `GET /api/v1/datasets/{id}/insights/pack`
  - `POST /api/v1/datasets/{id}/insights/ask`
- Ask response now includes:
  - `answerJson`,
  - `evidenceResolved`,
  - `packVersion`,
  - enriched `meta` with confidence and validation status.

#### Frontend
- Added output-mode selector (DeepDive/Executive) in chart viewer ask panel.
- Included new ask output panel details:
  - pack version,
  - confidence score,
  - validation state,
  - fallback signal,
  - resolved evidence list.
- Updated frontend contracts to support new API fields.

#### Documentation
- README updated with Insights v2 principles, usage flow, and request/response examples.
- API docs expanded with a dedicated Insights v2 section.

### Safety and governance outcomes
- No raw-row handoff to LLM in insights flow.
- Evidence-grounded answer path with anchor resolution.
- Causality-unsafe wording rewritten to safer association language.
- Percentage-scale handling preserved in raw scale with explicit caveats when unknown.

### Validation notes
- Application and frontend builds were validated during rollout.
- API/integration build attempts may fail locally if an API process is running and locks output DLLs (MSB3021/MSB3027); this is an environment lock issue, not a compile-time code defect in the delivered changes.

### Operational notes
- Existing unrelated local modifications were intentionally kept outside these commits:
  - `src/InsightEngine.Infra.ExternalService/Services/LocalHttpLLMClient.cs`
  - `src/InsightEngine.Web/src/app/features/datasets/pages/recommendations-page/recommendations-page.component.html`

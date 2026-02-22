# AI UI Manual Verification

The Angular project currently does not have a configured unit test target in `src/InsightEngine.Web/angular.json`.
Because of that, AI feature UI checks are verified manually for now.

## Preconditions

1. Start API: `dotnet run --project src/InsightEngine.API`
2. Start Web: `npm start --prefix src/InsightEngine.Web`
3. Optional local LLM (for non-fallback flow): run Ollama and load the configured model (default `llama3`).
4. Ensure API `LLM.Provider` is set:
   - `None` for fallback verification
   - `LocalHttp` for local model verification

## Manual smoke steps

1. Upload a sample CSV from `samples/`.
2. Open the first recommendation and wait for chart render.
3. Click **Generate AI Summary**:
   - With `Provider=None`, verify fallback message and heuristic summary remain visible.
   - With `Provider=LocalHttp`, verify AI summary sections render (headline, bullets, cautions, next questions).
4. Click **Explain chart** and verify:
   - panel opens,
   - explanation sections render,
   - copy button copies non-empty content.
5. In **Ask Dataset**, submit a natural-language question and verify:
   - analysis plan appears,
   - **Apply Plan** updates controls,
   - **Apply & Run** refreshes chart with updated params.
6. Repeat AI Summary/Explain once and verify cache badge appears when `meta.cacheHit=true`.

## Regression checklist

- Chart still renders when AI endpoints fail or LLM is disabled.
- No full-page reload happens when AI actions are triggered.
- Existing exploration controls remain usable after Apply Plan.

## E2E sanity checks — navigation + breadcrumb

1. Open `/{lang}/datasets` and verify breadcrumb shows only `Dataset` (active, non-clickable).
2. Open one dataset in Explore (`/{lang}/datasets/{datasetId}/explore`) and verify breadcrumb:
   - `Dataset > Explore`
   - `Dataset` is clickable, `Explore` is active non-clickable.
3. Click **Next: Recommendations** and verify URL and breadcrumb:
   - `/{lang}/datasets/{datasetId}/recommendations`
   - `Dataset > Explore > Recommendations`
   - `Dataset` and `Explore` clickable, `Recommendations` active.
4. Click a recommendation card (or **Open chart**) and verify URL and breadcrumb:
   - `/{lang}/datasets/{datasetId}/charts/{recommendationId}`
   - `Dataset > Explore > Recommendations > Chart`
   - first three items clickable, `Chart` active non-clickable.

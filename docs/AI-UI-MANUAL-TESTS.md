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

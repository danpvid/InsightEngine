# QA Checklist - Metadata Index and Explore

Use this checklist to validate the metadata indexing pipeline and the Explore experience end-to-end.

## Pre-conditions
- API running (`src/InsightEngine.API`)
- Frontend running (`src/InsightEngine.Web`)
- Access to `/datasets/new`

## Verification Steps
1. Upload a CSV dataset
- Go to `/datasets/new`.
- Upload a medium CSV (numeric, date, and category columns).
- Confirm dataset appears in loaded datasets list.

2. Build metadata index
- Open recommendations or chart page for the uploaded dataset.
- Navigate to `/datasets/{datasetId}/explore`.
- If status is not ready, click `Rebuild Index`.
- Confirm status transitions to `building` and then `ready`.

3. Validate Overview tab
- Confirm row/column counts are shown.
- Confirm quality panel renders missingness/warnings when applicable.
- Confirm candidate keys list is present (or empty state is clear).

4. Validate Field details
- Select a numeric field in sidebar.
- Confirm numeric summary (min/max/mean/stddev + percentiles).
- Confirm histogram renders.
- Select a date field and confirm date density/gaps panel behavior.
- Select a string/category field and confirm top values panel.

5. Validate Correlations
- Open `Correlations` tab.
- Confirm ranked edges table loads.
- Filter by method/strength/search and verify table updates.
- Open one edge detail and use `Build scatter chart` action.

6. Validate Data Grid interactions
- Open `Data Grid` tab.
- Confirm sample rows are displayed.
- Apply filter pills via cell drilldown (`Filter to` / `Exclude`).
- Confirm row sample updates.
- Toggle visible columns and sorting.

7. Validate facets endpoint behavior
- Use a field with repeated categories.
- Confirm facet values/counts update with active filters.
- Confirm top-N cap behavior.

8. Validate Saved Views (local)
- Configure a custom explore state (tab, field, filters, pins).
- Save as a named view.
- Reload page and restore the saved view.
- Rename and delete the saved view.

## Non-functional Checks
- Large datasets should not trigger full NxN correlation computations.
- Correlation results should reflect capped/top-K behavior.
- UI should show loading skeletons and clear empty/error states.
- No regressions on existing recommendations/chart routes.

## Expected Artifacts
- `index.json` generated in dataset storage.
- `index.status.json` reflects last build state.

## Smoke API Calls (optional)
- `POST /api/v1/datasets/{id}/index:build`
- `GET /api/v1/datasets/{id}/index/status`
- `GET /api/v1/datasets/{id}/index`
- `GET /api/v1/datasets/{id}/rows`
- `GET /api/v1/datasets/{id}/facets?field=<column>&top=20`

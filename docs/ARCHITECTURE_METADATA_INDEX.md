# Metadata Index Architecture

## Goal
Provide a deterministic "catalog index" for single-table datasets, similar to how search engines index documents: quick metadata retrieval, faceting, and relationship discovery without scanning the full dataset on every UI interaction.

## Main Components
- API layer:
  - `POST /api/v1/datasets/{datasetId}/index:build`
  - `GET /api/v1/datasets/{datasetId}/index`
  - `GET /api/v1/datasets/{datasetId}/index/status`
  - `GET /api/v1/datasets/{datasetId}/rows`
  - `GET /api/v1/datasets/{datasetId}/facets`
- Application layer:
  - request/response contracts (`BuildIndexRequest`, `BuildIndexResponse`)
  - application service orchestration and envelope consistency
- Domain layer:
  - `DatasetIndex`, `ColumnIndex`, `CorrelationEdge`, `KeyCandidate`, `DatasetQualityIndex`
  - semantic tagging heuristics and index limits model
  - CQRS handlers for build/query/status
- Infra layer:
  - `DuckDbMetadataAnalyzer` for deterministic profiling and statistics
  - `IndexStore` for JSON persistence + in-memory cache
  - status lifecycle file (`building`, `ready`, `failed`)

## Data Flow
1. Dataset is uploaded and stored as CSV.
2. Build endpoint triggers indexing with configurable caps.
3. Analyzer computes:
   - type-aware column profiles
   - numeric/date/string stats
   - candidate keys
   - capped correlations (top edges only)
4. Semantic tags are inferred from column names + stats.
5. `DatasetIndex` is persisted to `index.json` and cached.
6. Explore UI consumes index/status for fast panels and drilldowns.

## Storage Format
- Per dataset folder:
  - `index.json`
  - `index.status.json`
- JSON is serialized with enum strings for cross-platform consistency.

## Performance and Safety Guards
- Correlations are capped:
  - default max numeric columns for correlation
  - top-K edges per column
  - sampling rows for heavy operations
- Dynamic SQL safety:
  - strict column whitelisting from schema
  - quoted identifiers
  - bounded query parameters and limits
- Query endpoints enforce max limits and command timeouts.

## Explore UX Mapping
- Field Explorer sidebar reads from `columns`.
- Overview tab reads dataset health/keys/tags/limits.
- Correlations tab reads top-ranked edges.
- Distributions tab reads histogram and percentile metadata.
- Data Grid uses row sampling and facets endpoints for interactive filtering.
- Saved views persist local UI state only (browser localStorage).

## Known Limits (Current Scope)
- Single-table datasets only.
- Correlation methods prioritize numeric relationships.
- No full NxN correlation matrix persisted.
- Facets and rows use bounded server-side windows.

## Future Extensions
- Multi-table lineage and join suggestions.
- Asynchronous distributed indexing for very large datasets.
- Incremental index updates and stale-region rebuilds.
- Extended semantic ontology and domain-specific quality rules.

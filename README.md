# InsightEngine

InsightEngine is a local-first analytics MVP that turns raw CSV datasets into chart recommendations, executable ECharts payloads, and simulation outputs without requiring manual SQL authoring.

## Architecture

The solution is split into a .NET backend and an Angular frontend:

- `src/InsightEngine.API`: REST API, middleware, health endpoints, hosted jobs
- `src/InsightEngine.Application`: orchestration layer (commands/queries)
- `src/InsightEngine.Domain`: core models, rules, validators, result pattern
- `src/InsightEngine.Infra.Data`: DuckDB execution, CSV profiling, file storage, metadata persistence
- `src/InsightEngine.Web`: Angular chart viewer and dataset workflow UI

For a deeper breakdown, see `ARCHITECTURE.md`.

## Quickstart

### Prerequisites

- .NET SDK 10
- Node.js 18+
- npm 9+

### Backend

```bash
cd src/InsightEngine.API
dotnet run
```

Default URLs:

- API: `http://localhost:5000` / `https://localhost:5001`
- Swagger: `https://localhost:5001/swagger`
- Health: `http://localhost:5000/health`

### Frontend

```bash
cd src/InsightEngine.Web
npm install
npm start
```

Frontend URL:

- `http://localhost:4200`
- Optional local LLM server (for AI features): `http://localhost:11434`

## Configuration

Main runtime settings are in `src/InsightEngine.API/appsettings.json`.

### InsightEngineSettings

```json
{
  "InsightEngineSettings": {
    "UploadMaxBytes": 20971520,
    "ScatterMaxPoints": 2000,
    "HistogramBinsMin": 5,
    "HistogramBinsMax": 50,
    "QueryResultMaxRows": 1000,
    "CacheTtlSeconds": 300,
    "DefaultTimeoutSeconds": 30,
    "RetentionDays": 30,
    "CleanupIntervalMinutes": 60
  }
}
```

### Metadata persistence

```json
{
  "MetadataPersistence": {
    "Enabled": true,
    "ConnectionString": "Data Source=App_Data/insightengine-metadata.db"
  }
}
```

Notes:

- Dataset metadata is persisted in SQLite (local file).
- Raw CSV files remain in `FileStorage:BasePath`.
- Retention cleanup runs in background and can also be triggered manually.

### LLM (local-first, pluggable providers)

```json
{
  "LLM": {
    "Provider": "None",
    "TimeoutSeconds": 20,
    "MaxTokens": 512,
    "MaxContextBytes": 24000,
    "AskMaxQuestionChars": 600,
    "Temperature": 0.2,
    "EnableCaching": true,
    "LocalHttp": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3"
    },
    "Redaction": {
      "Enabled": true,
      "ColumnNamePatterns": ["email", "phone", "cpf", "ssn"]
    }
  }
}
```

Provider options:

- `None`: AI endpoints stay available but return deterministic fallback content.
- `LocalHttp`: uses a local HTTP LLM provider (Ollama-compatible).
- `OpenAI`: placeholder for future provider integration.

Enable local AI with Ollama (example):

```bash
ollama serve
ollama pull llama3
```

Then set in `appsettings.json`:

```json
{
  "LLM": {
    "Provider": "LocalHttp",
    "LocalHttp": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3"
    }
  }
}
```

Data safety defaults:

- LLM context is built from schema, profile stats, chart/query metadata, and reduced aggregates.
- Raw CSV rows are not sent to the model.
- Redaction masks/removes sensitive columns by configured name patterns.

## API overview

Key endpoints:

- `POST /api/v1/datasets` - upload CSV
- `GET /api/v1/datasets/runtime-config` - frontend runtime limits
- `GET /api/v1/datasets/{id}/recommendations` - chart recommendations
- `GET /api/v1/datasets/{id}/charts/{recommendationId}` - executable chart (supports exploration query params)
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/ai-summary` - on-demand AI summary
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/explain` - on-demand chart explanation
- `POST /api/v1/datasets/{id}/ask` - natural language question -> analysis plan (no SQL execution)
- `POST /api/v1/datasets/{id}/simulate` - scenario simulation
- `POST /api/v1/datasets/cleanup` - manual cleanup trigger (dev/admin)
- `GET /health` and `GET /health/ready` - liveness/readiness

### Example: chart execution response (short)

```json
{
  "success": true,
  "data": {
    "datasetId": "8e9e66f6-57f8-4f2d-b7f1-8c6de4a5e40f",
    "recommendationId": "rec_001",
    "option": { "series": [] },
    "insightSummary": {
      "headline": "Stable upward movement",
      "bulletPoints": ["Trend is positive"],
      "signals": {
        "trend": "Upward",
        "volatility": "Low",
        "outliers": "None",
        "seasonality": "Weak"
      },
      "confidence": 0.82
    },
    "meta": {
      "queryHash": "...",
      "duckDbMs": 18,
      "rowCountReturned": 120,
      "cacheHit": true
    }
  }
}
```

### Error envelope

All API errors follow this shape:

```json
{
  "success": false,
  "errors": [
    {
      "code": "validation_error",
      "message": "Invalid request",
      "target": "filters"
    }
  ],
  "traceId": "00-...",
  "status": 400
}
```

### Example: AI summary request/response (short)

Request:

```http
POST /api/v1/datasets/{id}/charts/{recommendationId}/ai-summary
Content-Type: application/json

{
  "aggregation": "sum",
  "metricY": "revenue",
  "timeBin": "month"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "insightSummary": {
      "headline": "Revenue trend is upward with moderate variance.",
      "bulletPoints": ["Growth accelerates after Q2."],
      "cautions": ["Possible seasonal bias in holiday months."],
      "nextQuestions": ["Which segment drives the lift?"],
      "confidence": 0.79
    },
    "meta": {
      "provider": "LocalHttp",
      "model": "llama3",
      "durationMs": 842,
      "cacheHit": false
    }
  }
}
```

## Smoke tests

Run full backend integration tests:

```bash
dotnet test InsightEngine.slnx
```

Run smoke-only flow tests:

```bash
dotnet test InsightEngine.slnx --filter "Category=Smoke"
```

Build frontend:

```bash
cd src/InsightEngine.Web
npm run build
```

Manual UI verification for AI features:

- `docs/AI-UI-MANUAL-TESTS.md`

## Operational notes

- Enums are serialized as strings (backend and frontend models aligned).
- Chart responses include `meta.cacheHit` for cache visibility.
- Request correlation uses trace identifiers in logs and error responses.
- Retention cleanup removes expired metadata and corresponding local artifacts.
- AI features are opt-in/on-demand and do not block chart rendering.

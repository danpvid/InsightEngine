# InsightEngine

> Auto-BI inteligente que transforma datasets CSV em insights acionaveis, sem SQL manual.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Angular](https://img.shields.io/badge/Angular-17-DD0031?logo=angular)](https://angular.dev/)
[![Architecture](https://img.shields.io/badge/architecture-DDD%20%2B%20CQRS-green.svg)](docs/)

InsightEngine is a local-first analytics platform that ingests CSV datasets, profiles data automatically, generates chart recommendations, executes analytical queries with DuckDB, and now supports opt-in AI features through a pluggable LLM provider layer.

## Visao do Produto

### Capacidades atuais
- Upload inteligente de CSV com validacao de limites e perfilamento automatico.
- Recomendacoes de visualizacao (line, bar, histogram, scatter) com score e criterios.
- Exploracao self-service (aggregation, time bin, metric, group by, filters).
- Insight Summary heuristico com sinais (trend, volatility, outliers, seasonality).
- Simulacao what-if (baseline vs simulated) com operacoes controladas.
- AI opt-in (Local LLM first): AI Summary, Explain Chart, Ask (analysis plan), Deep Insights (evidence-grounded report).

### Principios de arquitetura do produto
- Core flow nao depende de LLM e nao bloqueia renderizacao do grafico.
- Sem envio de CSV bruto para modelo: apenas schema/profile/meta/amostra agregada.
- Enums serializados como string (backend e frontend).
- API com envelope de erro padronizado + traceId.

## Status do Projeto

### Entregue (MVP RC)
- Backend .NET + frontend Angular 17 em fluxo completo (upload -> recommendations -> chart viewer).
- Painel de exploracao com estado em URL e reset para baseline.
- Navegacao inteligente entre insights e ordenacao por score/impacto/tipo.
- Simulacao com CTE/View no DuckDB, sem reescrita do CSV.
- Cache por queryHash e cache hit em metadados.
- Observabilidade (logs estruturados, correlacao, health endpoints).
- Persistencia minima de metadata + politica de retencao.
- LLM layer pluggable com provider local HTTP + fallback deterministico.

## Arquitetura

Camadas principais:
- `src/InsightEngine.API`: REST controllers, middleware, health, runtime config.
- `src/InsightEngine.Application`: orquestracao de casos de uso e servicos.
- `src/InsightEngine.Domain`: modelos, regras, enums, contratos e heuristicas.
- `src/InsightEngine.Infra.Data`: DuckDB, profiler CSV, armazenamento de arquivos, metadata.
- `src/InsightEngine.Web`: Angular app (upload, recommendations, chart viewer).
- `tests/InsightEngine.IntegrationTests`: testes de integracao e smoke.

Documento detalhado: `ARCHITECTURE.md`.

## Tecnologias

### Backend
- .NET SDK 10
- ASP.NET Core Web API
- DuckDB.NET
- FluentValidation
- MediatR
- SQLite (metadata local opcional)

### Frontend
- Angular 17 (standalone)
- Angular Material
- ECharts + ngx-echarts

## Quickstart

### Pre-requisitos
- .NET SDK `10.0.103` (ver `global.json`)
- Node.js 18+
- npm 9+

### Opcao 1 (scripts Windows)
```bash
setup.bat
start-demo.bat
```

### Opcao 2 (manual)

1) Backend
```bash
cd src/InsightEngine.API
dotnet run
```

2) Frontend
```bash
cd src/InsightEngine.Web
npm install
npm start
```

URLs padrao:
- API: `http://localhost:5000` / `https://localhost:5001`
- Swagger: `https://localhost:5001/swagger`
- Health: `http://localhost:5000/health`
- Frontend: `http://localhost:4200`

## Fluxo de Uso (UI)

1. Upload: `/datasets/new`
2. Recommendations: `/datasets/{datasetId}/recommendations`
3. Chart Viewer: `/datasets/{datasetId}/charts/{recId}`
4. Exploracao: ajustar controles no painel lateral e compartilhar URL.
5. Simulacao: aba de simulacao com comparativo baseline vs simulated.
6. AI opt-in: Generate AI Summary, Explain chart, Ask question.

## Configuracao

Arquivo principal: `src/InsightEngine.API/appsettings.json`.

### InsightEngineSettings (limites/runtime)
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

### MetadataPersistence
```json
{
  "MetadataPersistence": {
    "Enabled": true,
    "ConnectionString": "Data Source=App_Data/insightengine-metadata.db"
  }
}
```

### LLM (local-first, pluggable)
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
    "DeepInsights": {
      "EvidenceVersion": "v1",
      "MaxEvidenceSeriesPoints": 400,
      "ForecastDefaultHorizon": 30,
      "ForecastMaxHorizon": 90,
      "ForecastMovingAverageWindow": 5,
      "MaxBreakdownSegments": 10,
      "MaxRequestsPerMinute": 12,
      "CooldownSeconds": 4
    },
    "Redaction": {
      "Enabled": true,
      "ColumnNamePatterns": ["email", "phone", "cpf", "ssn"]
    }
  }
}
```

Provider options:
- `None`: endpoints de AI ativos com fallback heuristico/deterministico.
- `LocalHttp`: provedor local (Ollama/llama.cpp HTTP compativel).
- `OpenAI`: placeholder para integracao futura.

Exemplo local com Ollama:
```bash
ollama serve
ollama pull llama3
```

## API Overview

Principais endpoints:
- `POST /api/v1/datasets` - upload CSV
- `GET /api/v1/datasets/runtime-config` - limites runtime para frontend
- `GET /api/v1/datasets/{id}/recommendations` - recomendacoes
- `GET /api/v1/datasets/{id}/charts/{recommendationId}` - chart + insightSummary + meta
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/ai-summary` - resumo AI on-demand
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/explain` - explicacao estruturada
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/deep-insights` - narrativa analitica profunda com citacoes de evidencias
- `POST /api/v1/datasets/{id}/ask` - pergunta NL -> analysis plan (sem SQL execution)
- `POST /api/v1/datasets/{id}/simulate` - simulacao
- `POST /api/v1/datasets/cleanup` - cleanup manual (dev/admin)
- `GET /health` e `GET /health/ready`
- `POST /api/v1/auth/login` - login demo JWT

## Exemplos de resposta

### Chart execution (resumido)
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

### AI summary (resumido)
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

### Deep insights (resumido)
```json
{
  "success": true,
  "data": {
    "report": {
      "headline": "Demand is rising with concentrated segment influence",
      "executiveSummary": "Evidence-grounded narrative generated from deterministic facts.",
      "keyFindings": [{ "title": "Rising trend", "evidenceIds": ["TS_TREND_SLOPE"] }]
    },
    "meta": {
      "provider": "LocalHttp",
      "model": "llama3",
      "durationMs": 1240,
      "cacheHit": false,
      "validationStatus": "ok"
    },
    "explainability": {
      "evidenceUsedCount": 14,
      "topEvidenceIdsUsed": ["TS_TREND_SLOPE", "DIST_MEAN_METRIC"]
    }
  }
}
```

### Error envelope padrao
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

## Qualidade, Testes e Smoke

Backend:
```bash
dotnet test InsightEngine.slnx
```

Smoke flow:
```bash
dotnet test InsightEngine.slnx --filter "Category=Smoke"
```

Frontend build:
```bash
cd src/InsightEngine.Web
npm run build
```

AI UI manual checks:
- `docs/AI-UI-MANUAL-TESTS.md`

## Seguranca e Governanca de Dados

- Enums serializados como string em toda a stack.
- Logs com correlacao/traceId.
- `meta.cacheHit` em respostas cacheadas.
- LLM context builder aplica redaction e limite de bytes.
- CSV bruto nao e enviado ao modelo.

## Documentacao complementar

- `ARCHITECTURE.md`
- `docs/API_CONTRACTS.md`
- `docs/MELHORIAS-UX.md`
- `QUICK-START-DEMO.md`
- `START-HERE.md`
- `LEIA-PRIMEIRO.md`
- `PACKAGES.md`

## Contribuicao

1. Fork do repositorio.
2. Crie branch de feature (`git checkout -b feature/minha-feature`).
3. Commit seguindo conventional commits.
4. Abra PR com descricao objetiva e passos de validacao.

## Licenca

MIT. See `LICENSE`.


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
2. Pre-import Preview: `/datasets/{datasetId}/import-preview`
3. Finalizacao de schema (target, ignored columns, type overrides).
4. Recommendations: `/datasets/{datasetId}/recommendations`
5. Chart Viewer: `/datasets/{datasetId}/charts/{recId}`
6. Explore: `/datasets/{datasetId}/explore` (metadata-driven discovery).
7. Exploracao: ajustar controles no painel lateral e compartilhar URL.
8. Simulacao: aba de simulacao com comparativo baseline vs simulated.
9. AI opt-in: Generate AI Summary, Explain chart, Ask question.

### Pre-import Preview & Confirmation

Antes de entrar em recommendations, o usuario confirma como o dataset deve ser interpretado:

- **Target column**: metrica principal para recomendacao/formula/simulacao.
- **Ignored columns**: colunas removidas do fluxo analitico.
- **Column type overrides**: ajuste manual de tipo (`Integer`, `Decimal`, `Money`, `Percentage`, etc).
- **CurrencyCode**: aplicado quando colunas forem confirmadas como `Money`.

Ao finalizar, o schema confirmado e persistido e passa a ser usado nos endpoints downstream (profile, recommendations, charts, index, formula discovery).

### Percentage policy (Option 1)

- Valores percentuais sao sempre armazenados em **raw scale** (sem normalizacao no import).
- O sistema infere `percentageScaleHint` (`0..1`, `0..100`, `unknown`) com base no perfil amostral.
- Conversao visual para `%` e feita apenas na camada de formatacao (UI/tooltip/label), sem alterar dados persistidos.

Para datasets legados sem `schema.json`, `GET /schema` faz **backfill automatico** com defaults:
- `schemaConfirmed = false`
- `confirmedType = inferredType`
- `ignoredColumnsCount = 0`
- `targetColumn = primeira coluna numeric-like`, quando existir

## Metadata Index (Dataset Catalog)

O InsightEngine agora possui um pipeline de indexacao de metadados para datasets tabulares (single-table), inspirado em catalog/explore workflows.

### Capacidades do Explore
- Field explorer com busca, filtro por tipo, null rate, distinct count e semantic tags.
- Overview com qualidade do dataset, candidate keys, top fields e proximos passos.
- Field details com histograma, percentis, top values, densidade temporal e acoes de drilldown.
- Correlations com ranked edges e matrix-lite (top edges, sem NxN completo).
- Data Grid com row sampling, filter pills, sort local e value drilldowns.
- Saved views locais (localStorage) para persistir estado da exploracao.

### Endpoints do Metadata Index
- `POST /api/v1/datasets/{datasetId}/index:build`
- `GET /api/v1/datasets/{datasetId}/index`
- `GET /api/v1/datasets/{datasetId}/index/status`
- `GET /api/v1/datasets/{datasetId}/rows?limit=...&offset=...&filter=column|op|value`
- `GET /api/v1/datasets/{datasetId}/facets?field=...&top=20&filter=column|op|value`

### Screenshot placeholders
- `[Explore - Overview]`
- `[Explore - Field Details]`
- `[Explore - Correlations]`
- `[Explore - Data Grid + Facets]`

### Documentacao complementar
- `docs/METADATA_INDEX_PLAN.md`
- `docs/ARCHITECTURE_METADATA_INDEX.md`
- `docs/QA_METADATA_INDEX.md`

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
      "Model": "llama3.2"
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

### Feature flags V2 (default OFF)
```json
{
  "Features": {
    "RecommendationV2Enabled": false,
    "RecommendationV2DebugLogging": false,
    "LlmStructuredInsightsV2Enabled": false,
    "AutoApplyZeroErrorFormulaEnabled": false,
    "AuthRequiredForDatasets": false,
    "FakePlanUpgradeEnabled": true
  }
}
```

### RecommendationWeights (RecommendationEngineV2)
```json
{
  "RecommendationWeights": {
    "Correlation": 0.35,
    "Variance": 0.2,
    "Completeness": 0.15,
    "Outlier": 0.05,
    "Temporal": 0.1,
    "RoleHint": 0.05,
    "SemanticHint": 0.05,
    "CardinalityPenalty": 0.03,
    "NearConstantPenalty": 0.02
  }
}
```

Notas:
- `RecommendationV2Enabled=true` ativa ranking por relevancia com `DatasetIndex`.
- `RecommendationV2DebugLogging=true` registra top candidatos com breakdown de score (somente log).

### JWT + Refresh Token
```json
{
  "JwtSettings": {
    "Secret": "your-256-bit-secret-key-here-minimum-length",
    "Issuer": "InsightEngine",
    "Audience": "InsightEngine.Users",
    "ExpirationInMinutes": 60,
    "RefreshExpirationInDays": 30
  }
}
```

Backend auth stack:
- ASP.NET Core Identity (`ApplicationUser`) + EF Core store
- JWT access token (15 min default)
- Refresh token rotation persisted em `RefreshTokens`

`AuthRequiredForDatasets=true` habilita protecao obrigatoria para endpoints de dataset.  
Com `false` (default), o sistema permanece compativel com fluxos antigos sem login.

### Endpoints de autenticacao e perfil
- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `GET /api/v1/me`
- `PUT /api/v1/me/profile`
- `PUT /api/v1/me/password`
- `PUT /api/v1/me/avatar`
- `GET /api/v1/me/plan`
- `POST /api/v1/me/plan/upgrade` (fake, controlado por `FakePlanUpgradeEnabled`)
- `LlmStructuredInsightsV2Enabled=true` ativa prompt/payload estruturado para insights (com limite deterministico de tamanho).
- `AutoApplyZeroErrorFormulaEnabled=true` aplica automaticamente formula inferida quando o erro maximo for zero.
- O fluxo LLM usa timeout configuravel por `LLM:TimeoutSeconds`; timeout retorna erro via `Result` (sem exception como controle de fluxo).
- DTOs publicos de indexacao/finalizacao/formula inference possuem validadores FluentValidation antes do mapeamento para Command/Query.

Opcoes adicionais:
- `FormulaInference:EpsilonZero` define o limiar de erro considerado "zero" para early-stop (default `0`).
- `ScenarioSimulation:SimulationFormulaMaxError` define erro maximo permitido para propagacao de formula na simulacao (default `1e-6`).

Provider options:
- `None`: endpoints de AI ativos com fallback heuristico/deterministico.
- `LocalHttp`: provedor local (Ollama/llama.cpp HTTP compativel).
- `OpenAI`: placeholder para integracao futura.

Exemplo local com Ollama:
```bash
ollama serve
ollama pull llama3.2
# ou: ollama pull gemma3
```

## API Overview

Principais endpoints:
- `POST /api/v1/datasets` - upload CSV
- `GET /api/v1/datasets/runtime-config` - limites runtime para frontend
- `GET /api/v1/datasets/{id}/preview?sampleSize=200` - pre-import preview
- `POST /api/v1/datasets/{id}/finalize` - confirma target/ignored/overrides e persiste schema
- `GET /api/v1/datasets/{id}/schema` - retorna schema confirmado (ou backfill default para legado)
- `GET /api/v1/datasets/{id}/recommendations` - recomendacoes
- `POST /api/v1/datasets/{id}/index:build` - constroi metadata index
- `GET /api/v1/datasets/{id}/index` - retorna metadata index persistido
- `GET /api/v1/datasets/{id}/index/status` - status da indexacao
- `GET /api/v1/datasets/{id}/rows` - amostragem de linhas para data grid
- `GET /api/v1/datasets/{id}/facets` - contagens top-N para facetas
- `GET /api/v1/datasets/{id}/charts/{recommendationId}` - chart + insightSummary + meta
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/ai-summary` - resumo AI on-demand
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/explain` - explicacao estruturada
- `POST /api/v1/datasets/{id}/charts/{recommendationId}/deep-insights` - narrativa analitica profunda com citacoes de evidencias
- `POST /api/v1/datasets/{id}/ask` - pergunta NL -> analysis plan (sem SQL execution)
- `POST /api/v1/datasets/{id}/insights/pack` - gera Insight Pack v2 sem expor linhas brutas
- `GET /api/v1/datasets/{id}/insights/pack` - gera Insight Pack v2 via query params (idempotente para debug/manual testing)
- `POST /api/v1/datasets/{id}/insights/ask` - responde pergunta usando apenas Insight Pack + evidencias resolvidas
- `POST /api/v1/datasets/{id}/simulate` - simulacao
- `POST /api/v1/datasets/cleanup` - cleanup manual (dev/admin)
- `GET /health` e `GET /health/ready`
- `POST /api/v1/auth/login` - login demo JWT

## Insights v2 (Pack + Ask)

Objetivo: respostas de negocio acionaveis, auditaveis e seguras por design.

Principios nao-negociaveis:
- Sem envio de linhas brutas para LLM.
- Toda afirmacao numerica deve estar ancorada em evidencia (`evidenceId`).
- Linguagem causal e sanitizada para associacao/correlacao quando necessario.
- Escala percentual preservada em raw scale (0-1, 0-100, ou unknown).

### Fluxo recomendado
1. Gerar pack (`/insights/pack`) com filtros/timeframe/segmentacao.
2. Fazer pergunta (`/insights/ask`) com `outputMode` (`DeepDive` ou `Executive`).
3. Renderizar `answer`, `answerJson`, `evidenceResolved`, `meta.confidenceScore` e `meta.validationStatus`.

### Exemplo: gerar Insight Pack v2
```bash
curl -X POST "http://localhost:5000/api/v1/datasets/{datasetId}/insights/pack" \
  -H "Content-Type: application/json" \
  -d '{
    "recommendationId": "rec_001",
    "aggregation": "Sum",
    "timeBin": "Month",
    "metricY": "sales",
    "groupBy": "region",
    "outputMode": "DeepDive"
  }'
```

### Exemplo: ask com modo Executive
```bash
curl -X POST "http://localhost:5000/api/v1/datasets/{datasetId}/insights/ask" \
  -H "Content-Type: application/json" \
  -d '{
    "recommendationId": "rec_001",
    "question": "Quais acoes priorizar para melhorar o target no proximo mes?",
    "aggregation": "Sum",
    "timeBin": "Month",
    "metricY": "sales",
    "groupBy": "region",
    "outputMode": "Executive"
  }'
```

### Campos principais da resposta de ask v2
- `answer`: resposta textual curta para exibicao imediata.
- `answerJson`: estrutura padronizada (executiveSummary, findings, drivers, recommendations).
- `evidenceResolved`: evidencias resolvidas (`evidenceId`, `label`, `path`, `value`) para auditoria UI.
- `packVersion`: versao do pack utilizada na resposta.
- `meta.confidenceScore`: score agregado (0-1) considerando cobertura de evidencia e confianca declarada.
- `meta.validationStatus`: `ok`, `ok_sanitized` ou `fallback`.

## Exemplos de resposta

### Import preview (resumido)
```json
{
  "success": true,
  "data": {
    "tempUploadId": "8e9e66f6-57f8-4f2d-b7f1-8c6de4a5e40f",
    "sampleSize": 200,
    "columns": [
      { "name": "date", "inferredType": "Date", "confidence": 0.99 },
      { "name": "sales", "inferredType": "Decimal", "confidence": 0.95 }
    ],
    "suggestedTargetCandidates": ["sales"],
    "suggestedIgnoredCandidates": []
  }
}
```

### Finalize import (resumido)
```json
{
  "success": true,
  "data": {
    "datasetId": "8e9e66f6-57f8-4f2d-b7f1-8c6de4a5e40f",
    "schemaVersion": 1,
    "targetColumn": "sales",
    "ignoredColumnsCount": 1,
    "storedColumnsCount": 5,
    "currencyCode": "BRL"
  }
}
```

### Get schema (resumido)
```json
{
  "success": true,
  "data": {
    "datasetId": "8e9e66f6-57f8-4f2d-b7f1-8c6de4a5e40f",
    "schemaVersion": 1,
    "schemaConfirmed": true,
    "targetColumn": "sales",
    "ignoredColumnsCount": 1,
    "columns": [
      { "name": "sales", "inferredType": "Decimal", "confirmedType": "Money", "isTarget": true },
      { "name": "region", "inferredType": "Category", "confirmedType": "Category", "isIgnored": true }
    ]
  }
}
```

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
      "model": "llama3.2",
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
      "model": "llama3.2",
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

### Insight Pack Ask v2 (resumido)
```json
{
  "success": true,
  "data": {
    "answer": "As principais oportunidades estao associadas a segmentos com maior share e estabilidade.",
    "answerJson": {
      "executiveSummary": ["..."],
      "caveats": ["..."]
    },
    "evidenceResolved": [
      {
        "evidenceId": "D1",
        "label": "Driver candidate",
        "path": "packV2.targetStory.driverCandidates.numericDrivers[0]",
        "value": "price (pearson=0.42, spearman=0.39)"
      }
    ],
    "packVersion": "2.0",
    "meta": {
      "provider": "LocalHttp",
      "model": "llama3.2",
      "validationStatus": "ok_sanitized",
      "confidenceScore": 0.78,
      "fallbackUsed": false
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

## Dashboard Home (Dinâmico por Dataset)

Endpoint agregado (backend):

```http
GET /api/v1/dashboard?datasetId={guid}
```

Retorno principal (`DashboardViewModel`):
- `dataset`: resumo do dataset selecionado.
- `kpis`: cards de KPI prontos para renderização.
- `charts`: recomendações de charts.
- `tables.topFeatures` e `tables.dataQuality`.
- `insights`: resumo executivo + warnings.
- `metadata`: disponibilidade de índice/recomendações/fórmula.

Regras:
- Multi-tenant por owner: consulta por `(datasetId + CurrentUserId)`.
- Dataset sem ownership retorna `404` (sem vazamento de existência).
- Dataset sem índice retorna payload gracioso com `metadata.indexAvailable=false` e `charts=[]`.

Frontend:
- Nova home autenticada em `/:lang/dashboard`.
- Seletor de dataset no topo com busca e persistência do último `datasetId` por usuário no `localStorage`.
- Layout responsivo com KPIs, grid de charts, tabelas, insights e metadata.
- Menu lateral mantém `Datasets` como primeiro item e inclui `Dashboard` como segundo item.

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


## Dashboard Cache (Per Dataset, Per User)

The dashboard endpoint (GET /api/v1/dashboard?datasetId={guid}) now uses persistent JSON cache in DashboardCache.

- Cache key scope: OwnerUserId + DatasetId + Version
- Current version: dashboard-v2
- Stored payload: serialized DashboardViewModel JSON
- Validation:
  - cache is reused only when dataset.UpdatedAt <= cached.SourceDatasetUpdatedAt
  - cache is reused only when SourceFingerprint matches current index/formula/feature-state fingerprint
- Invalidation occurs automatically on:
  - dataset updates (new UpdatedAt)
  - index rebuild / formula updates (fingerprint changes)
  - dashboard format/version changes (DashboardCacheVersion bump)


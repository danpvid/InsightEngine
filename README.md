# InsightEngine

> **Auto-BI inteligente** que transforma datasets crus em **insights acionáveis**,  
> sem necessidade de SQL, modelagem manual ou ferramentas complexas.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Architecture](https://img.shields.io/badge/architecture-DDD%20%2B%20CQRS-green.svg)](docs/)

---

## ✨ Visão do Produto

O **InsightEngine** é uma plataforma analítica de próxima geração que democratiza o acesso a insights de negócio através de:

### Capacidades Atuais
- 📤 **Upload Inteligente**: Processamento de datasets CSV com detecção automática de tipos e estrutura
- 🔍 **Profiling Automático**: Análise estatística instantânea (tipos inferidos, distribuição, cardinalidade, valores nulos)
- 🎯 **Recomendações Inteligentes**: Geração automática de até **12 visualizações** baseadas nas características dos dados
- 📊 **Renderização Backend**: ECharts options completos gerados pelo servidor, prontos para visualização
- ⚡ **Performance Otimizada**: Motor analítico DuckDB para queries sub-segundo em datasets grandes
- 🔧 **Production-Ready**: Telemetria, gap filling, tratamento de erros e API documentada

### Roadmap
- 🔗 **Multi-tabela** com relacionamento assistido por IA
- 🎲 **Simulações What-If** para análise de cenários
- 📈 **Previsões** com modelos estatísticos e ML
- 💬 **Copiloto Analítico** em linguagem natural (NLP)

---

## 🎯 Status do Projeto

### ✅ Concluído (v1.0 - MVP) — **Dia 7: Frontend Completo!** 🎉

**Core Features:**
- ✅ Upload de datasets com streaming eficiente (até 20MB)
- ✅ Profiling automático com inferência de tipos (Date, Number, Category, Boolean, String)
- ✅ Engine de recomendações com 4 tipos de charts (Line, Bar, Histogram, Scatter)
- ✅ Execução de **Time Series** com DuckDB
- ✅ Multi-format date parsing (YYYYMMDD, DD/MM/YYYY, YYYY-MM-DD, etc.)
- ✅ Tratamento de números com separadores de milhar

**Hardening & Production Features:**
- ✅ API envelope padronizada (success, data, errors, traceId)
- ✅ Performance telemetry (executionMs, duckDbMs, queryHash)
- ✅ Gap filling configurável (None, Nulls, ForwardFill, Zeros)
- ✅ ECharts defaults (grid, auto dataZoom para >200 pontos)
- ✅ JSON optimization (ignore nulls)
- ✅ Debug SQL em Development

**🆕 Frontend Angular (Dia 7):**
- ✅ Interface web completa com Angular 17 + Material Design
- ✅ Página de upload de CSV com validação
- ✅ Visualização de recomendações em grid responsivo
- ✅ Renderização de gráficos interativos com ECharts
- ✅ Estados de loading e tratamento de erros
- ✅ Navegação entre páginas (Upload → Recommendations → Chart)
- ✅ CORS configurado para desenvolvimento
- ✅ Documentação completa da API (docs/API_CONTRACTS.md)

**Architecture:**
- ✅ DDD (Domain-Driven Design)
- ✅ CQRS (Command Query Responsibility Segregation)
- ✅ Result Pattern para tratamento de erros
- ✅ Domain Notifications
- ✅ Validation Pipeline com FluentValidation

### 🚧 Em Desenvolvimento (v1.1)

- 🔄 Execução de gráficos **Bar** (category × measure)
- 🔄 Execução de gráficos **Histogram** (distribuição)
- 🔄 Execução de gráficos **Scatter** (correlação)
- 🔄 Testes de integração (WebApplicationFactory)

### 📋 Backlog (v2.0+)

- 📅 Multi-dataset com relacionamento assistido
- 📅 Cache de queries com Redis
- 📅 Exportação de insights (PDF, Excel)
- 📅 Alertas e notificações
- 📅 Dashboard builder
- 📅 Simulações What-If
- 📅 Modelos preditivos
- 📅 NLP Copilot

---


## 🏗️ Arquitetura

### Visão Geral

O InsightEngine adota uma **arquitetura limpa e desacoplada**, seguindo os princípios de **DDD (Domain-Driven Design)** e **CQRS (Command Query Responsibility Segregation)**, garantindo:

- 🎯 **Alta Coesão**: Cada camada tem responsabilidades bem definidas
- 🔌 **Baixo Acoplamento**: Dependências invertidas através de interfaces
- 🧪 **Testabilidade**: Separação clara entre lógica de negócio e infraestrutura
- 📈 **Escalabilidade**: Preparado para crescimento horizontal e vertical
- 🔧 **Manutenibilidade**: Código limpo, SOLID e fácil de evoluir

```
┌─────────────────────────────────────────────────────────────┐
│                    InsightEngine.API                         │
│  Controllers │ Middlewares │ Swagger │ Health Checks        │
│  ▶ REST Endpoints                                            │
│  ▶ API Envelope Standardization                             │
│  ▶ HTTP Request/Response Handling                           │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP
┌────────────────────────▼────────────────────────────────────┐
│              InsightEngine.Application                       │
│  Commands │ Queries │ Handlers │ DTOs                      │
│  ▶ Application Services (thin orchestration)                │
│  ▶ MediatR Pipeline (Commands + Queries)                    │
│  ▶ Validation Behavior (FluentValidation)                   │
└────────────────────────┬────────────────────────────────────┘
                         │ CQRS
┌────────────────────────▼────────────────────────────────────┐
│               InsightEngine.Domain                           │
│  Entities │ Value Objects │ Services │ Interfaces          │
│  ▶ Business Logic (RecommendationEngine, Profiler)         │
│  ▶ Domain Events & Notifications                            │
│  ▶ Result Pattern & Error Handling                          │
└────────────────────────┬────────────────────────────────────┘
                         │ Domain Interfaces
┌────────────────────────▼────────────────────────────────────┐
│              InsightEngine.Infra.Data                        │
│  Repositories │ DuckDB │ File Storage │ CSV Profiler       │
│  ▶ ChartExecutionService (DuckDB analytical engine)         │
│  ▶ FileStorageService (streaming uploads)                   │
│  ▶ CsvProfiler (type inference + statistics)                │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│          InsightEngine.CrossCutting (IoC)                    │
│  ▶ Dependency Injection Bootstrap                            │
│  ▶ Configuration Management                                  │
│  ▶ Logging & Telemetry Setup                               │
└─────────────────────────────────────────────────────────────┘
```

### Camadas Detalhadas

#### 1. **InsightEngine.API** (Presentation Layer)
**Responsabilidades:**
- Exposição de endpoints REST
- Validação de entrada HTTP
- Transformação de DTOs (Domain → API)
- Middleware pipeline (CORS, Auth, Error Handling)
- Documentação Swagger/OpenAPI

**Componentes Principais:**
- `DataSetController`: Upload, Profile, Recommendations, Chart Execution
- `BaseController`: Response helpers (ApiResponse, ApiErrorResponse)
- `Program.cs`: Host configuration, middleware setup
- `appsettings.json`: Environment-specific configuration

#### 2. **InsightEngine.Application** (Application Layer)
**Responsabilidades:**
- Orchestration de casos de uso
- Validação de negócio (FluentValidation)
- Coordenação entre Domain e Infrastructure
- Transaction boundaries

**Componentes Principais:**
- `IDataSetApplicationService`: Fachada para operações de dataset
- `ValidationBehavior`: Pipeline para validação automática
- MediatR Handlers: Processam Commands e Queries

#### 3. **InsightEngine.Domain** (Domain Layer)
**Responsabilidades:**
- **Core Business Logic** (livre de infraestrutura)
- Regras de negócio e invariantes
- Domain Services complexos
- Value Objects e Entities

**Componentes Principais:**
- `RecommendationEngine`: Gera recomendações de charts baseado no profile
- `ChartRecommendation`: Value Object com specs de visualização
- `DatasetProfile`: Representação do profiling estatístico
- `Result<T>`: Pattern para tratamento de sucesso/falha
- Interfaces: `IChartExecutionService`, `ICsvProfiler`, `IFileStorageService`

**Domain Services:**
```csharp
RecommendationEngine
├── DetectColumnRoles (Time, Measure, Category, Id)
├── GenerateTimeSeriesRecommendations (Line charts)
├── GenerateCategoryBarRecommendations (Bar charts)
├── GenerateHistogramRecommendations (Distributions)
└── GenerateScatterRecommendations (Correlations)
```

#### 4. **InsightEngine.Infra.Data** (Infrastructure Layer)
**Responsabilidades:**
- Persistência (file system)
- Motor analítico (DuckDB)
- Profiling de CSV
- I/O operations

**Componentes Principais:**
- `ChartExecutionService`: Executa queries DuckDB e retorna ECharts options
  - Multi-format date parsing (COALESCE + TRY_STRPTIME)
  - Number sanitization (remove thousand separators)
  - Gap filling (temporal completeness)
  - Query hash para cache/deduplication
- `FileStorageService`: Upload streaming com buffer 80KB
- `CsvProfiler`: Type inference com heurísticas avançadas

**DuckDB Integration:**
```sql
-- Example generated query
SELECT 
    date_trunc('day', parsed_date) AS x,
    AVG(parsed_value) AS y
FROM (
    SELECT 
        COALESCE(
            TRY_CAST("date" AS TIMESTAMP),
            TRY_STRPTIME(CAST("date" AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST("date" AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST("date" AS VARCHAR), '%Y-%m-%d')
        ) AS parsed_date,
        CAST(REPLACE(CAST("sales" AS VARCHAR), ',', '') AS DOUBLE) AS parsed_value
    FROM read_csv_auto('uploads/dataset.csv', header=true)
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL
GROUP BY 1 ORDER BY 1;
```

#### 5. **InsightEngine.CrossCutting** (Cross-Cutting Concerns)
**Responsabilidades:**
- Dependency Injection setup
- Configuration binding
- Logging infrastructure
- Shared utilities

**Componentes Principais:**
- `NativeInjectorBootStrapper`: Registra todos os serviços
- Configuration: ChartExecutionSettings, FileStorage paths

---

## 🚀 Tecnologias & Stack

### Backend Core
- **[.NET 8.0](https://dotnet.microsoft.com/)** - Framework principal
- **[ASP.NET Core](https://docs.microsoft.com/aspnet/core)** - Web API
- **[C# 12](https://learn.microsoft.com/dotnet/csharp/)** - Linguagem

### Analytical Engine
- **[DuckDB.NET 1.1.3](https://duckdb.org/)** - In-process analytical database
  - OLAP queries em memória
  - Processamento columnar
  - Zero-configuration
  - Native CSV reading

### Architecture & Patterns
- **[MediatR 12.2.0](https://github.com/jbogard/MediatR)** - CQRS implementation
- **[FluentValidation 11.9.0](https://fluentvalidation.net/)** - Validation pipeline
- **Result Pattern** - Functional error handling
- **Domain Notifications** - Decoupled error collection

### Serialization & API
- **System.Text.Json** - High-performance JSON
- **Swagger/OpenAPI** - API documentation
- **ECharts** - Visualization library (options generated server-side)

### Development & Quality
- **Serilog** - Structured logging
- **Polly** - Resilience policies (retry, circuit breaker)
- **xUnit** - Unit testing framework
- **FluentAssertions** - Assertion library

### DevOps & Deployment
- **Docker** - Containerization
- **GitHub Actions** - CI/CD (planned)
- **Application Insights** - Monitoring (planned)

---

## 📁 Estrutura de Pastas

```
InsightEngine/
├── 📂 src/
│   ├── 📦 InsightEngine.API/
│   │   ├── Controllers/
│   │   │   └── V1/
│   │   │       ├── BaseController.cs
│   │   │       └── DataSetController.cs
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs
│   │   │   ├── ApiErrorResponse.cs
│   │   │   └── ChartExecutionResponse.cs
│   │   ├── Middlewares/
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   │
│   ├── 📦 InsightEngine.Application/
│   │   ├── Services/
│   │   │   ├── IDataSetApplicationService.cs
│   │   │   └── DataSetApplicationService.cs
│   │   └── Behaviors/
│   │       └── ValidationBehavior.cs
│   │
│   ├── 📦 InsightEngine.Domain/
│   │   ├── Commands/
│   │   │   └── DataSet/
│   │   │       ├── UploadDataSetCommand.cs
│   │   │       └── UploadDataSetCommandHandler.cs
│   │   ├── Queries/
│   │   │   └── DataSet/
│   │   │       ├── GetDataSetProfileQuery.cs
│   │   │       ├── GetDataSetRecommendationsQuery.cs
│   │   │       └── GetDataSetChartQuery.cs
│   │   ├── Models/
│   │   │   ├── DatasetProfile.cs
│   │   │   ├── ChartRecommendation.cs
│   │   │   ├── ChartExecutionResult.cs
│   │   │   └── EChartsOption.cs
│   │   ├── Services/
│   │   │   ├── RecommendationEngine.cs
│   │   │   └── EChartsOptionTemplateFactory.cs
│   │   ├── Enums/
│   │   │   ├── ChartType.cs
│   │   │   ├── InferredType.cs
│   │   │   ├── AxisRole.cs
│   │   │   ├── Aggregation.cs
│   │   │   ├── TimeBin.cs
│   │   │   └── GapFillMode.cs
│   │   ├── Helpers/
│   │   │   ├── QueryHashHelper.cs
│   │   │   └── GapFillHelper.cs
│   │   ├── Interfaces/
│   │   │   ├── IChartExecutionService.cs
│   │   │   ├── ICsvProfiler.cs
│   │   │   └── IFileStorageService.cs
│   │   └── Core/
│   │       ├── Result.cs
│   │       ├── Query.cs
│   │       ├── Command.cs
│   │       └── DomainNotification.cs
│   │
│   ├── 📦 InsightEngine.Infra.Data/
│   │   ├── Services/
│   │   │   ├── ChartExecutionService.cs
│   │   │   ├── CsvProfiler.cs
│   │   │   └── FileStorageService.cs
│   │   └── Configuration/
│   │       └── ChartExecutionSettings.cs
│   │
│   └── 📦 InsightEngine.CrossCutting/
│       └── IoC/
│           └── NativeInjectorBootStrapper.cs
│
├── 📂 tools/
│   └── InsightEngine.DataGenerator/
│       └── Templates/
│           └── BusinessTemplates.cs
│
├── 📂 docs/
│   ├── API_CONTRACTS.md
│   ├── DAY-1-PROFILING.md
│   ├── DAY-2-RECOMMENDATIONS.md
│   ├── DAY-3-ECHARTS-TEMPLATES.md
│   └── DAY-4-EXECUTION.md
│
├── 📂 samples/
│   └── README.md
│
├── InsightEngine.sln
└── README.md
```

---

## 🚀 Getting Started

### Pré-requisitos

**Backend:**
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- IDE: [Visual Studio 2022](https://visualstudio.microsoft.com/) ou [VS Code](https://code.visualstudio.com/) + C# Extension

**Frontend (Novo!):**
- [Node.js 18+](https://nodejs.org/) e npm
- [Angular CLI 17+](https://angular.io/cli): `npm install -g @angular/cli`

### 🎯 Início Rápido (Completo com Frontend)

#### Opção 1: Scripts Automatizados (Windows)

```bash
# 1. Setup (apenas primeira vez)
setup.bat

# 2. Iniciar demo completa
start-demo.bat
```

#### Opção 2: Comandos Manuais

**1. Instalar Dependências do Frontend (primeira vez apenas)**
```bash
cd src/InsightEngine.Web
npm install
```

**2. Iniciar Backend (Terminal 1)**
```bash
cd src/InsightEngine.API
dotnet run
```

**3. Iniciar Frontend (Terminal 2)**
```bash
cd src/InsightEngine.Web
npm start
```

**4. Acessar**
- **Frontend (UI)**: http://localhost:4200
- **Backend (API)**: https://localhost:5001
- **Swagger UI**: https://localhost:5001/swagger

### 📖 Guias Disponíveis

- **[LEIA-PRIMEIRO.md](LEIA-PRIMEIRO.md)** - Resolução de erros iniciais
- **[START-HERE.md](START-HERE.md)** - Guia de início rápido
- **[QUICK-START-DEMO.md](QUICK-START-DEMO.md)** - Roteiro de teste completo
- **[docs/DAY7_FRONTEND_SUMMARY.md](docs/DAY7_FRONTEND_SUMMARY.md)** - Documentação do frontend

---

## 🖥️ Como Usar a Interface Web

### 1️⃣ Upload de Dataset
1. Acesse http://localhost:4200/datasets/new
2. Selecione um arquivo CSV (use os samples disponíveis em `samples/`)
3. Clique em "Enviar e Gerar Recomendações"

### 2️⃣ Visualizar Recomendações
- Após o upload, você será redirecionado automaticamente
- Veja as recomendações de gráficos geradas pela IA
- Cada card mostra: tipo, eixos, e justificativa

### 3️⃣ Visualizar Gráfico Interativo
- Clique em qualquer recomendação
- O gráfico ECharts será renderizado
- Interaja: hover, zoom, clique na legenda
- Veja metadados: tempo de execução, linhas retornadas, etc.

---

## 📊 Exemplos de Uso (Apenas API)

### Instalação & Execução (Backend Standalone)

1. **Clone o repositório**
```bash
git clone https://github.com/danpvid/InsightEngine.git
cd InsightEngine
```

2. **Restaure as dependências**
```bash
dotnet restore
```

3. **Compile o projeto**
```bash
dotnet build
```

4. **Execute a API**
```bash
cd src/InsightEngine.API
dotnet run
```

A API estará disponível em:
- **HTTPS**: `https://localhost:5001`
- **HTTP**: `http://localhost:5000`
- **Swagger UI**: `https://localhost:5001/swagger`

### Configuração

Edite `appsettings.json` ou `appsettings.Development.json`:

```json
{
  "FileStorage": {
    "BasePath": "uploads"
  },
  "ChartExecution": {
    "GapFillMode": "Nulls",
    "EnableAutoDataZoom": true,
    "DataZoomThreshold": 200,
    "IncludeDebugSql": false
  }
}
```

**Opções de Gap Filling:**
- `None`: Sem preenchimento
- `Nulls`: Preenche com valores nulos
- `ForwardFill`: Propaga último valor válido
- `Zeros`: Preenche com zeros

---

## 📊 Exemplos de Uso da API

### 1. Upload de Dataset

```bash
curl -X POST https://localhost:5001/api/v1/datasets \
  -F "file=@sales_data.csv"
```

**Response:**
```json
{
  "success": true,
  "message": "Arquivo enviado com sucesso.",
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "sales_data.csv",
    "storedFileName": "3fa85f64-5717-4562-b3fc-2c963f66afa6.csv",
    "sizeBytes": 1048576,
    "createdAtUtc": "2026-02-14T20:00:00Z"
  }
}
```

### 2. Profile do Dataset

```bash
curl https://localhost:5001/api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/profile
```

**Response:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "rowCount": 1000,
    "sampleSize": 1000,
    "columns": [
      {
        "name": "order_date",
        "inferredType": "Date",
        "nullRate": 0.0,
        "distinctCount": 365,
        "topValues": ["2024-01-01", "2024-01-02", "2024-01-03"]
      },
      {
        "name": "revenue",
        "inferredType": "Number",
        "nullRate": 0.0,
        "distinctCount": 950,
        "topValues": ["1500.50", "2300.75", "1800.00"]
      },
      {
        "name": "category",
        "inferredType": "Category",
        "nullRate": 0.0,
        "distinctCount": 5,
        "topValues": ["Electronics", "Furniture", "Clothing"]
      }
    ]
  }
}
```

### 3. Obter Recomendações de Visualização

```bash
curl https://localhost:5001/api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/recommendations
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": "rec_001",
      "title": "revenue over time",
      "reason": "Time column + numeric measure: time series with daily average.",
      "chart": {
        "library": "ECharts",
        "type": "Line"
      },
      "query": {
        "x": {
          "column": "order_date",
          "role": "Time",
          "bin": "Day"
        },
        "y": {
          "column": "revenue",
          "role": "Measure",
          "aggregation": "Avg"
        }
      },
      "xColumn": "order_date",
      "yColumn": "revenue",
      "aggregation": "Avg",
      "timeBin": "Day"
    }
  ]
}
```

### 4. Executar Visualização

```bash
curl https://localhost:5001/api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/charts/rec_001
```

**Response:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recommendationId": "rec_001",
    "option": {
      "title": {
        "text": "revenue over time",
        "subtext": "Time column + numeric measure: time series with daily average."
      },
      "tooltip": {
        "trigger": "axis",
        "axisPointer": { "type": "cross" }
      },
      "grid": {
        "left": "3%",
        "right": "4%",
        "bottom": "10%",
        "top": "15%",
        "containLabel": true
      },
      "xAxis": {
        "type": "time",
        "name": "order_date"
      },
      "yAxis": {
        "type": "value",
        "name": "revenue"
      },
      "series": [{
        "name": "Avg(revenue)",
        "type": "line",
        "smooth": true,
        "data": [
          [1704067200000, 1500.50],
          [1704153600000, 2300.75],
          [1704240000000, 1800.00]
        ]
      }]
    },
    "meta": {
      "executionMs": 15,
      "duckDbMs": 12,
      "queryHash": "757e2aa5b00d27c8a6683ef29a2b883f...",
      "rowCountReturned": 365,
      "chartType": "line",
      "generatedAt": "2026-02-14T20:30:00Z"
    }
  }
}
```

---

## 📖 Documentação

- **[API Contracts](docs/API_CONTRACTS.md)** - Documentação completa dos endpoints
- **[Day 1 - Profiling](docs/DAY-1-PROFILING.md)** - Detecção de tipos e estatísticas
- **[Day 2 - Recommendations](docs/DAY-2-RECOMMENDATIONS.md)** - Engine de recomendações
- **[Day 3 - ECharts Templates](docs/DAY-3-ECHARTS-TEMPLATES.md)** - Templates de visualização
- **[Day 4 - Execution](docs/DAY-4-EXECUTION.md)** - Execução de queries com DuckDB

---

## 🧪 Testes

```bash
# Executar todos os testes
dotnet test

# Com cobertura de código
dotnet test /p:CollectCoverage=true
```

---

## 🐳 Docker

```bash
# Build da imagem
docker build -t insightengine:latest .

# Executar container
docker run -p 5000:80 -p 5001:443 insightengine:latest
```

---

## 🤝 Contribuindo

Contribuições são bem-vindas! Por favor:

1. Fork o projeto
2. Crie uma branch para sua feature (`git checkout -b feature/AmazingFeature`)
3. Commit suas mudanças (`git commit -m 'Add some AmazingFeature'`)
4. Push para a branch (`git push origin feature/AmazingFeature`)
5. Abra um Pull Request

### Guidelines

- Siga os padrões DDD e CQRS
- Adicione testes unitários para novas features
- Documente APIs públicas
- Mantenha mensagens de commit descritivas

---

## 📝 Licença

Este projeto está sob a licença MIT. Veja o arquivo [LICENSE](LICENSE) para mais detalhes.

---

## 👥 Autores

- **Dan Zanin** - *Initial work* - [danpvid](https://github.com/danpvid)

---

## 🙏 Agradecimentos

- DuckDB team pela incrível analytical database
- MediatR community
- ECharts team pelas poderosas visualizações

---

## 📧 Contato

Para dúvidas ou sugestões, entre em contato através das issues do GitHub.

---

**InsightEngine** - Transformando dados em insights acionáveis 🚀
- ✅ Clean Architecture
- ✅ JWT Bearer Authentication

## 🔧 Como Executar

1. **Restaurar pacotes:**
```bash
dotnet restore
```

2. **Atualizar connection string** no `appsettings.json` da API

3. **Criar as migrations:**
```bash
cd src/InsightEngine.Infra.Data
dotnet ef migrations add InitialCreate -s ../InsightEngine.API/InsightEngine.API.csproj
```

4. **Aplicar migrations:**
```bash
dotnet ef database update -s ../InsightEngine.API/InsightEngine.API.csproj
```

5. **Executar a aplicação:**
```bash
cd src/InsightEngine.API
dotnet run
```

6. **Acessar o Swagger:**
```
https://localhost:5001/swagger
```

## 🔐 Autenticação

O projeto está configurado com JWT Bearer Token. Para testar:

1. **Obter um token:**
```bash
POST https://localhost:5001/api/auth/login
Content-Type: application/json

{
  "email": "usuario@exemplo.com",
  "password": "senha123"
}
```

2. **Usar o token no Swagger:**
   - Clique no botão "Authorize" 🔒
   - Digite: `Bearer {seu_token_aqui}`
   - Clique em "Authorize"

3. **Configurações JWT** (appsettings.json):
```json
{
  "JwtSettings": {
    "SecretKey": "sua-chave-secreta-minimo-32-caracteres",
    "Issuer": "InsightEngine.API",
    "Audience": "InsightEngine.Client",
    "ExpirationInMinutes": 480
  }
}
```

**⚠️ IMPORTANTE:** Altere a `SecretKey` em produção para uma chave forte e segura!

## 📦 Gerenciamento de Pacotes

Este projeto utiliza **Central Package Management (CPM)** do NuGet para gerenciar versões de pacotes de forma centralizada.

- ✅ Todas as versões são definidas em `Directory.Packages.props`
- ✅ Arquivos `.csproj` apenas referenciam os pacotes (sem versão)
- ✅ Previne conflitos de versão entre projetos
- ✅ Facilita atualizações e manutenção

**Para adicionar um novo pacote:**

1. Adicione a versão no `Directory.Packages.props`:
```xml
<PackageVersion Include="Serilog" Version="3.1.1" />
```

2. Referencie no projeto (.csproj):
```xml
<PackageReference Include="Serilog" />
```

📚 Veja mais detalhes em [PACKAGES.md](PACKAGES.md)

## 📁 Estrutura de Pastas

```
InsightEngine/
├── src/
│   ├── InsightEngine.API/
│   │   ├── Controllers/
│   │   ├── Properties/
│   │   └── Program.cs
│   ├── InsightEngine.Application/
│   │   ├── AutoMapper/
│   │   ├── Commands/
│   │   ├── Models/
│   │   └── Queries/
│   ├── InsightEngine.Domain/
│   │   ├── Core/
│   │   │   ├── Models/
│   │   │   └── Notifications/
│   │   └── Interfaces/
│   ├── InsightEngine.Infra.Data/
│   │   ├── Context/
│   │   ├── Repositories/
│   │   └── UoW/
│   ├── InsightEngine.Infra.ExternalService/
│   └── InsightEngine.CrossCutting/
│       └── IoC/
├── tests/
├── Directory.Packages.props     # 📦 Gerenciamento centralizado de pacotes
├── InsightEngine.sln
├── README.md
├── PACKAGES.md                  # 📚 Documentação de pacotes
├── SECURITY.md
└── .gitignore
```

## 💡 Exemplo de Uso

### Criando um Command

```csharp
public class CreateUserCommand : Command
{
    public string Name { get; set; }
    public string Email { get; set; }
    
    public override bool IsValid()
    {
        // Validação com FluentValidation
        return true;
    }
}
```

### Criando um Handler

```csharp
public class CreateUserCommandHandler : CommandHandler, IRequestHandler<CreateUserCommand, bool>
{
    public CreateUserCommandHandler(
        IDomainNotificationHandler notificationHandler,
        IUnitOfWork unitOfWork) : base(notificationHandler, unitOfWork)
    {
    }
    
    public async Task<bool> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsValid())
        {
            NotifyError("Command", "Dados inválidos");
            return false;
        }
        
        // Lógica de negócio
        
        return await CommitAsync();
    }
}
```

### Criando um Controller

```csharp
[Route("api/[controller]")]
public class UsersController : BaseController
{
    public UsersController(
        IDomainNotificationHandler notificationHandler,
        IMediator mediator) : base(notificationHandler, mediator)
    {
    }
    
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command)
    {
        return await SendCommand(command);
    }
}
```

## 📝 Licença

Este projeto está sob a licença MIT.

# InsightEngine — Dia 4: Execução de Gráfico Real (Line / Time Series)

## 📊 Objetivo Alcançado

Entregar o primeiro "valor visível" do produto: executar uma recomendação de gráfico e retornar **EChartsOption completo com dados reais**, pronto para renderização.

---

## ✅ Features Implementadas

### 🎯 Endpoint Principal
```
GET /api/v1/datasets/{datasetId}/charts/{recommendationId}
```

### 📦 Response Envelope (com Telemetria)
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recommendationId": "rec_001",
    "option": {
      "title": {
        "text": "Sales over time",
        "subtext": "Daily average trend analysis"
      },
      "tooltip": {
        "trigger": "axis",
        "axisPointer": { "type": "cross" }
      },
      "xAxis": {
        "type": "time",
        "name": "createdAt"
      },
      "yAxis": {
        "type": "value",
        "name": "amount"
      },
      "series": [{
        "name": "Avg(amount)",
        "type": "line",
        "smooth": true,
        "data": [
          [1704067200000, 42.3],
          [1704153600000, 44.1],
          [1704240000000, 39.8]
        ]
      }]
    },
    "meta": {
      "rowCountReturned": 365,
      "chartType": "line",
      "generatedAt": "2026-02-14T10:30:00Z"
    }
  }
}
```

---

## 🏗️ Arquitetura Implementada

### Domain Layer (Contratos)
```
Domain/
├── Interfaces/
│   └── IChartExecutionService.cs          ✨ NEW - Contrato de execução
├── Models/
│   └── EChartsOption.cs                   ✨ NEW - Modelo tipado para ECharts
└── Queries/DataSet/
    ├── GetDataSetChartQuery.cs            ✨ NEW - CQRS Query
    ├── GetDataSetChartQueryValidator.cs   ✨ NEW - FluentValidation
    └── GetDataSetChartQueryHandler.cs     ✨ NEW - Business Logic
```

### Infrastructure Layer (Implementação)
```
Infra.Data/
├── Services/
│   └── ChartExecutionService.cs           ✨ NEW - DuckDB Executor (249 linhas)
└── InsightEngine.Infra.Data.csproj        📝 UPDATED - +DuckDB.NET.Data.Full
```

### Application Layer (Orquestração)
```
Application/Services/
├── IDataSetApplicationService.cs          📝 UPDATED - +GetChartAsync
└── DataSetApplicationService.cs           📝 UPDATED - Implementação
```

### API Layer (HTTP)
```
API/Controllers/V1/
└── DataSetController.cs                   📝 UPDATED - +GetChart endpoint
```

### CrossCutting (DI)
```
CrossCutting/IoC/
└── NativeInjectorBootStrapper.cs          📝 UPDATED - +IChartExecutionService
```

---

## 🔧 Tecnologias Adicionadas

### DuckDB.NET.Data.Full 1.1.3
- Motor analítico embutido (in-process)
- Suporte nativo a `read_csv_auto()`
- Funções analíticas: `date_trunc`, agregações
- Zero configuração, zero servidor externo
- Perfeito para MVP

```xml
<PackageVersion Include="DuckDB.NET.Data.Full" Version="1.1.3" />
```

---

## 📋 Escopo do Dia 4 MVP

### ✅ Suportado
- **ChartType**: `Line` apenas
- **ChartLibrary**: `ECharts` apenas
- **TimeBin**: `Day`, `Month`, `Year`
- **Aggregation**: `Sum`, `Avg`, `Count`, `Min`, `Max`
- **AxisRole**: `Time` (X), `Measure` (Y)

### 🚫 Não Suportado (Future)
- Bar, Scatter, Histogram (Dia 5+)
- D3.js, Chart.js (Dia 5+)
- Week, Quarter bins (Dia 5+)
- Multiple series (Dia 5+)
- Filtros complexos (Dia 5+)

---

## 🔐 Segurança e Robustez

### SQL Injection Protection
```csharp
// ❌ NUNCA faça isso
var sql = $"SELECT * FROM read_csv_auto('{csvPath}')";

// ✅ SEMPRE use parameters
command.CommandText = "SELECT * FROM read_csv_auto(@csvPath)";
command.Parameters.Add(new { ParameterName = "@csvPath", Value = csvPath });
```

### CAST Safety
```sql
-- Protege contra CSV com colunas string
CAST("createdAt" AS TIMESTAMP)
CAST("amount" AS DOUBLE)
```

### Validações Completas
1. ✅ Dataset existe?
2. ✅ ChartLibrary = ECharts?
3. ✅ ChartType = Line?
4. ✅ X tem TimeBin?
5. ✅ Y tem Aggregation?
6. ✅ Roles corretos (Time/Measure)?

---

## 📊 SQL Gerado (Exemplo)

### Input
- **Column X**: `createdAt` (Time, Bin=Day)
- **Column Y**: `amount` (Measure, Aggregation=Avg)

### Output SQL
```sql
SELECT 
    date_trunc('day', CAST("createdAt" AS TIMESTAMP)) AS x,
    AVG(CAST("amount" AS DOUBLE)) AS y
FROM read_csv_auto(@csvPath, header=true, ignore_errors=true)
WHERE "createdAt" IS NOT NULL AND "amount" IS NOT NULL
GROUP BY 1
ORDER BY 1;
```

### Result
```
x                    | y
---------------------|-------
2024-01-01 00:00:00 | 42.30
2024-01-02 00:00:00 | 44.10
2024-01-03 00:00:00 | 39.80
```

### Transformed to ECharts
```json
"data": [
  [1704067200000, 42.3],
  [1704153600000, 44.1],
  [1704240000000, 39.8]
]
```

---

## 🎯 Fluxo End-to-End (Dia 4)

```
1. POST /api/v1/datasets
   ↓ Upload CSV (ecommerce_sales.csv)
   ← { datasetId: "abc-123" }

2. GET /api/v1/datasets/abc-123/recommendations
   ↓ Generate 12 recommendations
   ← [
       { id: "rec_001", title: "Sales over time (Line)", ... },
       { id: "rec_002", title: "Revenue by category (Bar)", ... },
       ...
     ]

3. GET /api/v1/datasets/abc-123/charts/rec_001   ⭐ NEW
   ↓ Execute DuckDB query
   ↓ Aggregate time series
   ↓ Build EChartsOption
   ← {
       option: { xAxis, yAxis, series: [{ data: [[ts,val]] }] },
       meta: { rowCountReturned: 365 }
     }

4. Frontend
   const myChart = echarts.init(dom);
   myChart.setOption(response.data.option);  // ✨ PRONTO!
```

---

## 🧪 Smoke Test (Swagger)

### Passo 1: Upload
```bash
POST /api/v1/datasets
Content-Type: multipart/form-data

file: ecommerce_sales.csv
```

### Passo 2: Recommendations
```bash
GET /api/v1/datasets/{id}/recommendations

Response:
[
  { "id": "rec_001", "title": "createdAt vs amount (Line)", ... },
  ...
]
```

### Passo 3: Execute Chart ⭐
```bash
GET /api/v1/datasets/{id}/charts/rec_001

Response:
{
  "success": true,
  "data": {
    "datasetId": "{id}",
    "recommendationId": "rec_001",
    "option": {
      "xAxis": { "type": "time" },
      "series": [{
        "type": "line",
        "data": [[1704067200000, 42.3], ...]
      }]
    },
    "meta": {
      "rowCountReturned": 365
    }
  }
}
```

### Critério de Aceite ✅
- Status 200
- `option.xAxis.type == "time"`
- `option.series[0].type == "line"`
- `option.series[0].data.length > 0`
- `data[0][0]` é timestamp em ms
- `data[0][1]` é number

---

## 📈 Estatísticas do Dia 4

| Métrica | Valor |
|---------|-------|
| **Arquivos Novos** | 6 |
| **Arquivos Modificados** | 6 |
| **Linhas Adicionadas** | 563 |
| **Endpoints Novos** | 1 (GET /charts/{recId}) |
| **Commands** | 1 |
| **Queries** | 4 (+1 nova) |
| **Validators** | 5 (+1 novo) |
| **Handlers** | 5 (+1 novo) |
| **Domain Services** | 3 (+1 novo: ChartExecutionService) |
| **Pacotes NuGet** | +1 (DuckDB) |
| **Chart Types Suportados** | 1 (Line) |
| **Aggregations Suportadas** | 5 (Sum, Avg, Count, Min, Max) |
| **Time Bins Suportados** | 3 (Day, Month, Year) |

---

## 🎓 Aprendizados Técnicos

### 1. DuckDB é perfeito para MVP
- Zero setup
- In-process (sem servidor)
- Suporta CSV direto com `read_csv_auto()`
- Performance excelente para datasets < 100MB

### 2. Separação de Responsabilidades
```
Domain → IChartExecutionService (O QUE)
Infra → ChartExecutionService (COMO - DuckDB)
```

### 3. Result Pattern com Generics
```csharp
// ❌ Erro de compilação
return Result.Failure("error");  // Result não é Result<T>

// ✅ Correto
return Result<EChartsOption>.Failure<EChartsOption>("error");
```

### 4. On-Demand Recommendations (MVP Smart)
- Não persistir recommendations no DB (Day 4)
- Regenerar on-demand em cada chamada
- Elimina complexidade de cache/versioning
- Performance OK para MVP (<100ms)

---

## 🚀 Próximos Passos (Dia 5+)

### Dia 5: Expansão de Chart Types
- [ ] Suportar Bar (category-based)
- [ ] Suportar Scatter (2 measures)
- [ ] Suportar Histogram (bins automáticos)
- [ ] Multiple series (group by category)

### Dia 6: Performance
- [ ] Cache de execução (datasetId + recId)
- [ ] Persistir results em JSON
- [ ] Sampling para datasets grandes (>1M rows)
- [ ] Lazy loading / pagination

### Dia 7: Frontend
- [ ] React component: DatasetUploader
- [ ] React component: RecommendationList
- [ ] React component: ChartRenderer (ECharts)
- [ ] Auto-refresh on upload

---

## 🎉 Definition of Done - Dia 4

✅ **Compilação**: Zero erros  
✅ **Endpoint**: GET /charts/{recId} funcional  
✅ **DuckDB**: Executando queries parametrizadas  
✅ **EChartsOption**: Completo com series.data  
✅ **Validação**: FluentValidation integrada  
✅ **Telemetria**: Response envelope com meta  
✅ **DDD**: Commands/Queries na Domain  
✅ **Git**: Commit + Push para main  
✅ **Documentação**: README do Dia 4 completo  

---

## 📝 Commits

```
50b4554 - feat(day-4): implement chart execution with DuckDB for Line/Time Series

12 files changed, 563 insertions(+)
- Domain: +3 Query files, +1 Interface, +1 Model
- Infra: +1 ChartExecutionService (249 linhas)
- Application: +1 método GetChartAsync
- API: +1 endpoint GetChart
- CrossCutting: +1 DI registration
- Packages: +DuckDB.NET.Data.Full 1.1.3
```

---

## 🎯 Estado Atual do Produto

### Endpoints Disponíveis
1. ✅ `POST /api/v1/datasets` - Upload CSV
2. ✅ `GET /api/v1/datasets` - List all datasets
3. ✅ `GET /api/v1/datasets/{id}` - Get dataset (stub)
4. ✅ `GET /api/v1/datasets/{id}/profile` - Profile analysis
5. ✅ `GET /api/v1/datasets/{id}/recommendations` - 12 smart recommendations
6. ✅ `GET /api/v1/datasets/{id}/charts/{recId}` ⭐ **NOVO - Execute chart**

### Pipeline Completo
```
CSV Upload → Profile → Recommendations → Execute → Visualize
```

### Próximo Marco
🎯 **Demo Ready**: Frontend React renderizando gráficos reais!

---

## 💡 Observações de Produto

### Timezone
- **MVP**: Tratado como "local time"
- **Future**: Adicionar timezone no QuerySpec

### Performance
- **MVP**: Processar arquivo completo
- **Otimização futura**: 
  - Sampling para datasets >1M rows
  - Cache por datasetVersion + recId
  - Materialized views

### Segurança
- ✅ Parametrização de queries (SQL injection protected)
- ✅ Validação de extensão .csv
- ✅ Limite de 20MB por arquivo
- ✅ GUID como filename (path traversal protected)

---

**🎉 Dia 4 COMPLETO! Primeira visualização real funcionando!**

Agora o InsightEngine consegue:
- ✅ Receber CSVs
- ✅ Analisar estrutura
- ✅ Gerar recomendações inteligentes
- ✅ **Executar queries analíticas** ⭐
- ✅ **Retornar gráficos prontos para renderizar** ⭐

**Next**: Frontend React ou expandir para outros chart types (Bar, Scatter, Histogram).

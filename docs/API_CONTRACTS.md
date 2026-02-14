# API Contracts - InsightEngine MVP

> Documentação completa dos endpoints REST para consumo do frontend.
> Versão: 1.0 | Data: 2026-02-14

## Base URL
```
http://localhost:5000/api/v1
```

## Autenticação
Por enquanto, todos os endpoints estão com `[AllowAnonymous]` para MVP.

---

## 1. Upload Dataset

### `POST /datasets`

Upload de arquivo CSV (até 20MB).

**Request:**
```http
POST /api/v1/datasets
Content-Type: multipart/form-data

file: [arquivo.csv]
```

**Response 201 Created:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "vendas.csv",
    "storedFileName": "3fa85f64-5717-4562-b3fc-2c963f66afa6.csv",
    "sizeBytes": 1048576,
    "createdAtUtc": "2026-02-14T10:30:00Z"
  },
  "errors": null,
  "traceId": "00-1234567890abcdef-1234567890abcdef-00"
}
```

**Response 400 Bad Request:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "file": ["File is required", "File must be a CSV"]
  },
  "traceId": "00-1234567890abcdef-1234567890abcdef-00"
}
```

**Status Codes:**
- `201` - Created
- `400` - Bad Request (validação)
- `413` - Payload Too Large (> 20MB)
- `500` - Internal Server Error

---

## 2. Get All Datasets

### `GET /datasets`

Lista todos os datasets carregados.

**Request:**
```http
GET /api/v1/datasets
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": [
    {
      "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "originalFileName": "vendas.csv",
      "sizeBytes": 1048576,
      "rowCount": 10000,
      "columnCount": 15,
      "uploadedAt": "2026-02-14T10:30:00Z"
    }
  ],
  "errors": null,
  "traceId": "00-abc123-def456-00"
}
```

**Status Codes:**
- `200` - OK
- `500` - Internal Server Error

---

## 3. Get Dataset Profile

### `GET /datasets/{datasetId}/profile`

Retorna o profile completo do dataset com análise de colunas.

**Request:**
```http
GET /api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/profile
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "fileName": "vendas.csv",
    "rowCount": 10000,
    "columnCount": 15,
    "columns": [
      {
        "name": "data_venda",
        "dataType": "Date",
        "role": "Time",
        "nullCount": 0,
        "distinctCount": 365,
        "min": "2023-01-01",
        "max": "2023-12-31",
        "sampleValues": ["2023-01-01", "2023-01-02", "2023-01-03"]
      },
      {
        "name": "valor_total",
        "dataType": "Numeric",
        "role": "Measure",
        "nullCount": 5,
        "distinctCount": 8543,
        "min": 10.50,
        "max": 15000.00,
        "mean": 523.45,
        "sampleValues": [150.00, 320.50, 890.00]
      },
      {
        "name": "categoria",
        "dataType": "Text",
        "role": "Dimension",
        "nullCount": 0,
        "distinctCount": 8,
        "topValues": [
          { "value": "Eletrônicos", "count": 3200 },
          { "value": "Livros", "count": 2800 }
        ],
        "sampleValues": ["Eletrônicos", "Livros", "Moda"]
      }
    ],
    "profiledAt": "2026-02-14T10:31:00Z"
  },
  "errors": null,
  "traceId": "00-xyz789-abc123-00"
}
```

**Status Codes:**
- `200` - OK
- `404` - Dataset not found
- `500` - Internal Server Error

---

## 4. Get Chart Recommendations

### `GET /datasets/{datasetId}/recommendations`

Retorna lista de recomendações de gráficos geradas pelo motor de IA.

**Request:**
```http
GET /api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/recommendations
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": [
    {
      "id": "rec_001",
      "title": "Vendas ao longo do tempo",
      "reason": "Detectada série temporal com coluna 'data_venda' e medida 'valor_total'",
      "chart": {
        "type": "Line",
        "library": "ECharts"
      },
      "query": {
        "x": {
          "column": "data_venda",
          "role": "Time",
          "bin": "Day"
        },
        "y": {
          "column": "valor_total",
          "role": "Measure",
          "aggregation": "Sum"
        }
      }
    },
    {
      "id": "rec_002",
      "title": "Média de vendas por mês",
      "reason": "Agregação mensal recomendada para reduzir granularidade",
      "chart": {
        "type": "Line",
        "library": "ECharts"
      },
      "query": {
        "x": {
          "column": "data_venda",
          "role": "Time",
          "bin": "Month"
        },
        "y": {
          "column": "valor_total",
          "role": "Measure",
          "aggregation": "Avg"
        }
      }
    }
  ],
  "errors": null,
  "traceId": "00-rec123-xyz456-00"
}
```

**Status Codes:**
- `200` - OK
- `404` - Dataset not found
- `500` - Internal Server Error

---

## 5. Get Chart with Data (Execute Recommendation)

### `GET /datasets/{datasetId}/charts/{recommendationId}`

**Endpoint mais importante**: Executa a recomendação e retorna EChartsOption completo com dados reais.

**Request:**
```http
GET /api/v1/datasets/3fa85f64-5717-4562-b3fc-2c963f66afa6/charts/rec_001
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recommendationId": "rec_001",
    "option": {
      "title": {
        "text": "Vendas ao longo do tempo",
        "subtext": "Detectada série temporal com coluna 'data_venda' e medida 'valor_total'"
      },
      "tooltip": {
        "trigger": "axis",
        "axisPointer": {
          "type": "cross"
        }
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
        "name": "data_venda"
      },
      "yAxis": {
        "type": "value",
        "name": "valor_total"
      },
      "series": [
        {
          "name": "Sum(valor_total)",
          "type": "line",
          "smooth": true,
          "data": [
            [1704067200000, 15420.50],
            [1704153600000, 18930.00],
            [1704240000000, null],
            [1704326400000, 21045.75]
          ]
        }
      ],
      "dataZoom": [
        {
          "type": "slider",
          "show": true,
          "xAxisIndex": 0,
          "start": 0,
          "end": 100
        },
        {
          "type": "inside",
          "xAxisIndex": 0,
          "start": 0,
          "end": 100
        }
      ]
    },
    "meta": {
      "executionMs": 125,
      "duckDbMs": 87,
      "queryHash": "a3f5d8c2e1b4567890abcdef1234567890abcdef1234567890abcdef12345678",
      "datasetVersion": null,
      "rowCountReturned": 365,
      "chartType": "line",
      "generatedAt": "2026-02-14T10:35:22Z",
      "debugSql": "SELECT date_trunc('day', ...) AS x, SUM(...) AS y FROM ..."
    }
  },
  "errors": null,
  "traceId": "00-chart789-exec456-00"
}
```

**Response 404 Not Found:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "general": ["Recommendation 'rec_999' not found. Available recommendations: rec_001, rec_002, rec_003"]
  },
  "traceId": "00-notfound-123-00"
}
```

**Response 400 Bad Request:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "recommendationId": ["Recommendation ID must match pattern rec_XXX"]
  },
  "traceId": "00-validation-456-00"
}
```

**Status Codes:**
- `200` - OK
- `400` - Bad Request (validação)
- `404` - Dataset ou recomendação não encontrado
- `500` - Internal Server Error

---

## Frontend Integration Guide

### 1. Upload Flow
```javascript
// Upload CSV
const formData = new FormData();
formData.append('file', file);

const uploadResponse = await fetch('/api/v1/datasets', {
  method: 'POST',
  body: formData
});

const { data } = await uploadResponse.json();
const datasetId = data.datasetId;
```

### 2. Get Recommendations
```javascript
const recsResponse = await fetch(`/api/v1/datasets/${datasetId}/recommendations`);
const { data: recommendations } = await recsResponse.json();
```

### 3. Execute Chart
```javascript
const chartResponse = await fetch(`/api/v1/datasets/${datasetId}/charts/${recommendations[0].id}`);
const { data: chartData } = await chartResponse.json();

// Usar com ECharts
const myChart = echarts.init(document.getElementById('chart'));
myChart.setOption(chartData.option);
```

### 4. Error Handling
```javascript
const response = await fetch(url);
const json = await response.json();

if (!json.success) {
  // json.errors contém mapa de erros
  console.error('Errors:', json.errors);
  console.error('TraceId:', json.traceId); // Para suporte
}
```

---

## Notes

### Gap Filling
Por padrão, lacunas em séries temporais são preenchidas com `null`. Pode ser configurado em `appsettings.json`:
- `None` - Sem preenchimento
- `Nulls` - Preenche com null (default)
- `ForwardFill` - Último valor conhecido
- `Zeros` - Preenche com 0

### DataZoom Automático
Habilitado automaticamente quando `rowCountReturned > 200` pontos.

### Debug SQL
Campo `debugSql` em `meta` só aparece em ambiente Development.

### Performance
- `executionMs`: Tempo total do handler (profile + recommendations + query + mapping)
- `duckDbMs`: Tempo apenas da execução DuckDB
- `queryHash`: Use para cache/deduplicação no frontend

---

## Changelog

### v1.0 - 2026-02-14
- Initial MVP release
- Support for Line charts only
- ECharts library only
- Time series with Day/Month/Year bins
- Aggregations: Sum, Avg, Count, Min, Max

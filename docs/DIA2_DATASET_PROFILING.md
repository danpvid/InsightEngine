# Dia 2 - Dataset Profiling (Motor do InsightEngine)

## Objetivo

Implementar o primeiro "motor" do InsightEngine: um sistema que lê CSVs, infere o schema automaticamente e retorna um "data profile" que usuários de negócio conseguem entender.

## Endpoints Implementados

### 1. Upload de Dataset
**POST** `/api/v1/datasets`

Recebe um arquivo CSV, salva com identificador único e retorna metadata.

#### Request
```http
POST /api/v1/datasets HTTP/1.1
Content-Type: multipart/form-data
Authorization: Bearer {token}

file: vendas.csv
```

#### Response (201 Created)
```json
{
  "success": true,
  "message": "Arquivo enviado com sucesso.",
  "data": {
    "datasetId": "a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f",
    "originalFileName": "vendas.csv",
    "storedFileName": "a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f.csv",
    "sizeBytes": 245678,
    "createdAtUtc": "2026-02-14T03:00:00Z"
  }
}
```

#### Validações
- ❌ `file == null` → 400 Bad Request
- ❌ `file.Length == 0` → 400 Bad Request
- ❌ `extensão != .csv` → 400 Bad Request
- ❌ `file.Length > 20MB` → 413 Payload Too Large (configurável)

#### Segurança
- ✅ Arquivo salvo como `{datasetId}.csv` para evitar colisões
- ✅ Previne path traversal attacks
- ✅ Streaming para arquivos grandes (não carrega na memória)

---

### 2. Profile do Dataset
**GET** `/api/v1/datasets/{datasetId}/profile`

Analisa o CSV e retorna schema inferido com estatísticas.

#### Request
```http
GET /api/v1/datasets/a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f/profile HTTP/1.1
Authorization: Bearer {token}
```

#### Response (200 OK)
```json
{
  "success": true,
  "data": {
    "datasetId": "a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f",
    "rowCount": 12450,
    "sampleSize": 5000,
    "columns": [
      {
        "name": "sale_date",
        "inferredType": "Date",
        "nullRate": 0.0,
        "distinctCount": 365,
        "topValues": ["2025-01-01", "2025-01-02", "2025-01-03"]
      },
      {
        "name": "amount",
        "inferredType": "Number",
        "nullRate": 0.01,
        "distinctCount": 4200,
        "topValues": ["19.9", "29.9", "9.9"]
      },
      {
        "name": "status",
        "inferredType": "Category",
        "nullRate": 0.0,
        "distinctCount": 3,
        "topValues": ["completed", "pending", "cancelled"]
      },
      {
        "name": "is_paid",
        "inferredType": "Boolean",
        "nullRate": 0.0,
        "distinctCount": 2,
        "topValues": ["true", "false"]
      },
      {
        "name": "description",
        "inferredType": "String",
        "nullRate": 0.15,
        "distinctCount": 8900,
        "topValues": ["Product A", "Product B", "Product C"]
      }
    ]
  }
}
```

---

## Tipos Inferidos (Heurística Simples e Eficaz)

### 1. Boolean
**Condição:** 90%+ dos valores parseia como boolean

**Valores aceitos:**
- `true`, `false`
- `yes`, `no`
- `1`, `0`
- `t`, `f`
- `y`, `n`
- `sim`, `não`, `nao`

**Exemplo:**
```csv
is_active
true
false
true
1
0
yes
```
→ **Boolean** ✅

---

### 2. Date
**Condição:** 90%+ dos valores parseia como DateTime

**Formatos suportados:**
- ISO: `yyyy-MM-dd`
- BR: `dd/MM/yyyy`
- US: `MM/dd/yyyy`
- Compacto: `yyyyMMdd`
- Com separadores: `yyyy/MM/dd`, `dd-MM-yyyy`, `MM-dd-yyyy`

**Exemplo:**
```csv
sale_date
2025-01-01
2025-01-02
2025-01-03
```
→ **Date** ✅

---

### 3. Number
**Condição:** 90%+ dos valores parseia como decimal

**Formatos aceitos:**
- Inteiros: `123`, `-456`
- Decimais: `19.99`, `-45.67`
- Com separador de milhar: `1,234.56` (removido automaticamente)
- Notação científica: `1.23e5`

**Exemplo:**
```csv
amount
19.90
29.90
1,234.56
-45.67
```
→ **Number** ✅

---

### 4. Category
**Condição:** Baixa cardinalidade (poucos valores distintos)

**Regra:** `distinctCount <= max(20, rowCount * 0.05)`

Ou seja:
- Se dataset tem < 400 linhas: limite = 20 valores distintos
- Se dataset tem ≥ 400 linhas: limite = 5% do total

**Exemplo:**
```csv
status
completed
pending
cancelled
completed
completed
pending
```
→ **Category** ✅ (apenas 3 valores distintos)

---

### 5. String
**Condição:** Default quando nenhum outro tipo se aplica

**Casos:**
- Alta cardinalidade (muitos valores distintos)
- Texto livre
- Valores mistos que não atingem 90% de threshold

**Exemplo:**
```csv
description
Produto de alta qualidade para uso profissional
Item importado com certificação internacional
Mercadoria nacional com garantia estendida
```
→ **String** ✅

---

## Performance e Otimizações

### Amostragem Inteligente
- **Sample size:** 5.000 linhas (configurável em `appsettings.json`)
- **Inferência de tipo:** baseada na amostra (rápido)
- **Contagem total:** varre arquivo completo sem carregar em memória
- **Trade-off:** Inferência rápida vs. precisão absoluta

### Limites de Memória
- **Distinct tracking:** máximo 10.000 valores distintos por coluna
- **Top values:** máximo 1.000 valores únicos rastreados
- **Quando o limite é atingido:** para de rastrear e retorna estimativa

### Streaming
- **Upload:** não carrega arquivo inteiro na memória
- **Profile:** processa linha por linha com buffer de 80KB
- **Parser:** CsvHelper para robustez (aspas, vírgulas internas, etc.)

---

## Configurações

### appsettings.json
```json
{
  "UploadSettings": {
    "BasePath": "uploads",
    "MaxFileSizeBytes": 20971520,
    "ProfileSampleSize": 5000
  }
}
```

### Limites Configuráveis
| Parâmetro | Valor Padrão | Descrição |
|-----------|--------------|-----------|
| `MaxFileSizeBytes` | 20 MB | Tamanho máximo de upload |
| `ProfileSampleSize` | 5.000 linhas | Linhas para inferência |
| `MaxDistinctTracking` | 10.000 | Limite de valores distintos |
| `TopValuesCount` | 3 | Top N valores mais frequentes |
| `TypeInferenceThreshold` | 90% | Mínimo para inferir tipo |

---

## Arquitetura Implementada

### Domain Layer
```
Domain/
├── Enums/
│   └── InferredType.cs (Number, Date, Boolean, String, Category)
├── ValueObjects/
│   ├── ColumnProfile.cs (Name, InferredType, NullRate, DistinctCount, TopValues)
│   └── DatasetProfile.cs (DatasetId, RowCount, SampleSize, Columns)
├── Interfaces/
│   └── ICsvProfiler.cs
└── Settings/
    └── UploadSettings.cs
```

### Infrastructure Layer
```
Infra.Data/
└── Services/
    └── CsvProfiler.cs (heurísticas de inferência com CsvHelper)
```

### API Layer
```
API/
└── Controllers/V1/
    └── DataSetController.cs (POST /, GET /{id}/profile)
```

---

## Fluxo de Uso Completo

### 1. Upload
```bash
curl -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer {token}" \
  -F "file=@vendas.csv"
```

**Resposta:**
```json
{
  "success": true,
  "data": {
    "datasetId": "a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f",
    ...
  }
}
```

### 2. Profile
```bash
curl -X GET "https://localhost:5000/api/v1/datasets/a3f7b2c1-5d4e-4a9b-8c3f-1e2d3c4b5a6f/profile" \
  -H "Authorization: Bearer {token}"
```

**Resposta:**
```json
{
  "success": true,
  "data": {
    "rowCount": 12450,
    "sampleSize": 5000,
    "columns": [...]
  }
}
```

---

## Testing no Swagger

### Passo 1: Autenticar
1. Abra `https://localhost:5000/swagger`
2. POST `/api/v1/auth/login` com credenciais
3. Copie o token JWT
4. Clique no botão 🔒 "Authorize" no topo
5. Cole: `Bearer {token}`

### Passo 2: Upload
1. POST `/api/v1/datasets`
2. Clique em "Try it out"
3. Selecione um CSV de teste
4. Execute
5. **Copie o `datasetId` da resposta**

### Passo 3: Profile
1. GET `/api/v1/datasets/{datasetId}/profile`
2. Clique em "Try it out"
3. Cole o `datasetId` copiado
4. Execute
5. **Confira os tipos inferidos** ✅

---

## Checklist de Validação (Definition of Done)

✅ **Upload funciona:**
- POST `/api/v1/datasets` retorna 201 Created
- Arquivo salvo como `{datasetId}.csv`
- Metadata completo no response

✅ **Profile funciona:**
- GET `/api/v1/datasets/{datasetId}/profile` retorna 200 OK
- Tipos inferidos corretamente:
  - Datas → `Date`
  - Números → `Number`
  - Booleanos → `Boolean`
  - Baixa cardinalidade → `Category`
  - Alta cardinalidade → `String`

✅ **Estatísticas corretas:**
- `nullRate` faz sentido
- `distinctCount` coerente
- `topValues` mostra os 3 mais frequentes
- `sampleSize` e `rowCount` presentes

✅ **Performance:**
- Upload de 20MB funciona sem timeout
- Profile de 10k+ linhas retorna em < 5 segundos
- Memória não estoura com arquivos grandes

✅ **Validações:**
- CSV-only enforcement (400 para outros formatos)
- Limite de 20MB respeitado (413 quando excede)
- Arquivo não encontrado retorna 404

---

## Próximos Passos (Dia 3+)

1. **Transformações:** Filtros, agregações, joins
2. **Visualizações:** Gráficos automáticos baseados no tipo
3. **ML:** Detecção de anomalias, correlações
4. **Export:** Salvar profile como JSON/PDF
5. **Suporte XLSX:** Ler Excel além de CSV

---

## Tecnologias Utilizadas

- **CsvHelper 33.1.0:** Parser robusto de CSV
- **CsvProfiler:** Heurísticas de inferência de tipo
- **Streaming:** FileStream com buffer de 80KB
- **CQRS:** Separação de comandos (upload) e queries (profile)
- **Clean Architecture:** Domain → Infra → API

---

## Observações do MVP

### Decisões de Simplicidade
1. **CSV only:** XLSX virá depois
2. **Sample size fixo:** 5.000 linhas (configurável, mas fixo no MVP)
3. **RowCount = amostra + contagem sem parse:** Não faz parse completo agora
4. **Heurísticas simples:** 90% threshold é "good enough"
5. **Sem cache:** Profile calculado on-demand (pode cachear depois)

### Trade-offs Conscientes
| Decisão | Trade-off | Justificativa |
|---------|-----------|---------------|
| Amostragem de 5k linhas | Precisão vs. Velocidade | MVP precisa ser rápido |
| 90% threshold | Rigor vs. Pragmatismo | Cobre 90%+ dos casos reais |
| Distinct limit 10k | Memória vs. Completude | Previne OOM em colunas com alta cardinalidade |
| Top 3 values | Informação vs. Simplicidade | Suficiente para entender distribuição |

---

**Status:** ✅ Dia 2 Completo - Motor de Profiling Funcionando!

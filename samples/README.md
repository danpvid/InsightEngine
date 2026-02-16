# 📊 InsightEngine Sample CSV Files

Este diretório contém CSVs gerados automaticamente pelo **InsightEngine.DataGenerator** para testes completos da API.

## 📁 Arquivos Disponíveis

| Arquivo | Linhas | Colunas | Descrição | Melhor para |
|---------|--------|---------|-----------|-------------|
| `vendas_ecommerce.csv` | 5.000 | 19 | Vendas E-commerce (PT-BR) | Line, Bar, Scatter |
| `controladoria_contabilidade.csv` | 5.000 | 17 | Lançamentos Contábeis | Line, Bar, Histogram |
| `recursos_humanos.csv` | 5.000 | 19 | Dados de Funcionários | Histogram, Bar |
| `logistica_entregas.csv` | 5.000 | 18 | Rastreamento de Entregas | Scatter, Line |
| `marketing_digital.csv` | 5.000 | 18 | Campanhas de Marketing | Line, Bar, Scatter |
| `producao_manufatura.csv` | 5.000 | 17 | Ordens de Produção | Bar, Line, Histogram |
| `inventario_produtos.csv` | 5.000 | 17 | Controle de Estoque | Bar, Histogram |
| `dados_clientes.csv` | 5.000 | 19 | Cadastro de Clientes | Histogram, Bar |
| `fornecedores_compras.csv` | 5.000 | 18 | Dados de Fornecedores | Bar, Scatter |
| `financas_fluxo_caixa.csv` | 5.000 | 17 | Movimentações Financeiras | Line, Histogram |
| `ecommerce_sales.csv` | 5.000 | 12 | Transações de e-commerce (EN) | Line, Bar, Scatter |
| `employee_records.csv` | 8.000 | 12 | Registros de RH (EN) | Histogram, Bar |
| `financial_transactions.csv` | 10.000 | 12 | Transações bancárias (EN) | Line, Histogram |
| `healthcare_patients.csv` | 6.000 | 12 | Registros médicos (EN) | Histogram, Bar |
| `logistics_shipments.csv` | 7.500 | 13 | Operações de logística (EN) | Scatter, Line |

**Total:** 71.500 linhas, ~200 colunas

### � Novos Arquivos Gerados (2026)

Os arquivos marcados com **(PT-BR)** foram gerados recentemente com dados em português brasileiro, contendo distribuições mais realistas e maior diversidade de colunas. Estes arquivos simulam cenários empresariais completos com:

- **Distribuições não-homogêneas** (dados reais têm variações)
- **Relações causais** entre colunas
- **Dados faltantes** em proporções realistas
- **Valores extremos** (outliers) controlados
- **Dependências temporais** (datas sequenciais)

#### 📈 Características Especiais dos Novos Datasets

**Vendas E-commerce:**
- Distribuição sazonal de vendas
- Correlação entre desconto e volume
- Variação de frete por região
- Taxas realistas de cancelamento/devolução

**Controladoria:**
- Lançamentos contábeis balanceados (débito/crédito)
- Moedas estrangeiras com taxas de câmbio
- Centros de custo com pesos realistas
- Competências fiscais corretas

**Recursos Humanos:**
- Distribuição etária gaussiana
- Salários log-normais (com caudas longas)
- Dependentes correlacionados com idade
- Taxas de turnover realistas

**Logística:**
- Tempos de entrega com atrasos controlados
- Correlação peso x volume
- Performance variável por transportadora
- Tentativas de entrega realistas

**Marketing Digital:**
- ROI calculado realisticamente
- CTR decrescente com tempo
- Conversões em funil de vendas
- Segmentação por idade/gênero

**Produção:**
- Eficiências com variações controladas
- Defeitos correlacionados com operadores
- Tempos de produção realistas
- Custos materiais vs mão de obra

**Inventário:**
- Saldos com movimentações realistas
- Vencimentos distribuídos
- Categorias com pesos de mercado
- Responsáveis por setor

**Clientes:**
- RFV (Recência, Frequência, Valor) calculado
- Scores de crédito gaussianos
- Canais de aquisição com pesos
- Inativos com padrões realistas

**Fornecedores:**
- Avaliações com distribuição normal
- Prazos de pagamento negociais
- Descontos por volume
- Categorias B2B realistas

**Fluxo de Caixa:**
- Saldo acumulado consistente
- Entradas vs saídas balanceadas
- Moedas com volatilidade
- Previsões vs realizados

**E-commerce Sales** - Melhor para:
- Line Chart: `order_date` x `total_amount` (tendências de vendas)
- Bar Chart: `category` x `COUNT(*)` (produtos mais vendidos)
- Scatter: `discount_percentage` x `total_amount` (impacto de descontos)

**Employee Records** - Melhor para:
- Histogram: `salary` (distribuição salarial)
- Bar Chart: `department` x `COUNT(*)` (tamanho dos departamentos)
- Histogram: `years_of_service` (tempo de empresa)

**Financial Transactions** - Melhor para:
- Line Chart: `transaction_date` x `amount` (fluxo de caixa)
- Histogram: `amount` (distribuição de valores)
- Bar Chart: `transaction_type` x `SUM(amount)` (tipos de transação)

**Healthcare Patients** - Melhor para:
- Histogram: `age` (faixa etária de pacientes)
- Bar Chart: `diagnosis` x `COUNT(*)` (doenças mais comuns)
- Histogram: `treatment_cost` (custos de tratamento)

**Logistics Shipments** - Melhor para:
- Scatter: `weight_kg` x `delivery_days` (relação peso/tempo)
- Line Chart: `ship_date` x `COUNT(*)` (volume de envios)
- Bar Chart: `carrier` x `AVG(delivery_days)` (performance de transportadoras)

## 🚀 Como Usar

### 1️⃣ Teste via Swagger (Recomendado)

1. Inicie a API: `dotnet run --project src/InsightEngine.API`
2. Abra: `https://localhost:5000/swagger`
3. **Autentique-se** (POST `/api/v1/auth/login`)
   ```json
   {
     "username": "admin",
     "password": "admin123"
   }
   ```
4. **Copie o Bearer token** da resposta
5. **Clique no cadeado** 🔒 no topo do Swagger e cole o token
6. **Faça upload** (POST `/api/v1/datasets`)
7. **Veja o profile** (GET `/api/v1/datasets/{id}/profile`)
8. **Gere gráficos** (POST `/api/v1/charts/{type}`)

### 2️⃣ Teste via cURL

```bash
# 1. Login
TOKEN=$(curl -s -X POST "https://localhost:5000/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' | jq -r '.data.token')

# 2. Upload
DATASET_ID=$(curl -s -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/ecommerce_sales.csv" | jq -r '.data.id')

# 3. Profile
curl -X GET "https://localhost:5000/api/v1/datasets/$DATASET_ID/profile" \
  -H "Authorization: Bearer $TOKEN" | jq

# 4. Line Chart
curl -X POST "https://localhost:5000/api/v1/charts/line" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"datasetId\": \"$DATASET_ID\",
    \"xColumn\": \"order_date\",
    \"yColumn\": \"total_amount\"
  }" | jq
```

### 3️⃣ Teste via HTTP Files (VS Code REST Client)

Veja `test-upload.http` na raiz do projeto para exemplos completos.

### 4️⃣ Teste via Bash Script

```bash
./test-api.sh
```

### 🎯 Recomendações de Uso por Dataset (Novos Arquivos PT-BR)

**Vendas E-commerce** - Melhor para:
- Line Chart: `Data_Pedido` x `Total` (tendências de vendas)
- Bar Chart: `Categoria_Produto` x `COUNT(*)` (produtos mais vendidos)
- Scatter: `Desconto` x `Total` (impacto de descontos)
- Histogram: `Preco_Unitario` (distribuição de preços)

**Controladoria** - Melhor para:
- Line Chart: `Data_Lancamento` x `Valor` (fluxo contábil)
- Bar Chart: `Tipo_Lancamento` x `SUM(Valor)` (receitas vs despesas)
- Histogram: `Valor` (distribuição de lançamentos)
- Bar Chart: `Centro_Custo` x `COUNT(*)` (atividade por centro)

**Recursos Humanos** - Melhor para:
- Histogram: `Salario` (distribuição salarial)
- Bar Chart: `Departamento` x `COUNT(*)` (tamanho dos departamentos)
- Histogram: `Idade` (pirâmide etária)
- Scatter: `Idade` x `Salario` (correlação experiência/remuneração)

**Logística** - Melhor para:
- Scatter: `Peso_Kg` x `Data_Entrega` (relação peso/tempo)
- Line Chart: `Data_Saida` x `COUNT(*)` (volume de envios)
- Bar Chart: `Transportadora` x `AVG(Valor_Frete)` (custos por transportadora)
- Histogram: `Valor_Frete` (distribuição de custos)

**Marketing Digital** - Melhor para:
- Line Chart: `Data_Inicio` x `Investimento` (orçamento ao longo do tempo)
- Scatter: `Impressoes` x `Cliques` (efetividade de campanhas)
- Bar Chart: `Canal` x `ROI` (performance por canal)
- Histogram: `CPA` (distribuição de custos de aquisição)

**Produção** - Melhor para:
- Line Chart: `Data_Inicio` x `Quantidade_Produzida` (produtividade temporal)
- Bar Chart: `Maquina` x `Eficiencia` (performance de equipamentos)
- Histogram: `Custo_Materia_Prima` (distribuição de custos)
- Scatter: `Tempo_Producao_Min` x `Defeitos` (qualidade vs velocidade)

**Inventário** - Melhor para:
- Bar Chart: `Categoria` x `SUM(Quantidade_Estoque)` (estoque por categoria)
- Histogram: `Valor_Unitario` (preços de produtos)
- Line Chart: `Data_Ultima_Movimentacao` x `Quantidade_Movimentada` (atividade de estoque)
- Scatter: `Quantidade_Estoque` x `Valor_Total` (valorização de estoque)

**Clientes** - Melhor para:
- Histogram: `Idade` (faixa etária de clientes)
- Bar Chart: `Genero` x `COUNT(*)` (distribuição por gênero)
- Scatter: `Numero_Pedidos` x `Valor_Total_Compras` (RFV analysis)
- Bar Chart: `Canal_Aquisicao` x `COUNT(*)` (efetividade de canais)

**Fornecedores** - Melhor para:
- Bar Chart: `Categoria` x `AVG(Avaliacao)` (performance por categoria)
- Histogram: `Valor_Total_Compras` (distribuição de compras)
- Scatter: `Prazo_Pagamento` x `Desconto_Medio` (negociação vs prazo)
- Line Chart: `Data_Cadastro` x `Valor_Total_Compras` (crescimento de fornecedores)

**Fluxo de Caixa** - Melhor para:
- Line Chart: `Data` x `Saldo_Apos` (evolução do saldo)
- Bar Chart: `Tipo` x `SUM(Valor)` (entradas vs saídas)
- Histogram: `Valor` (distribuição de movimentações)
- Line Chart: `Data` x `Valor` (fluxo diário)

## 📊 Exemplos de Charts por Tipo

### Line Chart (Tendências Temporais)

```bash
POST /api/v1/charts/line
{
  "datasetId": "{id}",
  "xColumn": "order_date",      # Coluna temporal (Date)
  "yColumn": "total_amount"      # Coluna numérica (Number)
}
```

**Bons exemplos:**
- `ecommerce_sales.csv`: `order_date` x `total_amount`
- `financial_transactions.csv`: `transaction_date` x `amount`
- `logistics_shipments.csv`: `ship_date` x `weight_kg`

### Bar Chart (Comparações Categóricas)

```bash
POST /api/v1/charts/bar
{
  "datasetId": "{id}",
  "xColumn": "category",         # Coluna categórica
  "yColumn": "total_amount",     # Coluna numérica
  "aggregation": "sum"           # sum, avg, count, min, max
}
```

**Bons exemplos:**
- `ecommerce_sales.csv`: `category` x `COUNT(*)` (produtos mais vendidos)
- `employee_records.csv`: `department` x `AVG(salary)` (salário médio)
- `healthcare_patients.csv`: `diagnosis` x `COUNT(*)` (doenças comuns)

### Scatter Chart (Correlações)

```bash
POST /api/v1/charts/scatter
{
  "datasetId": "{id}",
  "xColumn": "discount_percentage",  # Number
  "yColumn": "total_amount"          # Number
}
```

**⚠️ Limite:** 2.000 pontos (amostragem aleatória aplicada automaticamente)

**Bons exemplos:**
- `ecommerce_sales.csv`: `discount_percentage` x `total_amount`
- `logistics_shipments.csv`: `weight_kg` x `delivery_days`
- `financial_transactions.csv`: `amount` x `fee`

### Histogram (Distribuições)

```bash
POST /api/v1/charts/histogram
{
  "datasetId": "{id}",
  "column": "salary"             # Coluna numérica
}
```

**⚠️ Limites:**
- Min bins: 5
- Max bins: 50
- Default: 20

**Bons exemplos:**
- `employee_records.csv`: `salary` (distribuição salarial)
- `healthcare_patients.csv`: `age` (faixa etária)
- `financial_transactions.csv`: `amount` (valores de transações)

## 🧪 Testando Limites de Segurança

### Upload Limit (20MB)

```bash
# ✅ Deve passar (arquivos samples são < 1MB)
curl -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/financial_transactions.csv"

# ❌ Deve falhar com 400 Bad Request
dd if=/dev/zero of=large.csv bs=1M count=25
curl -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@large.csv"
```

### Scatter Limit (2.000 pontos)

```bash
# Dataset com 10.000 linhas → API retorna amostra de 2.000
POST /api/v1/charts/scatter
{
  "datasetId": "{financial_transactions_id}",
  "xColumn": "amount",
  "yColumn": "fee"
}

# Resposta terá exatamente 2.000 pontos (amostragem aleatória)
```

### Histogram Bins (5-50)

```bash
# ✅ Bins = 10 (válido)
POST /api/v1/charts/histogram
{
  "datasetId": "{id}",
  "column": "salary",
  "bins": 10
}

# ⚠️ Bins = 3 → Clamped para 5
POST /api/v1/charts/histogram
{
  "datasetId": "{id}",
  "column": "salary",
  "bins": 3
}

# ⚠️ Bins = 100 → Clamped para 50
POST /api/v1/charts/histogram
{
  "datasetId": "{id}",
  "column": "salary",
  "bins": 100
}
```

## 🔄 Regenerar Samples

Para gerar novos dados aleatórios:

```bash
cd tools/InsightEngine.DataGenerator
dotnet run
```

Os arquivos serão **sobrescritos** com novos dados.

**Configuração:**
- Linhas por dataset: 5k-10k
- Seed aleatória: cada execução gera dados diferentes
- Tipos inferidos: todos os 5 tipos (Number, Date, Boolean, Category, String)

## ✅ Validação de Tipos

## ✅ Validação de Tipos

Cada dataset foi projetado para cobrir todos os 5 tipos inferidos pelo Profiler:

| Tipo | Descrição | Exemplos de Colunas |
|------|-----------|---------------------|
| 🔢 **Number** | Valores numéricos (int, decimal, negativo) | `salary`, `amount`, `age`, `weight_kg` |
| 📅 **Date** | Datas em múltiplos formatos | `order_date`, `hire_date`, `ship_date` |
| ✅ **Boolean** | Variações de verdadeiro/falso | `is_premium`, `is_active`, `is_express` |
| 🏷️ **Category** | Baixa cardinalidade (< 5% distinct) | `category`, `department`, `status`, `carrier` |
| 📝 **String** | Alta cardinalidade (texto livre) | `customer_id`, `notes`, `description` |

### 🎯 Cobertura de Edge Cases

- **Números negativos:** `financial_transactions.csv` (amount pode ser negativo)
- **Decimais:** Todos os datasets (valores monetários com 2 casas)
- **Datas ISO 8601:** `order_date`, `transaction_date` (formato: YYYY-MM-DD)
- **Nulls variados:** 0%, 5%, 15%, 30% de null rate
- **Boolean variants:** `true/false`, `yes/no`, `1/0`, `sim/não`
- **Alta cardinalidade:** IDs únicos (1 por linha)
- **Baixa cardinalidade:** Status, departments (5-10 valores)

## 📈 Testando Profile com Min/Max

Desde a Task 6.6, o Profile retorna min/max para colunas numéricas:

```bash
GET /api/v1/datasets/{id}/profile

# Resposta inclui:
{
  "data": {
    "columns": [
      {
        "name": "salary",
        "inferredType": "Number",
        "min": 35000.00,      # ✨ Novo campo
        "max": 150000.00,     # ✨ Novo campo
        "nullRate": 0.0,
        "distinctCount": 4285
      }
    ]
  }
}
```

**Use min/max para:**
- Calcular bins otimizados: `(max - min) / bins`
- Definir escalas de eixos de gráficos
- Validar outliers antes de plotar
- Evitar queries extras ao DuckDB

## 🗂️ Null Rates

Colunas possuem diferentes taxas de nulos para testes realistas:

| Null Rate | Tipo de Campo | Exemplos |
|-----------|---------------|----------|
| **0%** | Obrigatórios (PKs, dates) | `employee_id`, `order_date`, `patient_id` |
| **1-15%** | Opcionais comuns | `bonus`, `discount_percentage`, `middle_name` |
| **20-60%** | Raramente preenchidos | `notes`, `special_instructions`, `discharge_date` |

## 🛠️ Troubleshooting

### "Dataset not found"
- Verifique se o `datasetId` está correto
- Confirme que o token JWT é válido
- Use GET `/api/v1/datasets` para listar todos os datasets

### "Column not found in dataset"
- Nomes de colunas são **case-sensitive**
- Use GET `/api/v1/datasets/{id}/profile` para ver colunas disponíveis
- Colunas com espaços: use exatamente como aparecem no profile

### "Chart generation failed"
- **Line Chart:** xColumn deve ser Date, yColumn deve ser Number
- **Bar Chart:** xColumn deve ser Category/String
- **Scatter Chart:** ambas colunas devem ser Number
- **Histogram:** column deve ser Number

### "Unauthorized"
- Token JWT expirou (válido por 1 hora)
- Faça login novamente: POST `/api/v1/auth/login`
- Adicione `Authorization: Bearer {token}` no header

---

## 📚 Documentação Relacionada

- **API Endpoints:** Ver `docs/API.md` (Task 6.8)
- **Gerador de Dados:** Ver `tools/InsightEngine.DataGenerator/README.md`
- **Profiling System:** Ver `docs/DIA2_DATASET_PROFILING.md`
- **Architecture:** Ver `ARCHITECTURE.md`

---

**Nota:** Estes arquivos são gerados automaticamente e **não devem ser editados manualmente**. Para modificar a estrutura dos dados, edite os templates em `tools/InsightEngine.DataGenerator/Templates/`.

---

**Última atualização:** Dia 6 - Task 6.7 (Samples Enhancement)  
**Status:** ✅ Pronto para produção

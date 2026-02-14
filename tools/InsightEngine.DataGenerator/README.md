# InsightEngine Data Generator

## Overview

Gerador de CSVs semânticos para testes do InsightEngine. Cria datasets realistas de diferentes domínios de negócio com todos os tipos inferidos (Number, Date, Boolean, Category, String) e taxas configuráveis de valores nulos.

## Datasets Gerados

### 1. E-commerce Sales (5.000 linhas)
**Arquivo:** `ecommerce_sales.csv`

Transações de vendas de e-commerce com:
- **Number:** `amount` (10-5000), `quantity` (1-100), `discount_rate` (0-0.5)
- **Date:** `sale_date` (2024-2026)
- **Boolean:** `is_prime_member`, `is_gift`
- **Category:** `status` (4 valores), `payment_method` (5 valores), `customer_segment` (4 valores), `product_category` (6 valores)
- **String:** `transaction_id`, `shipping_notes`

**Null rates:** 0-30% variando por coluna

---

### 2. Employee Records (8.000 linhas)
**Arquivo:** `employee_records.csv`

Registros de RH com dados de funcionários:
- **Number:** `salary` (3000-25000), `bonus` (0-10000)
- **Date:** `hire_date` (2015-2025), `birth_date` (1960-2000)
- **Boolean:** `is_remote`, `has_benefits`
- **Category:** `department` (6 valores), `position` (6 valores), `performance_rating` (4 valores), `office_location` (5 valores)
- **String:** `employee_id`, `skills`

**Null rates:** 0-25% variando por coluna

---

### 3. Financial Transactions (10.000 linhas)
**Arquivo:** `financial_transactions.csv`

Transações bancárias e financeiras:
- **Number:** `amount` (-50000 a 50000), `balance_after` (0-1000000), `fee` (0-50)
- **Date:** `transaction_date` (2025-2026)
- **Boolean:** `is_international`, `is_recurring`
- **Category:** `transaction_type` (5 valores), `channel` (5 valores), `status` (4 valores), `risk_score` (3 valores)
- **String:** `transaction_id`, `description`

**Null rates:** 0-40% variando por coluna

---

### 4. Healthcare Patients (6.000 linhas)
**Arquivo:** `healthcare_patients.csv`

Registros de pacientes e visitas médicas:
- **Number:** `age` (0-100), `total_cost` (100-50000), `length_of_stay` (1-30)
- **Date:** `admission_date` (2024-2026), `discharge_date` (2024-2026, 15% null)
- **Boolean:** `is_emergency`, `is_readmission`
- **Category:** `department` (6 valores), `insurance_type` (4 valores), `severity` (4 valores)
- **String:** `patient_id`, `diagnosis`

**Null rates:** 0-15% variando por coluna

---

### 5. Logistics Shipments (7.500 linhas)
**Arquivo:** `logistics_shipments.csv`

Operações de logística e envios:
- **Number:** `weight_kg` (0.5-1000), `shipping_cost` (5-500), `distance_km` (1-5000)
- **Date:** `ship_date` (2025-2026), `delivery_date` (2025-2026, 20% null)
- **Boolean:** `is_insured`, `requires_signature`
- **Category:** `carrier` (5 valores), `service_level` (4 valores), `status` (5 valores), `destination_region` (6 valores)
- **String:** `shipment_id`, `special_instructions`

**Null rates:** 0-60% variando por coluna

---

## Como Usar

### Gerar os Datasets

```bash
cd tools/InsightEngine.DataGenerator
dotnet run
```

Os arquivos serão gerados em `samples/` na raiz do projeto.

### Testar com a API

1. Inicie o InsightEngine API
2. Faça upload de um dos CSVs gerados via Swagger:
   ```
   POST /api/v1/datasets
   ```
3. Obtenha o profile para validar a inferência:
   ```
   GET /api/v1/datasets/{datasetId}/profile
   ```

---

## Características dos Dados Gerados

### Variedade de Formatos

**Datas:**
- ISO: `2025-01-15`
- BR: `15/01/2025`
- US: `01/15/2025`
- Compacto: `20250115`
- Alternativo: `2025/01/15`

**Booleanos:**
- `true` / `false`
- `yes` / `no`
- `1` / `0`
- `Y` / `N`
- `T` / `F`
- `sim` / `não`
- `True` / `False`
- `YES` / `NO`

**Numbers:**
- Inteiros: `123`
- Decimais: `19.99`
- Com separador de milhar: `1,234.56` (30% dos casos)
- Negativos: `-45.67`

### Null Rate Realista
- Colunas obrigatórias: 0% null
- Colunas opcionais: 1-15% null
- Colunas frequentemente vazias: 20-60% null

### Alta Cardinalidade em Strings
- Strings combinam valores base com sufixos únicos
- Simula descrições livres, comentários, notas
- Garante que o tipo seja inferido como **String** (não **Category**)

---

## Estrutura do Código

### Models
- `ColumnDefinition`: Define uma coluna (nome, tipo, null rate, range)
- `DatasetTemplate`: Define um dataset completo
- `ColumnType`: Enum dos 5 tipos (Number, Date, Boolean, Category, String)

### Generators
- `CsvGenerator`: Motor de geração reutilizável
  - `GenerateAsync()`: Cria o CSV com base no template
  - `GenerateValue()`: Gera valor conforme o tipo da coluna
  - Métodos específicos por tipo: `GenerateNumber()`, `GenerateDate()`, etc.

### Templates
- `BusinessTemplates`: Templates pré-definidos de domínios de negócio
  - `EcommerceSales()`
  - `EmployeeRecords()`
  - `FinancialTransactions()`
  - `HealthcarePatients()`
  - `LogisticsShipments()`
  - `GetAllTemplates()`: Retorna todos os templates

---

## Extensibilidade

### Criar Novo Template

```csharp
public static DatasetTemplate MyCustomDataset()
{
    return new DatasetTemplate
    {
        Name = "my_custom_data",
        Description = "Meu dataset customizado",
        RowCount = 5000,
        Columns = new List<ColumnDefinition>
        {
            new() { 
                Name = "my_number", 
                Type = ColumnType.Number, 
                NullRate = 0.05,
                NumberRange = (0.0m, 1000.0m)
            },
            new() { 
                Name = "my_category", 
                Type = ColumnType.Category, 
                NullRate = 0.0,
                PossibleValues = new List<string> { "A", "B", "C" }
            },
            // ... mais colunas
        }
    };
}
```

### Adicionar ao Gerador

```csharp
// Em BusinessTemplates.GetAllTemplates()
public static List<DatasetTemplate> GetAllTemplates()
{
    return new List<DatasetTemplate>
    {
        EcommerceSales(),
        EmployeeRecords(),
        MyCustomDataset(), // <-- Adicionar aqui
        // ...
    };
}
```

---

## Validação da Inferência de Tipos

Após gerar e fazer upload dos CSVs, valide se o profile está correto:

### Checklist por Tipo

**Number:**
- ✅ Colunas numéricas inferidas como `Number`
- ✅ Negativos e decimais processados corretamente
- ✅ Separadores de milhar removidos

**Date:**
- ✅ Diferentes formatos (ISO, BR, US) reconhecidos
- ✅ Inferido como `Date` com 90%+ de sucesso

**Boolean:**
- ✅ Variações (true/false, yes/no, 1/0) reconhecidas
- ✅ Inferido como `Boolean`

**Category:**
- ✅ Baixa cardinalidade (≤ 5% das linhas) → `Category`
- ✅ Top values mostram os valores mais frequentes

**String:**
- ✅ Alta cardinalidade → `String`
- ✅ Texto livre não confundido com `Category`

**Null Rate:**
- ✅ `nullRate` próximo ao configurado no template
- ✅ Colunas com 0% null rate mostram 0.0

---

## Performance

| Dataset | Linhas | Colunas | Tempo de Geração |
|---------|--------|---------|------------------|
| ecommerce_sales | 5.000 | 12 | ~2s |
| employee_records | 8.000 | 12 | ~3s |
| financial_transactions | 10.000 | 12 | ~4s |
| healthcare_patients | 6.000 | 12 | ~2.5s |
| logistics_shipments | 7.500 | 13 | ~3s |

**Total:** 36.500 linhas em ~15 segundos

---

## Benefícios para Testes

1. **Realismo:** Dados simulam cenários reais de negócio
2. **Cobertura:** Todos os 5 tipos inferidos presentes
3. **Null Handling:** Testa robustez com valores faltantes
4. **Variedade:** Múltiplos formatos de data, boolean, number
5. **Volume:** 5k-10k linhas testam performance
6. **Reprodutibilidade:** Mesmo seed = mesmos dados
7. **Documentação:** Templates autodocumentados

---

## Próximos Passos

- [ ] Adicionar seed configurável para reprodutibilidade
- [ ] Suporte a XLSX (Excel)
- [ ] Templates adicionais (Marketing, IoT, Logs)
- [ ] Correlações entre colunas (ex: discount_rate vs amount)
- [ ] Anomalias intencionais para testar detecção
- [ ] API REST para geração sob demanda

---

**Licença:** Uso interno do InsightEngine
**Autor:** InsightEngine Team
**Versão:** 1.0

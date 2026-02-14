# InsightEngine Sample CSV Files

Este diretório contém CSVs gerados automaticamente pelo **InsightEngine.DataGenerator** para testes da API.

## Arquivos Disponíveis

| Arquivo | Linhas | Colunas | Descrição |
|---------|--------|---------|-----------|
| `ecommerce_sales.csv` | 5.000 | 12 | Transações de e-commerce |
| `employee_records.csv` | 8.000 | 12 | Registros de RH |
| `financial_transactions.csv` | 10.000 | 12 | Transações bancárias |
| `healthcare_patients.csv` | 6.000 | 12 | Registros médicos |
| `logistics_shipments.csv` | 7.500 | 13 | Operações de logística |

**Total:** 36.500 linhas, 61 colunas

## Como Usar

### 1. Teste via Swagger

1. Inicie a API: `dotnet run --project src/InsightEngine.API`
2. Abra: `https://localhost:5000/swagger`
3. Autentique-se (POST `/api/v1/auth/login`)
4. Faça upload de um CSV (POST `/api/v1/datasets`)
5. Veja o profile (GET `/api/v1/datasets/{id}/profile`)

### 2. Teste via cURL

```bash
# Upload
curl -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer {token}" \
  -F "file=@samples/ecommerce_sales.csv"

# Profile
curl -X GET "https://localhost:5000/api/v1/datasets/{datasetId}/profile" \
  -H "Authorization: Bearer {token}"
```

## Regenerar Samples

```bash
cd tools/InsightEngine.DataGenerator
dotnet run
```

Os arquivos serão sobrescritos com novos dados aleatórios.

## Validação de Tipos

Cada dataset foi projetado para cobrir todos os 5 tipos inferidos:

- ✅ **Number:** Valores numéricos, decimais, negativos
- ✅ **Date:** Múltiplos formatos (ISO, BR, US, compacto)
- ✅ **Boolean:** Variações (true/false, yes/no, 1/0, sim/não)
- ✅ **Category:** Baixa cardinalidade (status, department, etc.)
- ✅ **String:** Alta cardinalidade (IDs, descriptions, notes)

## Null Rates

Colunas possuem diferentes taxas de nulos para testes realistas:

- **0%:** Campos obrigatórios (IDs, dates principais)
- **1-15%:** Campos opcionais (bonus, discharge_date)
- **20-60%:** Campos frequentemente vazios (notes, special_instructions)

---

**Nota:** Estes arquivos são gerados automaticamente e não devem ser editados manualmente.

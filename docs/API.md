# 📚 InsightEngine API Documentation

> **Complete REST API documentation for intelligent chart generation**  
> Version: 1.0 | Date: 2026-02-14 | Task 6.8

## 📋 Table of Contents

- [Overview](#overview)
- [Authentication](#authentication)
- [Endpoints](#endpoints)
  - [Auth](#1-auth-endpoints)
  - [Datasets](#2-dataset-endpoints)
  - [Charts](#3-chart-endpoints)
- [Data Models](#data-models)
- [Error Handling](#error-handling)
- [Security and Limits](#security-and-limits)
- [Integration Examples](#integration-examples)

---

## 🌐 Overview

### Base URL

```
https://localhost:5000/api/v1
```

### Response Pattern (Task 6.1)

All responses follow the standardized envelope:

```json
{
  "success": true,
  "data": { ... },
  "errors": null,
  "traceId": "00-abc123def456-789-00"
}
```

**Fields:**
- `success` (boolean): Indicates if the request was successful
- `data` (object|null): Response payload (null on error)
- `errors` (object|null): Validation error map (null on success)
- `traceId` (string): ID for tracing and debugging

### Content-Type

- **Request:** `application/json` ou `multipart/form-data` (upload)
- **Response:** `application/json` com `camelCase` (Task 6.3)

### JSON Serialization (Task 6.3)

```json
{
  "propertyName": "camelCase",
  "nullHandling": "ignore",
  "dateFormat": "ISO 8601",
  "enumFormat": "string"
}
```

---

## 🔐 Authentication

### JWT Bearer Token

The API uses JWT authentication. Include the token in the header of all requests:

```http
Authorization: Bearer {token}
```

### Token Expiration

- **Validity:** 1 hour
- **Refresh:** Login again when expired

---

## 📡 Endpoints

## 1. Auth Endpoints

### 1.1 Login

Authenticates user and returns JWT token.

**Endpoint:** `POST /api/v1/auth/login`

**Request Body:**
```json
{
  "username": "admin",
  "password": "admin123"
}
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresAt": "2026-02-14T11:30:00Z",
    "username": "admin"
  },
  "errors": null,
  "traceId": "00-auth123-456-00"
}
```

**Response 401 Unauthorized:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "credentials": ["Invalid username or password"]
  },
  "traceId": "00-auth123-456-00"
}
```

**Status Codes (Task 6.2):**
- `200 OK` - Successful login
- `400 Bad Request` - Validation failed
- `401 Unauthorized` - Invalid credentials

---

### 1.2 Get Profile

Returns authenticated user information.

**Endpoint:** `GET /api/v1/auth/profile`

**Headers:**
```http
Authorization: Bearer {token}
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "username": "admin",
    "email": "admin@insightengine.com",
    "roles": ["Admin"]
  },
  "errors": null,
  "traceId": "00-profile789-123-00"
}
```

---

## 2. Dataset Endpoints

### 2.1 Upload Dataset

Uploads CSV file for analysis.

**Endpoint:** `POST /api/v1/datasets`

**Headers:**
```http
Authorization: Bearer {token}
Content-Type: multipart/form-data
```

**Request Body:**
```http
Content-Type: multipart/form-data

file: [arquivo.csv]
```

**Limits (Task 6.5):**
- **Max size:** 20MB
- **Format:** CSV with header
- **Encoding:** UTF-8 recommended

**Response 201 Created:**
```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "sales_data.csv",
    "storedFileName": "3fa85f64-5717-4562-b3fc-2c963f66afa6.csv",
    "fileSizeBytes": 1048576,
    "createdAt": "2026-02-14T10:30:00Z"
  },
  "errors": null,
  "traceId": "00-upload456-789-00"
}
```

**Response 400 Bad Request:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "file": [
      "File is required",
      "File must be a CSV (.csv extension)",
      "File size must not exceed 20MB"
    ]
  },
  "traceId": "00-upload456-789-00"
}
```

**Status Codes:**
- `201 Created` - Successful upload
- `400 Bad Request` - Validation failed
- `413 Payload Too Large` - File > 20MB
- `401 Unauthorized` - Invalid/expired token

**cURL Example:**
```bash
curl -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer {token}" \
  -F "file=@samples/ecommerce_sales.csv"
```

---

### 2.2 List All Datasets

Returns list of all user datasets.

**Endpoint:** `GET /api/v1/datasets`

**Headers:**
```http
Authorization: Bearer {token}
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "originalFileName": "sales_data.csv",
      "fileSizeBytes": 1048576,
      "uploadedAt": "2026-02-14T10:30:00Z"
    },
    {
      "id": "7cb91f82-8234-4abc-def1-234567890abc",
      "originalFileName": "employee_records.csv",
      "fileSizeBytes": 524288,
      "uploadedAt": "2026-02-14T09:15:00Z"
    }
  ],
  "errors": null,
  "traceId": "00-list123-456-00"
}
```

**Status Codes:**
- `200 OK` - List returned successfully
- `401 Unauthorized` - Invalid token

---

### 2.3 Get Dataset Profile

Returns detailed dataset analysis with column statistics.

**Endpoint:** `GET /api/v1/datasets/{id}/profile`

**Headers:**
```http
Authorization: Bearer {token}
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "rowCount": 5000,
    "sampleSize": 5000,
    "columns": [
      {
        "name": "order_date",
        "inferredType": "Date",
        "nullRate": 0.0,
        "distinctCount": 365,
        "topValues": ["2023-01-15", "2023-02-20", "2023-03-10"],
        "min": null,
        "max": null
      },
      {
        "name": "total_amount",
        "inferredType": "Number",
        "nullRate": 0.02,
        "distinctCount": 3847,
        "topValues": ["99.99", "149.99", "199.99"],
        "min": 10.50,
        "max": 9999.99
      },
      {
        "name": "category",
        "inferredType": "Category",
        "nullRate": 0.0,
        "distinctCount": 8,
        "topValues": ["Electronics", "Books", "Clothing"],
        "min": null,
        "max": null
      },
      {
        "name": "is_premium",
        "inferredType": "Boolean",
        "nullRate": 0.0,
        "distinctCount": 2,
        "topValues": ["true", "false"],
        "min": null,
        "max": null
      },
      {
        "name": "customer_id",
        "inferredType": "String",
        "nullRate": 0.0,
        "distinctCount": 4985,
        "topValues": ["CUST001", "CUST002", "CUST003"],
        "min": null,
        "max": null
      }
    ]
  },
  "errors": null,
  "traceId": "00-profile456-789-00"
}
```

**New in Task 6.6: Min/Max for numeric columns**

Columns with `inferredType: "Number"` now include:
- `min` (double): Minimum value found
- `max` (double): Maximum value found

**Min/Max Usage:**
- Calculate optimized bins for histograms: `(max - min) / bins`
- Define chart axis scales
- Validate outliers before plotting
- Avoid extra DuckDB queries

**Cache (Task 6.4):**
Profile is cached in memory after first request. Dataset updates automatically invalidate the cache.

**Response 404 Not Found:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "id": ["Dataset not found"]
  },
  "traceId": "00-profile456-789-00"
}
```

**Status Codes:**
- `200 OK` - Profile returned successfully
- `404 Not Found` - Dataset not found
- `401 Unauthorized` - Invalid token

---

### 2.4 Get Chart Recommendations

Returns intelligent chart recommendations based on dataset profile.

**Endpoint:** `GET /api/v1/datasets/{id}/recommendations`

**Headers:**
```http
Authorization: Bearer {token}
```

**Response 200 OK:**
```json
{
  "success": true,
  "data": [
    {
      "id": "rec_001",
      "title": "Sales Over Time",
      "description": "Line chart showing sales trends by date",
      "chartType": "Line",
      "confidence": 0.95,
      "xColumn": "order_date",
      "yColumn": "total_amount",
      "aggregation": "sum"
    },
    {
      "id": "rec_002",
      "title": "Sales by Category",
      "description": "Bar chart comparing sales across categories",
      "chartType": "Bar",
      "confidence": 0.88,
      "xColumn": "category",
      "yColumn": "total_amount",
      "aggregation": "sum"
    },
    {
      "id": "rec_003",
      "title": "Discount Impact Analysis",
      "description": "Scatter plot showing correlation between discount and sales",
      "chartType": "Scatter",
      "confidence": 0.75,
      "xColumn": "discount_percentage",
      "yColumn": "total_amount",
      "aggregation": null
    }
  ],
  "errors": null,
  "traceId": "00-recs789-123-00"
}
```

**Status Codes:**
- `200 OK` - Recommendations generated successfully
- `404 Not Found` - Dataset not found
- `401 Unauthorized` - Invalid token

---

## 3. Chart Endpoints

### 3.1 Generate Line Chart

Generates line chart to visualize temporal trends.

**Endpoint:** `POST /api/v1/charts/line`

**Headers:**
```http
Authorization: Bearer {token}
Content-Type: application/json
```

**Request Body:**
```json
{
  "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "xColumn": "order_date",
  "yColumn": "total_amount"
}
```

**Requirements:**
- `xColumn`: Must be `Date` type
- `yColumn`: Must be `Number` type

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "chartType": "line",
    "labels": ["2023-01-01", "2023-01-02", "2023-01-03"],
    "datasets": [
      {
        "label": "total_amount",
        "data": [1250.50, 1830.75, 2100.00]
      }
    ],
    "metadata": {
      "rowsProcessed": 365,
      "executionTimeMs": 125,
      "generatedAt": "2026-02-14T10:35:00Z"
    }
  },
  "errors": null,
  "traceId": "00-line123-456-00"
}
```

**Response 400 Bad Request:**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "xColumn": ["Column 'order_date' not found in dataset"],
    "yColumn": ["Column 'total_amount' must be of type Number"]
  },
  "traceId": "00-line123-456-00"
}
```

**Status Codes:**
- `200 OK` - Chart generated successfully
- `400 Bad Request` - Validation failed
- `404 Not Found` - Dataset not found

---

### 3.2 Generate Bar Chart

Generates bar chart for categorical comparisons.

**Endpoint:** `POST /api/v1/charts/bar`

**Request Body:**
```json
{
  "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "xColumn": "category",
  "yColumn": "total_amount",
  "aggregation": "sum"
}
```

**Parameters:**
- `xColumn`: Categorical column (Category or String)
- `yColumn`: Numeric column (Number)
- `aggregation`: `sum`, `avg`, `count`, `min`, `max`

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "chartType": "bar",
    "labels": ["Electronics", "Books", "Clothing", "Home", "Sports"],
    "datasets": [
      {
        "label": "sum(total_amount)",
        "data": [125000.50, 89300.25, 67800.00, 54200.75, 42100.00]
      }
    ],
    "metadata": {
      "rowsProcessed": 5000,
      "aggregation": "sum",
      "executionTimeMs": 87
    }
  },
  "errors": null,
  "traceId": "00-bar456-789-00"
}
```

**Aggregations:**
- `sum`: Sum of values
- `avg`: Average of values
- `count`: Count of records
- `min`: Minimum value
- `max`: Maximum value

---

### 3.3 Generate Scatter Chart

Generates scatter plot for correlation analysis.

**Endpoint:** `POST /api/v1/charts/scatter`

**Request Body:**
```json
{
  "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "xColumn": "discount_percentage",
  "yColumn": "total_amount"
}
```

**Requirements:**
- `xColumn`: Must be `Number` type
- `yColumn`: Must be `Number` type

**Safety Limit (Task 6.5):**
- **Max points:** 2,000
- If dataset > 2,000 rows, random sampling is applied automatically

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "chartType": "scatter",
    "points": [
      { "x": 5.0, "y": 150.50 },
      { "x": 10.0, "y": 135.75 },
      { "x": 15.0, "y": 127.80 },
      { "x": 20.0, "y": 120.00 }
    ],
    "metadata": {
      "rowsProcessed": 2000,
      "totalRows": 5000,
      "sampled": true,
      "executionTimeMs": 92
    }
  },
  "errors": null,
  "traceId": "00-scatter789-123-00"
}
```

**Field `sampled`:**
- `true`: Sampling was applied (dataset > 2k rows)
- `false`: All points were returned

---

### 3.4 Generate Histogram

Generates histogram to visualize value distribution.

**Endpoint:** `POST /api/v1/charts/histogram`

**Request Body:**
```json
{
  "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "column": "total_amount",
  "bins": 20
}
```

**Parameters:**
- `column`: Numeric column (Number)
- `bins` (optional): Number of bins (default: 20)

**Safety Limits (Task 6.5):**
- **Min bins:** 5
- **Max bins:** 50
- Values outside range are automatically clamped

**Response 200 OK:**
```json
{
  "success": true,
  "data": {
    "chartType": "histogram",
    "bins": [
      { "min": 0, "max": 500, "count": 850 },
      { "min": 500, "max": 1000, "count": 1200 },
      { "min": 1000, "max": 1500, "count": 980 },
      { "min": 1500, "max": 2000, "count": 620 }
    ],
    "metadata": {
      "rowsProcessed": 5000,
      "bins": 20,
      "binsRequested": 20,
      "binsClamped": false,
      "min": 10.50,
      "max": 9999.99,
      "executionTimeMs": 78
    }
  },
  "errors": null,
  "traceId": "00-hist123-456-00"
}
```

**Field `binsClamped`:**
- `true`: Bins were adjusted to 5-50 range
- `false`: Requested bins were in valid range

**Example with clamping:**
```json
// Request: bins = 100
// Response:
{
  "metadata": {
    "bins": 50,
    "binsRequested": 100,
    "binsClamped": true
  }
}
```

---

## 📦 Data Models

### InferredType (enum)

Data types inferred by the Profiler:

```typescript
enum InferredType {
  Number = "Number",
  Date = "Date",
  Boolean = "Boolean",
  Category = "Category",
  String = "String"
}
```

**Inference rules:**
- **Number:** > 90% values parseable as decimal
- **Date:** > 90% values parseable as date (ISO, BR, US)
- **Boolean:** > 90% values true/false, yes/no, 1/0
- **Category:** Distinct count ≤ 5% of total rows
- **String:** Default (free text)

### ChartType (enum)

Supported chart types:

```typescript
enum ChartType {
  Line = "Line",
  Bar = "Bar",
  Scatter = "Scatter",
  Histogram = "Histogram"
}
```

### Aggregation (enum)

Available aggregations for Bar charts:

```typescript
enum Aggregation {
  Sum = "sum",
  Avg = "avg",
  Count = "count",
  Min = "min",
  Max = "max"
}
```

---

## ⚠️ Error Handling

### Standardized Error Format

```json
{
  "success": false,
  "data": null,
  "errors": {
    "field1": ["Error message 1", "Error message 2"],
    "field2": ["Error message"]
  },
  "traceId": "00-error123-456-00"
}
```

### HTTP Codes (Task 6.2)

| Code | Meaning | When to use |
|------|---------|-------------|
| `200 OK` | Success | GET, POST chart generated |
| `201 Created` | Resource created | POST dataset upload |
| `400 Bad Request` | Validation failed | Invalid parameters |
| `401 Unauthorized` | Not authenticated | Missing/invalid token |
| `404 Not Found` | Resource doesn't exist | Dataset/ID not found |
| `413 Payload Too Large` | File too large | Upload > 20MB |
| `422 Unprocessable Entity` | Business logic failed | Domain rules |
| `500 Internal Server Error` | Unexpected error | Unhandled exception |

### Error Examples

**Validation (400):**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "file": ["File is required"],
    "xColumn": ["Column 'invalid_col' not found"]
  },
  "traceId": "00-val123-456-00"
}
```

**Not Found (404):**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "id": ["Dataset '00000000-0000-0000-0000-000000000000' not found"]
  },
  "traceId": "00-notfound-789-00"
}
```

**Unauthorized (401):**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "authorization": ["Token expired or invalid"]
  },
  "traceId": "00-auth-error-123-00"
}
```

**Payload Too Large (413):**
```json
{
  "success": false,
  "data": null,
  "errors": {
    "file": ["File size 25MB exceeds maximum allowed size of 20MB"]
  },
  "traceId": "00-toolarge-456-00"
}
```

---

## 🔒 Security and Limits

### Upload Limits (Task 6.5)

| Limit | Value | Configurable |
|-------|-------|--------------|
| Max size | 20MB | `appsettings.json:UploadSettings:MaxFileSizeBytes` |
| Timeout | 5 minutes | `Kestrel:Limits:RequestTimeout` |
| Rate limit | 100 req/min | Not implemented |

**Validations:**
- `.csv` extension required
- UTF-8 encoding recommended
- Header required

### Chart Limits (Task 6.5)

| Chart Type | Limit | Behavior |
|------------|-------|----------|
| Scatter | 2,000 points | Automatic random sampling |
| Histogram | 5-50 bins | Automatic clamping |
| Line | No limit | - |
| Bar | No limit | - |

**Configuration (appsettings.json):**
```json
{
  "ChartExecution": {
    "ScatterMaxPoints": 2000,
    "HistogramMinBins": 5,
    "HistogramMaxBins": 50
  }
}
```

### Cache (Task 6.4)

**Profile caching:**
- Armazenado em `IMemoryCache`
- TTL: Indefinido (invalidado manualmente)
- Key: `profile:{datasetId}`
- Invalidação: Ao deletar/atualizar dataset

**Benefits:**
- Reduces DuckDB calls
- Improves recommendation latency
- Supports multiple simultaneous requests

---

## 💡 Integration Examples

### JavaScript/TypeScript

```typescript
class InsightEngineClient {
  private baseUrl = 'https://localhost:5000/api/v1';
  private token: string = '';

  async login(username: string, password: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });

    const json = await response.json();
    if (json.success) {
      this.token = json.data.token;
    } else {
      throw new Error(json.errors);
    }
  }

  async uploadDataset(file: File): Promise<string> {
    const formData = new FormData();
    formData.append('file', file);

    const response = await fetch(`${this.baseUrl}/datasets`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${this.token}` },
      body: formData
    });

    const json = await response.json();
    if (!json.success) throw new Error(json.errors);
    return json.data.id;
  }

  async getProfile(datasetId: string) {
    const response = await fetch(
      `${this.baseUrl}/datasets/${datasetId}/profile`,
      {
        headers: { 'Authorization': `Bearer ${this.token}` }
      }
    );
    return response.json();
  }

  async generateLineChart(
    datasetId: string,
    xColumn: string,
    yColumn: string
  ) {
    const response = await fetch(`${this.baseUrl}/charts/line`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${this.token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ datasetId, xColumn, yColumn })
    });

    const json = await response.json();
    if (!json.success) {
      console.error('TraceId:', json.traceId);
      throw new Error(json.errors);
    }
    return json.data;
  }
}

// Usage
const client = new InsightEngineClient();
await client.login('admin', 'admin123');

const datasetId = await client.uploadDataset(file);
const profile = await client.getProfile(datasetId);
const chart = await client.generateLineChart(
  datasetId,
  'order_date',
  'total_amount'
);
```

### Python

```python
import requests
from typing import Dict, Any

class InsightEngineClient:
    def __init__(self, base_url: str = "https://localhost:5000/api/v1"):
        self.base_url = base_url
        self.token = None

    def login(self, username: str, password: str) -> None:
        response = requests.post(
            f"{self.base_url}/auth/login",
            json={"username": username, "password": password}
        )
        data = response.json()
        
        if data["success"]:
            self.token = data["data"]["token"]
        else:
            raise Exception(data["errors"])

    def _headers(self) -> Dict[str, str]:
        return {"Authorization": f"Bearer {self.token}"}

    def upload_dataset(self, file_path: str) -> str:
        with open(file_path, 'rb') as f:
            files = {'file': f}
            response = requests.post(
                f"{self.base_url}/datasets",
                headers=self._headers(),
                files=files
            )
        
        data = response.json()
        if not data["success"]:
            raise Exception(data["errors"])
        return data["data"]["id"]

    def get_profile(self, dataset_id: str) -> Dict[str, Any]:
        response = requests.get(
            f"{self.base_url}/datasets/{dataset_id}/profile",
            headers=self._headers()
        )
        return response.json()

    def generate_histogram(
        self,
        dataset_id: str,
        column: str,
        bins: int = 20
    ) -> Dict[str, Any]:
        response = requests.post(
            f"{self.base_url}/charts/histogram",
            headers={**self._headers(), "Content-Type": "application/json"},
            json={"datasetId": dataset_id, "column": column, "bins": bins}
        )
        
        data = response.json()
        if not data["success"]:
            print(f"TraceId: {data['traceId']}")
            raise Exception(data["errors"])
        return data["data"]

# Usage
client = InsightEngineClient()
client.login("admin", "admin123")

dataset_id = client.upload_dataset("sales_data.csv")
profile = client.get_profile(dataset_id)
histogram = client.generate_histogram(dataset_id, "salary", bins=30)
```

### cURL Examples

```bash
# 1. Login
TOKEN=$(curl -s -X POST "https://localhost:5000/api/v1/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' \
  | jq -r '.data.token')

# 2. Upload Dataset
DATASET_ID=$(curl -s -X POST "https://localhost:5000/api/v1/datasets" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/ecommerce_sales.csv" \
  | jq -r '.data.id')

# 3. Get Profile
curl -X GET "https://localhost:5000/api/v1/datasets/$DATASET_ID/profile" \
  -H "Authorization: Bearer $TOKEN" \
  | jq

# 4. Generate Bar Chart
curl -X POST "https://localhost:5000/api/v1/charts/bar" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"datasetId\": \"$DATASET_ID\",
    \"xColumn\": \"category\",
    \"yColumn\": \"total_amount\",
    \"aggregation\": \"sum\"
  }" \
  | jq

# 5. Generate Scatter (with sampling)
curl -X POST "https://localhost:5000/api/v1/charts/scatter" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"datasetId\": \"$DATASET_ID\",
    \"xColumn\": \"discount_percentage\",
    \"yColumn\": \"total_amount\"
  }" \
  | jq '.data.metadata.sampled'  # Check if sampling was applied
```

---

## 📚 Additional Resources

### Related Documentation

- **[Samples Guide](../samples/README.md)** - Test datasets and examples
- **[Test Suite](../samples/test-samples.http)** - Complete HTTP requests
- **[Architecture](../ARCHITECTURE.md)** - Architecture overview
- **[Profiling System](./DIA2_DATASET_PROFILING.md)** - How the Profiler works
- **[Chart Execution](./DAY-4-EXECUTION.md)** - Chart generation with DuckDB

### Roadmap

**In Development:**
- [ ] Support for other formats (Excel, JSON, Parquet)
- [ ] More chart types (Pie, Area, Heatmap)
- [ ] Chart export (PNG, SVG, PDF)
- [ ] Scheduled reports
- [ ] Data transformations (filter, sort, aggregate)

**Completed:**
- [x] Line, Bar, Scatter, Histogram charts
- [x] JWT Authentication
- [x] Profile caching
- [x] Min/Max calculation
- [x] Safety limits (upload, scatter, histogram)
- [x] Standardized API envelope
- [x] HTTP status code mapping
- [x] JSON serialization standards

---

## 🐛 Troubleshooting

### "Dataset not found"
- Verifique se o `datasetId` está correto (UUID válido)
- Confirme que o token JWT é válido
- Use `GET /api/v1/datasets` para listar todos

### "Column not found in dataset"
- Column names are **case-sensitive**
- Use `GET /api/v1/datasets/{id}/profile` to see available columns
- Check for spaces and special characters

### "Chart generation failed"
- **Line Chart:** xColumn must be Date, yColumn Number
- **Bar Chart:** xColumn Category/String, yColumn Number
- **Scatter Chart:** both columns Number
- **Histogram:** column must be Number

### "Unauthorized"
- JWT token expired (valid for 1 hour)
- Login again: `POST /api/v1/auth/login`
- Check header: `Authorization: Bearer {token}`

### "Payload Too Large"
- Files > 20MB are not supported
- Compress CSV or remove unnecessary columns
- Consider partitioning data

### TraceId for Support

Always include the `traceId` when reporting issues:

```json
{
  "traceId": "00-abc123def456-789-00"
}
```

Server logs include this ID for end-to-end tracing.

---

## 📝 Changelog

### v1.0 - 2026-02-14 (Day 6)

**Added:**
- Task 6.1: Standardized API response envelope
- Task 6.2: HTTP status code mapping
- Task 6.3: JSON serialization standards (camelCase)
- Task 6.4: Profile caching with IMemoryCache
- Task 6.5: Safety limits (20MB upload, 2k scatter points, 5-50 histogram bins)
- Task 6.6: Min/Max calculation for numeric columns
- Task 6.7: Enhanced samples directory with test suite
- Task 6.8: Complete API documentation (this file)

**Charts:**
- Line: Temporal trends
- Bar: Categorical comparisons with aggregations
- Scatter: Correlation analysis with sampling
- Histogram: Distribution visualization with bin clamping

**Authentication:**
- JWT Bearer token auth
- 1 hour expiration
- Login and profile endpoints

---

**Version:** 1.0  
**Date:** 2026-02-14  
**Task:** Day 6 - Task 6.8  
**Status:** ✅ Production Ready

**Support:** support@insightengine.com  
**Repository:** https://github.com/danpvid/InsightEngine

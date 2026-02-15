# Release & Deployment Checklist

## Overview

This document provides a comprehensive checklist for deploying InsightEngine to production environments. Follow these steps to ensure a safe, reliable deployment.

**Version**: 1.0.0  
**Last Updated**: February 14, 2026  
**Target Framework**: .NET 10.0

---

## Pre-Deployment Checklist

### ✅ Code Quality

- [ ] All integration tests passing (40/40 tests)
- [ ] Code review completed and approved
- [ ] No critical or high-severity vulnerabilities (package vulnerabilities documented)
- [ ] Static code analysis completed (SonarQube/CodeQL) - Optional
- [ ] Performance tests completed successfully
- [ ] Security scanning completed (OWASP ZAP, Snyk) - Optional

**Note**: Project currently has **integration tests only** (no unit tests). Consider adding unit tests for:
- Domain entities and value objects
- Validators (FluentValidation)
- Recommendation engine logic
- CSV profiler algorithms
- Chart execution service

### ✅ Documentation

- [ ] API documentation up-to-date (`docs/API.md`)
- [ ] Changelog updated with release notes
- [ ] Known issues documented
- [ ] Migration guide prepared (if breaking changes)
- [ ] Runbook created for operations team

### ✅ Version Control

- [ ] All changes committed and pushed
- [ ] Release branch created (`release/v1.0.0`)
- [ ] Version numbers updated in `.csproj` files
- [ ] Git tag created (`v1.0.0`)
- [ ] Release notes added to GitHub/GitLab

---

## Environment Configuration

### 1. Environment Variables

Create or update the following environment variables:

```bash
# Application Settings
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80;https://+:443

# Database
ConnectionStrings__DefaultConnection="Server=prod-sql-server;Database=InsightEngine;User Id=app_user;Password=***;Encrypt=True;TrustServerCertificate=False;"

# Azure Blob Storage (File Storage)
AZURE_STORAGE_CONNECTION_STRING="DefaultEndpointsProtocol=https;AccountName=***;AccountKey=***"
AZURE_STORAGE_CONTAINER="datasets"

# JWT Authentication
JWT__Secret="your-super-secure-secret-key-min-256-bits"
JWT__Issuer="https://api.insightengine.com"
JWT__Audience="https://api.insightengine.com"
JWT__ExpirationMinutes=60

# Python Execution (Chart Generation)
PYTHON_EXECUTABLE_PATH="/usr/bin/python3"
PYTHON_SCRIPTS_PATH="/app/python-scripts"

# Logging
LOGGING__LOGLEVEL__DEFAULT=Information
LOGGING__LOGLEVEL__MICROSOFT_ASPNETCORE=Warning
LOGGING__LOGLEVEL__INSIGHTENGINE_DOMAIN_BEHAVIORS=Information

# Application Insights (Optional)
APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=***;IngestionEndpoint=https://***"

# CORS Origins (Comma-separated)
CORS__ALLOWED_ORIGINS="https://app.insightengine.com,https://admin.insightengine.com"

# Rate Limiting (Optional)
RATELIMIT__REQUESTS_PER_MINUTE=60
RATELIMIT__REQUESTS_PER_HOUR=1000
```

### 2. appsettings.Production.json

Create `appsettings.Production.json` in `src/InsightEngine.API/`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "InsightEngine.Domain.Behaviors": "Information",
      "InsightEngine.Domain.Behaviors.LoggingBehavior": "Warning"
    },
    "ApplicationInsights": {
      "LogLevel": {
        "Default": "Information"
      }
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 20971520,
      "RequestHeadersTimeout": "00:02:00",
      "KeepAliveTimeout": "00:02:00"
    }
  },
  "ChartExecution": {
    "MaxScatterPoints": 2000,
    "HistogramBinRange": {
      "Min": 5,
      "Max": 50
    }
  },
  "Swagger": {
    "Enabled": false
  }
}
```

### 3. Secrets Management

**NEVER commit secrets to source control!**

#### Option A: Azure Key Vault (Recommended)

```csharp
// Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

#### Option B: User Secrets (Development)

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=..."
dotnet user-secrets set "JWT:Secret" "your-secret-key"
```

#### Option C: Environment Variables (Docker/K8s)

```yaml
# Kubernetes Secret
apiVersion: v1
kind: Secret
metadata:
  name: insightengine-secrets
type: Opaque
stringData:
  ConnectionStrings__DefaultConnection: "Server=..."
  JWT__Secret: "your-secret-key"
```

---

## Database Setup

### 1. SQL Server Requirements

- **Version**: SQL Server 2019+ or Azure SQL Database
- **Edition**: Standard or Premium (for production workloads)
- **Collation**: `SQL_Latin1_General_CP1_CI_AS`
- **Compatibility Level**: 150+

### 2. Database Creation

```sql
-- Create database
CREATE DATABASE InsightEngine
COLLATE SQL_Latin1_General_CP1_CI_AS;
GO

-- Create application user
USE InsightEngine;
GO

CREATE LOGIN insightengine_app 
WITH PASSWORD = 'SuperSecurePassword123!';
GO

CREATE USER insightengine_app 
FOR LOGIN insightengine_app;
GO

-- Grant permissions (least privilege)
ALTER ROLE db_datareader ADD MEMBER insightengine_app;
ALTER ROLE db_datawriter ADD MEMBER insightengine_app;
GO

-- Grant schema permissions
GRANT CREATE TABLE TO insightengine_app;
GRANT ALTER ON SCHEMA::dbo TO insightengine_app;
GO
```

### 3. Run Migrations

```bash
# Install EF Core tools (if not already installed)
dotnet tool install --global dotnet-ef

# Navigate to API project
cd src/InsightEngine.API

# Apply migrations to production database
dotnet ef database update --connection "Server=prod-sql;Database=InsightEngine;User Id=insightengine_app;Password=***"

# Verify migration
dotnet ef migrations list --connection "..."
```

### 4. Database Indexes (Performance)

```sql
-- Indexes for common queries
USE InsightEngine;
GO

-- DataSets table
CREATE NONCLUSTERED INDEX IX_DataSets_UploadDate 
ON DataSets(UploadDate DESC);

CREATE NONCLUSTERED INDEX IX_DataSets_IsActive 
ON DataSets(IsActive) 
INCLUDE (Id, OriginalFileName, UploadDate);

-- Add more indexes based on query patterns
```

### 5. Backup Strategy

```sql
-- Full backup daily at 2 AM
BACKUP DATABASE InsightEngine
TO DISK = 'C:\Backups\InsightEngine_Full.bak'
WITH COMPRESSION, INIT;

-- Transaction log backup every 15 minutes
BACKUP LOG InsightEngine
TO DISK = 'C:\Backups\InsightEngine_Log.trn'
WITH COMPRESSION;
```

---

## File Storage Setup

### Azure Blob Storage (Recommended)

#### 1. Create Storage Account

```bash
# Azure CLI
az storage account create \
  --name insightenginestorage \
  --resource-group InsightEngine-RG \
  --location eastus \
  --sku Standard_LRS \
  --kind StorageV2

# Create container
az storage container create \
  --name datasets \
  --account-name insightenginestorage \
  --public-access off
```

#### 2. Configure CORS (if needed)

```bash
az storage cors add \
  --services b \
  --methods GET POST PUT \
  --origins https://app.insightengine.com \
  --allowed-headers '*' \
  --exposed-headers '*' \
  --max-age 3600 \
  --account-name insightenginestorage
```

#### 3. Set Retention Policy

```bash
# Lifecycle management rule (delete files older than 90 days)
az storage account management-policy create \
  --account-name insightenginestorage \
  --policy @lifecycle-policy.json
```

**lifecycle-policy.json**:
```json
{
  "rules": [
    {
      "enabled": true,
      "name": "delete-old-datasets",
      "type": "Lifecycle",
      "definition": {
        "actions": {
          "baseBlob": {
            "delete": {
              "daysAfterModificationGreaterThan": 90
            }
          }
        },
        "filters": {
          "blobTypes": ["blockBlob"],
          "prefixMatch": ["datasets/"]
        }
      }
    }
  ]
}
```

### Alternative: Local File Storage

```bash
# Create directories
mkdir -p /var/insightengine/uploads
mkdir -p /var/insightengine/temp

# Set permissions
chown -R www-data:www-data /var/insightengine
chmod 750 /var/insightengine/uploads
```

---

## Python Environment Setup

### 1. Install Python Dependencies

```bash
# Install Python 3.10+
apt-get update
apt-get install -y python3.10 python3-pip

# Verify installation
python3 --version

# Install required packages
pip3 install pandas matplotlib seaborn scipy numpy
```

### 2. Python Scripts Deployment

```bash
# Copy Python scripts to deployment directory
mkdir -p /app/python-scripts
cp python-scripts/*.py /app/python-scripts/

# Set permissions
chmod 755 /app/python-scripts
chmod 644 /app/python-scripts/*.py
```

### 3. Verify Python Execution

```bash
# Test Python execution
python3 /app/python-scripts/generate_chart.py --help

# Test required libraries
python3 -c "import pandas, matplotlib, seaborn; print('OK')"
```

---

## Docker Deployment

### 1. Build Docker Image

```bash
# Build production image
docker build -t insightengine:1.0.0 -f src/InsightEngine.API/Dockerfile .

# Tag for registry
docker tag insightengine:1.0.0 myregistry.azurecr.io/insightengine:1.0.0
docker tag insightengine:1.0.0 myregistry.azurecr.io/insightengine:latest

# Push to registry
docker push myregistry.azurecr.io/insightengine:1.0.0
docker push myregistry.azurecr.io/insightengine:latest
```

### 2. Docker Compose (Production)

**docker-compose.production.yml**:

```yaml
version: '3.8'

services:
  api:
    image: myregistry.azurecr.io/insightengine:1.0.0
    container_name: insightengine-api
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=${DB_CONNECTION_STRING}
      - AZURE_STORAGE_CONNECTION_STRING=${STORAGE_CONNECTION_STRING}
      - JWT__Secret=${JWT_SECRET}
    volumes:
      - /var/insightengine/uploads:/app/uploads
      - /app/python-scripts:/app/python-scripts:ro
      - ./certs:/https:ro
    networks:
      - insightengine-net
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"

  # Optional: Add SQL Server if not using managed instance
  # db:
  #   image: mcr.microsoft.com/mssql/server:2022-latest
  #   ...

networks:
  insightengine-net:
    driver: bridge
```

### 3. Run Container

```bash
# Load environment variables
source .env.production

# Start services
docker-compose -f docker-compose.production.yml up -d

# Check logs
docker-compose logs -f api

# Verify health
curl http://localhost/health
```

---

## Kubernetes Deployment

### 1. Create Namespace

```bash
kubectl create namespace insightengine-prod
```

### 2. Deployment Manifest

**deployment.yaml**:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: insightengine-api
  namespace: insightengine-prod
spec:
  replicas: 3
  selector:
    matchLabels:
      app: insightengine-api
  template:
    metadata:
      labels:
        app: insightengine-api
        version: "1.0.0"
    spec:
      containers:
      - name: api
        image: myregistry.azurecr.io/insightengine:1.0.0
        imagePullPolicy: Always
        ports:
        - containerPort: 80
          name: http
        - containerPort: 443
          name: https
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: insightengine-secrets
              key: db-connection-string
        - name: JWT__Secret
          valueFrom:
            secretKeyRef:
              name: insightengine-secrets
              key: jwt-secret
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "2Gi"
            cpu: "2000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 80
          initialDelaySeconds: 10
          periodSeconds: 5
        volumeMounts:
        - name: python-scripts
          mountPath: /app/python-scripts
          readOnly: true
      volumes:
      - name: python-scripts
        configMap:
          name: python-scripts
      imagePullSecrets:
      - name: acr-secret
---
apiVersion: v1
kind: Service
metadata:
  name: insightengine-api
  namespace: insightengine-prod
spec:
  type: LoadBalancer
  selector:
    app: insightengine-api
  ports:
  - name: http
    port: 80
    targetPort: 80
  - name: https
    port: 443
    targetPort: 443
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: insightengine-api-hpa
  namespace: insightengine-prod
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: insightengine-api
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### 3. Apply Configuration

```bash
# Create secrets
kubectl create secret generic insightengine-secrets \
  --from-literal=db-connection-string="Server=..." \
  --from-literal=jwt-secret="..." \
  --namespace=insightengine-prod

# Apply deployment
kubectl apply -f deployment.yaml

# Verify deployment
kubectl get pods -n insightengine-prod
kubectl describe deployment insightengine-api -n insightengine-prod
```

---

## Health Checks

### 1. Implement Health Check Endpoints

Add to `Program.cs`:

```csharp
// Basic health check
app.MapHealthChecks("/health");

// Detailed health check
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Configure health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection"),
        healthQuery: "SELECT 1",
        name: "sql-server",
        tags: new[] { "ready", "db" })
    .AddAzureBlobStorage(
        connectionString: builder.Configuration["AZURE_STORAGE_CONNECTION_STRING"],
        name: "azure-blob-storage",
        tags: new[] { "ready", "storage" });
```

### 2. Health Check Monitoring

```bash
# Check health
curl http://localhost/health

# Expected response
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "sql-server": {
      "status": "Healthy",
      "duration": "00:00:00.0098765"
    },
    "azure-blob-storage": {
      "status": "Healthy",
      "duration": "00:00:00.0024691"
    }
  }
}
```

---

## Monitoring & Observability

### 1. Application Insights (Azure)

```bash
# Install NuGet package
dotnet add package Microsoft.ApplicationInsights.AspNetCore

# Add to Program.cs
builder.Services.AddApplicationInsightsTelemetry();
```

### 2. Metrics to Monitor

| Metric | Alert Threshold | Action |
|--------|----------------|--------|
| **CPU Usage** | > 80% for 5 min | Scale up |
| **Memory Usage** | > 90% | Investigate memory leak |
| **Request Duration** | P95 > 2s | Optimize slow queries |
| **Error Rate** | > 1% | Page on-call engineer |
| **Validation Failures** | > 10% | Check client integrations |
| **Database Connections** | > 80% of pool | Increase pool size |
| **Storage I/O** | High latency | Check storage throttling |

### 3. Log Aggregation

#### Azure Log Analytics Query

```kusto
// Errors in last 24 hours
AppTraces
| where TimeGenerated > ago(24h)
| where SeverityLevel >= 3
| summarize count() by Message, SeverityLevel
| order by count_ desc

// Slow requests
AppRequests
| where TimeGenerated > ago(1h)
| where Duration > 2000
| summarize count() by Name
| order by count_ desc
```

### 4. Alerts

```bash
# Create alert rule (Azure CLI)
az monitor metrics alert create \
  --name "High Error Rate" \
  --resource-group InsightEngine-RG \
  --scopes /subscriptions/.../insightengine-api \
  --condition "count requests/failed > 10" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action email admin@company.com
```

---

## Security Hardening

### 1. HTTPS/TLS Configuration

```csharp
// Program.cs - Force HTTPS
app.UseHttpsRedirection();
app.UseHsts();

// Configure Kestrel
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024; // 20MB
    
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
});
```

### 2. Security Headers

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'");
    await next();
});
```

### 3. CORS Configuration

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        policy.WithOrigins(
            "https://app.insightengine.com",
            "https://admin.insightengine.com")
        .AllowedMethods("GET", "POST", "PUT", "DELETE")
        .AllowedHeaders("Content-Type", "Authorization")
        .AllowCredentials();
    });
});

app.UseCors("Production");
```

### 4. Rate Limiting

```bash
dotnet add package AspNetCoreRateLimit
```

```csharp
// appsettings.Production.json
{
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "GeneralRules": [
      {
        "Endpoint": "*",
        "Period": "1m",
        "Limit": 60
      },
      {
        "Endpoint": "*/api/datasets/upload",
        "Period": "1h",
        "Limit": 10
      }
    ]
  }
}
```

---

## Performance Optimization

### 1. Caching Strategy

```csharp
// Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = configuration["Redis:ConnectionString"];
    options.InstanceName = "InsightEngine:";
});
```

### 2. Database Connection Pooling

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Min Pool Size=10;Max Pool Size=100;Connection Timeout=30"
  }
}
```

### 3. Response Compression

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
});
```

---

## Rollback Procedures

### 1. Docker Rollback

```bash
# List previous images
docker images | grep insightengine

# Stop current version
docker-compose down

# Deploy previous version
docker-compose -f docker-compose.production.yml up -d \
  --force-recreate \
  insightengine:1.0.0-previous

# Verify rollback
curl http://localhost/health
```

### 2. Kubernetes Rollback

```bash
# Check rollout history
kubectl rollout history deployment/insightengine-api -n insightengine-prod

# Rollback to previous version
kubectl rollout undo deployment/insightengine-api -n insightengine-prod

# Rollback to specific revision
kubectl rollout undo deployment/insightengine-api --to-revision=2 -n insightengine-prod

# Verify rollback
kubectl get pods -n insightengine-prod
```

### 3. Database Rollback

```sql
-- Restore from backup
RESTORE DATABASE InsightEngine
FROM DISK = 'C:\Backups\InsightEngine_Full_BeforeDeployment.bak'
WITH REPLACE, RECOVERY;

-- Or revert specific migration
dotnet ef migrations remove --project src/InsightEngine.Infra.Data
dotnet ef database update PreviousMigrationName --project src/InsightEngine.API
```

---

## Post-Deployment Verification

### ✅ Smoke Tests

Run these tests immediately after deployment:

```bash
# 1. Health check
curl -f https://api.insightengine.com/health || exit 1

# 2. Authentication
curl -X POST https://api.insightengine.com/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"test"}' || exit 1

# 3. Upload dataset (small file)
curl -X POST https://api.insightengine.com/api/v1/datasets/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@samples/sample-small.csv" || exit 1

# 4. Get profile
curl -X GET "https://api.insightengine.com/api/v1/datasets/$DATASET_ID/profile" \
  -H "Authorization: Bearer $TOKEN" || exit 1
```

### ✅ Verification Checklist

- [ ] Health endpoint returns 200 OK
- [ ] All database connections healthy
- [ ] Storage access working
- [ ] Authentication working
- [ ] File upload working (small file)
- [ ] Chart generation working
- [ ] No errors in logs
- [ ] Metrics being collected
- [ ] Alerts configured and working
- [ ] SSL certificate valid
- [ ] CORS headers correct
- [ ] Response times acceptable (< 2s P95)

---

## Troubleshooting

### Issue: "Connection pool exhausted"

**Solution**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "...;Max Pool Size=200;..."
  }
}
```

### Issue: "Python script execution fails"

**Checks**:
```bash
# 1. Verify Python installation
docker exec insightengine-api python3 --version

# 2. Check Python libraries
docker exec insightengine-api python3 -c "import pandas; print(pandas.__version__)"

# 3. Test script directly
docker exec insightengine-api python3 /app/python-scripts/generate_chart.py --help
```

### Issue: "High memory usage"

**Solutions**:
1. Enable response streaming for large datasets
2. Increase container memory limits
3. Implement pagination for list endpoints
4. Clear metadata cache: `POST /api/v1/cache/clear`

### Issue: "Slow database queries"

**Analysis**:
```sql
-- Find slow queries
SELECT 
    qs.total_elapsed_time / qs.execution_count / 1000000.0 AS avg_seconds,
    qs.execution_count,
    SUBSTRING(qt.TEXT, (qs.statement_start_offset/2)+1,
        ((CASE qs.statement_end_offset
            WHEN -1 THEN DATALENGTH(qt.TEXT)
            ELSE qs.statement_end_offset
        END - qs.statement_start_offset)/2) + 1) AS statement
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS qt
ORDER BY avg_seconds DESC;
```

---

## Support Contacts

| Role | Contact | Availability |
|------|---------|--------------|
| **DevOps Lead** | devops@company.com | 24/7 |
| **Database Admin** | dba@company.com | Business hours |
| **Security Team** | security@company.com | 24/7 |
| **On-Call Engineer** | oncall@company.com | 24/7 |

---

## Appendix

### A. Useful Commands

```bash
# Check application version
curl https://api.insightengine.com/api/v1/version

# Clear metadata cache
curl -X POST https://api.insightengine.com/api/v1/cache/clear \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# Export logs (Docker)
docker logs insightengine-api > logs.txt 2>&1

# Export logs (Kubernetes)
kubectl logs -n insightengine-prod deployment/insightengine-api --tail=1000 > logs.txt
```

### B. Performance Benchmarks

| Endpoint | Expected Response Time (P95) | Max File Size |
|----------|------------------------------|---------------|
| Upload Dataset | < 5s | 20 MB |
| Get Profile | < 500ms | N/A |
| Get Recommendations | < 300ms | N/A |
| Generate Chart | < 3s | N/A |
| List Datasets | < 200ms | N/A |

### C. Scaling Guidelines

| Load | Recommended Configuration |
|------|--------------------------|
| **< 100 req/min** | 2 instances, 1 GB RAM each |
| **100-500 req/min** | 3 instances, 2 GB RAM each |
| **500-1000 req/min** | 5 instances, 2 GB RAM each, read replicas |
| **> 1000 req/min** | 10+ instances, CDN, Redis cache, dedicated DB |

---

## Changelog

### Version 1.0.0 (February 14, 2026)

**Initial Production Release**

- ✅ REST API with 5 core endpoints
- ✅ CSV profiling with statistics
- ✅ Chart generation (7 types)
- ✅ Smart recommendations engine
- ✅ Azure Blob Storage integration
- ✅ JWT authentication
- ✅ Structured logging with MediatR pipeline
- ✅ Metadata caching
- ✅ Safety limits (20MB, 2k scatter, 5-50 bins)
- ✅ FluentValidation
- ✅ 40 integration tests
- ✅ Comprehensive API documentation
- ✅ Production-ready configuration

**Known Limitations**:
- Maximum file size: 20 MB
- Supported formats: CSV only
- Python required for chart generation
- No real-time updates (polling required)

**Next Release (v1.1.0)**:
- Excel file support
- Real-time WebSocket updates
- Advanced filtering
- Export to PDF/PNG
- Multi-tenancy support

---

## Future Improvements

### Testing Strategy

**Current State**: 40 integration tests covering end-to-end scenarios

**Recommended Additions**:

1. **Unit Tests** (Target: 80%+ code coverage)
   - Domain entities and value objects
   - FluentValidation validators
   - Recommendation engine algorithms
   - CSV profiler logic (min/max, statistics)
   - Chart execution service
   - Pipeline behaviors

2. **Performance Tests**
   - Load testing (JMeter, k6, NBomber)
   - Stress testing (peak load scenarios)
   - Endurance testing (memory leaks)
   - Benchmark tests (.NET BenchmarkDotNet)

3. **Security Tests**
   - OWASP ZAP automated scanning
   - SQL injection testing
   - XSS vulnerability scanning
   - Authentication/Authorization tests
   - Rate limiting validation

4. **Contract Tests**
   - API contract testing (Pact)
   - Schema validation
   - Backward compatibility tests

### Code Quality Improvements

1. **Static Analysis**
   - SonarQube integration
   - CodeQL security scanning
   - ReSharper code inspections
   - FxCop/Roslyn analyzers

2. **Package Vulnerabilities** (Current: 28 warnings)
   - Update `Azure.Identity` to latest (fix GHSA-m5vv-6r4h-3vj9, GHSA-wvxc-855f-jvrv)
   - Update `Microsoft.Extensions.Caching.Memory` to 9.0+ (fix GHSA-qj66-m88j-hmgj)
   - Update `Microsoft.Identity.Client` to latest (fix GHSA-x674-v45j-fwxw)
   - Update `Microsoft.IdentityModel.JsonWebTokens` to 7.0+ (fix GHSA-59j7-ghrg-fj52)
   - Update `System.IdentityModel.Tokens.Jwt` to 7.0+ (fix GHSA-59j7-ghrg-fj52)

3. **Nullable Reference Type Warnings** (Current: 15 warnings)
   - Fix CS8618 warnings in `DataSet.cs` (add `required` modifier or null-forgiving operator)
   - Fix CS8602 warnings (null dereference in controllers, services, tests)
   - Enable strict nullable checking project-wide

### Architecture Improvements

1. **Observability**
   - Add distributed tracing (OpenTelemetry)
   - Implement correlation IDs across requests
   - Add custom metrics (Prometheus)
   - Create Grafana dashboards

2. **Resilience**
   - Implement retry policies (Polly)
   - Add circuit breakers for external dependencies
   - Implement bulkhead isolation
   - Add request timeouts

3. **Performance**
   - Implement response caching (Redis)
   - Add CDN for static content
   - Database query optimization (missing indexes)
   - Implement pagination for large result sets

4. **Security**
   - Add API key authentication (in addition to JWT)
   - Implement refresh token rotation
   - Add request signing for sensitive operations
   - Implement audit logging

---

**Document Owner**: DevOps Team  
**Review Frequency**: Quarterly  
**Last Review**: February 14, 2026

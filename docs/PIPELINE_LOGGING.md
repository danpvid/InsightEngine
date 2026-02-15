# MediatR Pipeline Logging

## Overview

The InsightEngine uses **MediatR Pipeline Behaviors** to implement cross-cutting concerns such as validation, performance monitoring, and structured logging. This document describes the pipeline architecture and logging strategy.

## Architecture

### Pipeline Order

The MediatR pipeline processes requests in the following order:

```
Request → LoggingBehavior → PerformanceBehavior → ValidationBehavior → Handler → Response
             ↓                      ↓                      ↓
          Request/Response     Performance Tracking    FluentValidation
            Logging              & Slow Queries         + Domain Notifications
```

### Pipeline Behaviors

#### 1. LoggingBehavior

**Purpose**: Logs detailed request/response information for debugging.

**Location**: `src/InsightEngine.Domain/Behaviors/LoggingBehavior.cs`

**Features**:
- Logs request type and serialized payload (DEBUG level)
- Logs response type (DEBUG level)
- Limits JSON depth to 3 to prevent circular references
- Safe serialization with fallback for non-serializable objects

**Log Examples**:
```
[DEBUG] Handling UploadDataSetCommand - Request: {"FileName":"data.csv","ContentType":"text/csv"}
[DEBUG] Handled UploadDataSetCommand - Response type: Result<DataSetDto>
```

**Configuration**:
- Enable in **Development** environment only (sensitive data)
- Set `Logging:LogLevel:InsightEngine.Domain.Behaviors.LoggingBehavior` to `Debug`

---

#### 2. PerformanceBehavior

**Purpose**: Tracks execution time and identifies slow requests.

**Location**: `src/InsightEngine.Domain/Behaviors/PerformanceBehavior.cs`

**Features**:
- Measures total request execution time
- Logs slow requests (> 1000ms threshold) as **WARNING**
- Logs successful requests with timing as **INFORMATION**
- Logs failed requests with exception details as **ERROR**

**Log Examples**:
```
[DEBUG] Started executing GetDataSetProfileQuery
[INFO] Request GetDataSetProfileQuery completed successfully in 245ms
[WARN] Slow request detected: UploadDataSetCommand took 1523ms (threshold: 1000ms)
[ERROR] Request GetDataSetChartQuery failed after 89ms with exception: Invalid chart type
```

**Thresholds**:
- Default: 1000ms (1 second)
- Configurable via `SlowRequestThresholdMs` constant

---

#### 3. ValidationBehavior

**Purpose**: Validates all commands/queries using FluentValidation before handler execution.

**Location**: `src/InsightEngine.Domain/Behaviors/ValidationBehavior.cs`

**Features**:
- Integrates with **FluentValidation** validators
- Logs validation start with validator count (DEBUG)
- Logs validation failures with detailed errors (WARNING)
- Logs validation success with timing (DEBUG)
- Adds validation errors to **DomainNotificationHandler**
- Returns `Result.Failure` on validation errors

**Log Examples**:
```
[DEBUG] Validating UploadDataSetCommand with 1 validators
[DEBUG] No validators registered for GetAllDataSetsQuery, proceeding to handler
[WARN] Validation failed for GetDataSetChartQuery with 2 error(s) in 12ms: ChartType: Invalid chart type; DatasetId: Dataset not found
[DEBUG] Validation succeeded for UploadDataSetCommand in 8ms
```

**Integration**:
- Uses existing `IValidator<TRequest>` implementations
- Coordinates with `IDomainNotificationHandler`
- Returns strongly-typed `Result<T>` failures

---

## Structured Logging

All pipeline behaviors use **structured logging** with message templates and parameters:

```csharp
_logger.LogInformation(
    "Request {RequestName} completed in {ElapsedMs}ms",
    requestName,
    stopwatch.ElapsedMilliseconds);
```

### Benefits

1. **Machine-readable**: Log aggregation tools (ELK, Seq, Application Insights) can parse structured data
2. **Queryable**: Search by specific request names, execution times, error types
3. **Correlated**: All logs for a request share the same context
4. **Performant**: Only serializes parameters when needed

### Log Properties

Every pipeline log includes:
- `RequestName`: Type name of the command/query (e.g., `UploadDataSetCommand`)
- `ElapsedMs`: Execution time in milliseconds
- `ValidationErrors`: Detailed validation error messages (failures only)
- `ExceptionMessage`: Exception details (errors only)

---

## Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "InsightEngine.Domain.Behaviors": "Debug"
    }
  }
}
```

### appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "InsightEngine.Domain.Behaviors.LoggingBehavior": "Debug",
      "InsightEngine.Domain.Behaviors.PerformanceBehavior": "Debug",
      "InsightEngine.Domain.Behaviors.ValidationBehavior": "Debug"
    }
  }
}
```

### appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "InsightEngine.Domain.Behaviors.LoggingBehavior": "Information",
      "InsightEngine.Domain.Behaviors.PerformanceBehavior": "Information",
      "InsightEngine.Domain.Behaviors.ValidationBehavior": "Warning"
    }
  }
}
```

---

## Dependency Injection

Pipeline behaviors are registered in `NativeInjectorBootStrapper.cs`:

```csharp
// Pipeline execution order: Logging → Performance → Validation → Handler
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

**Important**: Order matters! MediatR executes behaviors in registration order.

---

## Performance Impact

### Overhead

- **LoggingBehavior**: ~1-5ms (JSON serialization)
- **PerformanceBehavior**: <1ms (stopwatch only)
- **ValidationBehavior**: 5-50ms (depends on validator complexity)

### Total Pipeline Overhead: ~10-100ms

This is acceptable for most scenarios. For high-throughput APIs, consider:
- Disable `LoggingBehavior` in production
- Increase `SlowRequestThresholdMs` to reduce log volume
- Use sampling (log 1 in 100 requests)

---

## Monitoring & Alerts

### Key Metrics to Monitor

1. **Slow Requests** (`PerformanceBehavior` warnings)
   - Alert if > 5% of requests exceed threshold
   - Investigate handler performance bottlenecks

2. **Validation Failures** (`ValidationBehavior` warnings)
   - Alert if validation failure rate > 10%
   - May indicate client integration issues

3. **Handler Exceptions** (`PerformanceBehavior` errors)
   - Alert on any unhandled exceptions
   - Critical system failures

### Sample Queries (Application Insights)

```kusto
// Slow requests in last 24h
traces
| where timestamp > ago(24h)
| where customDimensions.CategoryName == "InsightEngine.Domain.Behaviors.PerformanceBehavior"
| where severityLevel == 2 // Warning
| where message contains "Slow request"
| summarize count() by tostring(customDimensions.RequestName)
| order by count_ desc

// Validation failures by request type
traces
| where timestamp > ago(24h)
| where customDimensions.CategoryName == "InsightEngine.Domain.Behaviors.ValidationBehavior"
| where severityLevel == 2 // Warning
| summarize count() by tostring(customDimensions.RequestName)
| order by count_ desc
```

---

## Best Practices

### ✅ DO

- Use structured logging with named parameters
- Log at appropriate levels (DEBUG for trace, INFO for success, WARN for slow/validation failures, ERROR for exceptions)
- Include timing information for performance analysis
- Keep sensitive data out of logs (use `LoggingBehavior` in dev only)
- Monitor slow request warnings proactively

### ❌ DON'T

- Log PII (Personally Identifiable Information) in production
- Log every request payload (use sampling or dev-only)
- Use string interpolation in log messages (breaks structured logging)
- Ignore validation warnings (may indicate integration issues)
- Set all loggers to DEBUG in production (performance impact)

---

## Troubleshooting

### "No logs from pipeline behaviors"

**Cause**: Log level too high (e.g., `Warning` or `Error`)

**Solution**: Lower log level in `appsettings.json`:
```json
"InsightEngine.Domain.Behaviors": "Debug"
```

---

### "Validation logs missing"

**Cause**: No validators registered for command/query

**Solution**: 
1. Check if validator exists: `{CommandName}Validator.cs`
2. Verify validator is registered: `services.AddValidatorsFromAssembly(...)`
3. Check validator returns failures (not empty list)

---

### "Slow request warnings flooding logs"

**Cause**: Threshold too low or handler performance issue

**Solutions**:
1. Increase `SlowRequestThresholdMs` in `PerformanceBehavior.cs`
2. Optimize handler (add indexes, cache, etc.)
3. Use sampling: log 1 in N slow requests

---

## Examples

### Complete Request Flow Log

```
[DEBUG] 2025-01-15 10:23:45.123 [LoggingBehavior] Handling UploadDataSetCommand - Request: {"FileName":"sales.csv"}
[DEBUG] 2025-01-15 10:23:45.124 [PerformanceBehavior] Started executing UploadDataSetCommand
[DEBUG] 2025-01-15 10:23:45.125 [ValidationBehavior] Validating UploadDataSetCommand with 1 validators
[DEBUG] 2025-01-15 10:23:45.135 [ValidationBehavior] Validation succeeded for UploadDataSetCommand in 10ms
[INFO]  2025-01-15 10:23:45.823 [CsvProfiler] Profiled 10000 rows, 15 columns in 688ms
[INFO]  2025-01-15 10:23:45.824 [PerformanceBehavior] Request UploadDataSetCommand completed successfully in 700ms
[DEBUG] 2025-01-15 10:23:45.825 [LoggingBehavior] Handled UploadDataSetCommand - Response type: Result<DataSetDto>
```

### Validation Failure Log

```
[DEBUG] 2025-01-15 10:25:12.456 [ValidationBehavior] Validating GetDataSetChartQuery with 1 validators
[WARN]  2025-01-15 10:25:12.468 [ValidationBehavior] Validation failed for GetDataSetChartQuery with 2 error(s) in 12ms: ChartType: Must be one of: Bar, Line, Pie, Scatter, Histogram, BoxPlot, Heatmap; DatasetId: Dataset 'abc123' not found
[INFO]  2025-01-15 10:25:12.469 [PerformanceBehavior] Request GetDataSetChartQuery completed successfully in 13ms
```

### Slow Request + Exception Log

```
[DEBUG] 2025-01-15 10:30:00.001 [PerformanceBehavior] Started executing UploadDataSetCommand
[ERROR] 2025-01-15 10:30:01.850 [ChartExecutionService] Failed to execute Python script: FileNotFoundError: python.exe not found
[ERROR] 2025-01-15 10:30:01.851 [PerformanceBehavior] Request UploadDataSetCommand failed after 1850ms with exception: FileNotFoundError: python.exe not found
[WARN]  2025-01-15 10:30:01.851 [PerformanceBehavior] Slow request detected: UploadDataSetCommand took 1850ms (threshold: 1000ms)
```

---

## Related Documentation

- [API Documentation](./API.md) - REST API endpoints and contracts
- [Testing Guide](../samples/README.md) - Integration testing examples
- [Deployment Checklist](./RELEASE.md) - Production deployment steps

---

## Changelog

### Day 6 - Task 6.9 (2025-01-15)

- ✅ Enhanced `ValidationBehavior` with structured logging
- ✅ Created `PerformanceBehavior` for request timing
- ✅ Created `LoggingBehavior` for request/response debugging
- ✅ Registered all behaviors in DI container
- ✅ Documented pipeline architecture and best practices

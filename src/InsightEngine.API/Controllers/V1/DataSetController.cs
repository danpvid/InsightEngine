using InsightEngine.API.Models;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using CsvHelper;
using CsvHelper.Configuration;
using DuckDB.NET.Data;
using System.Globalization;

namespace InsightEngine.API.Controllers.V1;

/// <summary>
/// Controller para gerenciamento de datasets (arquivos CSV)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/datasets")]
[AllowAnonymous]
public class DataSetController : BaseController
{
    private readonly IDataSetApplicationService _dataSetApplicationService;
    private readonly IAIInsightService _aiInsightService;
    private readonly IDataSetCleanupService _dataSetCleanupService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<DataSetController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly InsightEngineSettings _runtimeSettings;

    public DataSetController(
        IDataSetApplicationService dataSetApplicationService,
        IAIInsightService aiInsightService,
        IDataSetCleanupService dataSetCleanupService,
        IFileStorageService fileStorageService,
        IDomainNotificationHandler notificationHandler,
        IMediator mediator,
        ILogger<DataSetController> logger,
        IWebHostEnvironment environment,
        IOptions<InsightEngineSettings> runtimeSettings)
        : base(notificationHandler, mediator)
    {
        _dataSetApplicationService = dataSetApplicationService;
        _aiInsightService = aiInsightService;
        _dataSetCleanupService = dataSetCleanupService;
        _fileStorageService = fileStorageService;
        _logger = logger;
        _environment = environment;
        _runtimeSettings = runtimeSettings.Value;
    }

    /// <summary>
    /// Upload de arquivo CSV (suporta arquivos grandes usando streaming)
    /// </summary>
    /// <param name="file">Arquivo CSV</param>
    /// <returns>Metadata do dataset criado</returns>
    /// <remarks>
    /// Exemplo de requisição:
    /// 
    ///     POST /api/v1/datasets
    ///     Content-Type: multipart/form-data
    ///     
    ///     file: [arquivo.csv]
    ///     
    /// Limite máximo: 20MB (configurável)
    /// 
    /// O arquivo é salvo como {datasetId}.csv para:
    /// - Evitar colisões de nomes
    /// - Mitigar ataques de path traversal
    /// - Garantir unicidade
    /// 
    /// Retorna HTTP 201 Created com:
    /// - datasetId: Identificador único do dataset
    /// - originalFileName: Nome original do arquivo
    /// - storedFileName: Nome do arquivo no storage ({datasetId}.csv)
    /// - sizeBytes: Tamanho do arquivo em bytes
    /// - createdAtUtc: Data/hora UTC do upload
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return ResponseResult(Result.Failure<object>("File is required."));
            }

            if (file.Length > _runtimeSettings.UploadMaxBytes)
            {
                var maxMb = Math.Round(_runtimeSettings.UploadMaxBytes / (1024d * 1024d), 2);
                return ErrorResponse(
                    StatusCodes.Status413PayloadTooLarge,
                    "payload_too_large",
                    $"File size exceeds the maximum allowed size of {maxMb}MB.",
                    "file");
            }

            var result = await _dataSetApplicationService.UploadAsync(file, _runtimeSettings.UploadMaxBytes);

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, result.Errors, "validation_error");
            }

            var response = new
            {
                success = true,
                message = "Arquivo enviado com sucesso.",
                data = new
                {
                    datasetId = result.Data.DatasetId,
                    originalFileName = result.Data.OriginalFileName,
                    storedFileName = result.Data.StoredFileName,
                    sizeBytes = result.Data.SizeBytes,
                    createdAtUtc = result.Data.CreatedAtUtc
                }
            };

            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Data.DatasetId, version = "1.0" },
                response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file upload");
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro interno ao processar o arquivo.");
        }
    }

    /// <summary>
    /// Lista todos os datasets
    /// </summary>
    /// <returns>Lista de datasets com informações resumidas</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var result = await _dataSetApplicationService.GetAllAsync();

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status500InternalServerError, result.Errors, "internal_error");
            }

            return Ok(new
            {
                success = true,
                data = result.Data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving datasets");
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao listar datasets.");
        }
    }

    /// <summary>
    /// Obtém informações de um dataset específico
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        // TODO: Implement GetByIdQuery when needed
        return ErrorResponse(StatusCodes.Status404NotFound, "not_found", "Dataset not found.");
    }

    /// <summary>
    /// Gera profile (análise) de um dataset CSV
    /// </summary>
    /// <param name="id">ID do dataset</param>
    /// <returns>Profile com schema inferido, estatísticas e amostras</returns>
    /// <remarks>
    /// Exemplo de resposta:
    /// 
    ///     {
    ///       "datasetId": "b8d3...e1",
    ///       "rowCount": 12450,
    ///       "columns": [
    ///         {
    ///           "name": "sale_date",
    ///           "inferredType": "Date",
    ///           "nullRate": 0.0,
    ///           "distinctCount": 365,
    ///           "topValues": ["2025-01-01", "2025-01-02", "2025-01-03"]
    ///         },
    ///         {
    ///           "name": "amount",
    ///           "inferredType": "Number",
    ///           "nullRate": 0.01,
    ///           "distinctCount": 9400,
    ///           "topValues": ["19.9", "29.9", "9.9"]
    ///         }
    ///       ]
    ///     }
    ///     
    /// Tipos inferidos:
    /// - Number: valores numéricos (90%+ parseia como decimal)
    /// - Date: datas (90%+ parseia como DateTime)
    /// - Boolean: booleanos (true/false, yes/no, 1/0, sim/não)
    /// - Category: baixa cardinalidade (≤ max(20, 5% das linhas))
    /// - String: texto genérico
    /// 
    /// Performance:
    /// - Amostra de até 5.000 linhas para inferência de tipo
    /// - Contagem total de linhas sem carregar tudo em memória
    /// </remarks>
    [HttpGet("{id:guid}/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProfile(Guid id)
    {
        try
        {
            var result = await _dataSetApplicationService.GetProfileAsync(id);

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            var profile = result.Data;

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = profile.DatasetId,
                    rowCount = profile.RowCount,
                    sampleSize = profile.SampleSize,
                    columns = profile.Columns.Select(c => new
                    {
                        name = c.Name,
                        inferredType = c.InferredType.ToString(),
                        nullRate = Math.Round(c.NullRate, 4),
                        distinctCount = c.DistinctCount,
                        topValues = c.TopValues
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating profile for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao gerar profile do dataset.");
        }
    }

    [HttpGet("{id:guid}/rows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRawRows(
        Guid id,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string[]? sort = null,
        [FromQuery] string[]? filters = null,
        [FromQuery] string? search = null)
    {
        var csvPath = _fileStorageService.GetFullPath($"{id}.csv");
        if (!System.IO.File.Exists(csvPath))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "not_found", $"Dataset not found: {id}");
        }

        var effectivePage = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(pageSize ?? 100, 1, _runtimeSettings.QueryResultMaxRows);

        try
        {
            var columns = await ReadCsvHeadersAsync(csvPath);
            if (columns.Length == 0)
            {
                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        datasetId = id,
                        columns,
                        rowCountTotal = 0,
                        rowCountReturned = 0,
                        page = effectivePage,
                        pageSize = effectivePageSize,
                        totalPages = 0,
                        truncated = false,
                        rows = Array.Empty<Dictionary<string, string?>>()
                    }
                });
            }

            var columnLookup = columns
                .ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);

            var errors = new List<string>();
            var rawFilters = ParseFilters(filters, errors);
            var parsedFilters = new List<ChartFilter>();
            foreach (var filter in rawFilters)
            {
                if (!columnLookup.ContainsKey(filter.Column))
                {
                    errors.Add($"Invalid filter column '{filter.Column}'.");
                    continue;
                }

                parsedFilters.Add(new ChartFilter
                {
                    Column = columnLookup[filter.Column],
                    Operator = filter.Operator,
                    Values = filter.Values
                });
            }

            var sortRules = ParseRawSort(sort, columnLookup, errors);
            if (errors.Count > 0)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, errors, "validation_error");
            }

            var escapedPath = csvPath.Replace("'", "''");
            var whereClause = BuildRawRowsWhereClause(parsedFilters, search, columns);
            var orderClause = BuildRawRowsOrderClause(sortRules, columns);
            var offset = (effectivePage - 1) * effectivePageSize;
            var selectColumns = string.Join(", ", columns.Select(c =>
                $"CAST({EscapeIdentifier(c)} AS VARCHAR) AS {EscapeIdentifier(c)}"));

            var countSql = $@"
SELECT COUNT(*)
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause};
";

            var dataSql = $@"
SELECT {selectColumns}
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause}
{orderClause}
LIMIT {effectivePageSize}
OFFSET {offset};
";

            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            long rowCountTotal;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = countSql;
                countCommand.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
                var countResult = countCommand.ExecuteScalar();
                rowCountTotal = Convert.ToInt64(countResult ?? 0);
            }

            var rows = new List<Dictionary<string, string?>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = dataSql;
                command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < columns.Length; i++)
                    {
                        row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                    }

                    rows.Add(row);
                }
            }

            var totalPages = rowCountTotal == 0
                ? 0
                : (int)Math.Ceiling(rowCountTotal / (double)effectivePageSize);

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = id,
                    columns,
                    rowCountTotal,
                    rowCountReturned = rows.Count,
                    page = effectivePage,
                    pageSize = effectivePageSize,
                    totalPages,
                    truncated = (offset + rows.Count) < rowCountTotal,
                    rows
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading raw rows for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao carregar os dados brutos do dataset.");
        }
    }

    [HttpGet("runtime-config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRuntimeConfig()
    {
        var traceId = GetTraceId();
        var payload = new RuntimeConfigResponse
        {
            UploadMaxBytes = _runtimeSettings.UploadMaxBytes,
            UploadMaxMb = Math.Round(_runtimeSettings.UploadMaxBytes / (1024d * 1024d), 2),
            ScatterMaxPoints = _runtimeSettings.ScatterMaxPoints,
            HistogramBinsMin = _runtimeSettings.HistogramBinsMin,
            HistogramBinsMax = _runtimeSettings.HistogramBinsMax,
            QueryResultMaxRows = _runtimeSettings.QueryResultMaxRows,
            CacheTtlSeconds = _runtimeSettings.CacheTtlSeconds,
            DefaultTimeoutSeconds = _runtimeSettings.DefaultTimeoutSeconds
        };

        return Ok(new ApiResponse<RuntimeConfigResponse>(payload, traceId));
    }

    [HttpPost("cleanup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CleanupExpired([FromQuery] int? retentionDays = null)
    {
        var effectiveRetentionDays = retentionDays ?? _runtimeSettings.RetentionDays;
        var result = await _dataSetCleanupService.CleanupExpiredAsync(effectiveRetentionDays);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Gera recomendações de gráficos inteligentes para um dataset
    /// </summary>
    /// <param name="id">ID do dataset</param>
    /// <returns>Lista de até 12 recomendações de gráficos com templates ECharts</returns>
    [HttpGet("{id:guid}/recommendations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRecommendations(Guid id)
    {
        try
        {
            var result = await _dataSetApplicationService.GetRecommendationsAsync(id);

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            return Ok(new
            {
                success = true,
                data = result.Data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating recommendations for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao gerar recomendações do dataset.");
        }
    }

    /// <summary>
    /// Executa uma recomendação de gráfico e retorna EChartsOption completo com dados reais
    /// </summary>
    /// <param name="id">ID do dataset</param>
    /// <param name="recommendationId">ID da recomendação (ex: rec_001)</param>
    /// <param name="aggregation">Opcional: Sobrescrever agregação (Sum, Avg, Count, Min, Max)</param>
    /// <param name="timeBin">Opcional: Sobrescrever período (Day, Week, Month, Quarter, Year)</param>
    /// <param name="yColumn">Opcional: Sobrescrever coluna Y</param>
    /// <returns>EChartsOption completo pronto para renderização</returns>
    /// <remarks>
    /// Exemplo de uso:
    /// 
    ///     GET /api/v1/datasets/{datasetId}/charts/rec_001
    ///     GET /api/v1/datasets/{datasetId}/charts/rec_001?aggregation=Avg&amp;timeBin=Week
    ///     
    /// Dia 4 MVP + Controles Dinâmicos:
    /// - Suporta gráficos Line, Bar, Scatter, Histogram
    /// - Suporta ECharts como biblioteca
    /// - Executa agregação via DuckDB
    /// - Permite sobrescrever agregação, período e métrica Y
    /// 
    /// Response envelope com telemetria:
    /// - datasetId: ID do dataset
    /// - recommendationId: ID da recomendação executada
    /// - option: EChartsOption completo (pronto para setOption)
    /// - executedQuery: QuerySpec resolvido
    /// - rowCountReturned: Número de pontos retornados
    /// </remarks>
    [HttpGet("{id:guid}/charts/{recommendationId}")]
    [ProducesResponseType(typeof(ApiResponse<Models.ChartExecutionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChart(
        Guid id, 
        string recommendationId,
        [FromQuery] string? aggregation = null,
        [FromQuery] string? timeBin = null,
        [FromQuery] string? yColumn = null,
        [FromQuery] string? metricY = null,
        [FromQuery] string? groupBy = null,
        [FromQuery] string[]? filters = null)
    {
        var resolvedMetricY = !string.IsNullOrWhiteSpace(metricY) ? metricY : yColumn;

        _logger.LogInformation(
            "GetChart called - DatasetId: {DatasetId}, RecommendationId: {RecommendationId}, Aggregation: {Aggregation}, TimeBin: {TimeBin}, YColumn: {YColumn}, GroupBy: {GroupBy}, Filters: {FilterCount}",
            id, recommendationId, aggregation ?? "null", timeBin ?? "null", resolvedMetricY ?? "null", groupBy ?? "null", filters?.Length ?? 0);

        try
        {
            var filterErrors = new List<string>();
            var parsedFilters = ParseFilters(filters, filterErrors);
            if (filterErrors.Count > 0)
            {
                return ResponseResult(Result.Failure<object>(filterErrors));
            }

            var result = await _dataSetApplicationService.GetChartAsync(
                id,
                recommendationId,
                aggregation,
                timeBin,
                resolvedMetricY,
                groupBy,
                parsedFilters);

            if (!result.IsSuccess)
            {
                return ResponseResult(Result.Failure<object>(result.Errors));
            }

            // Mapear Domain response para API response
            var domainResponse = result.Data!;
            var apiResponse = new Models.ChartExecutionResponse
            {
                DatasetId = domainResponse.DatasetId,
                RecommendationId = domainResponse.RecommendationId,
                Option = domainResponse.ExecutionResult.Option,
                InsightSummary = domainResponse.InsightSummary,
                Meta = new ChartExecutionMeta
                {
                    RowCountReturned = domainResponse.ExecutionResult.RowCount,
                    ExecutionMs = domainResponse.TotalExecutionMs,
                    DuckDbMs = domainResponse.ExecutionResult.DuckDbMs,
                    ChartType = domainResponse.ExecutionResult.Option.Series?.FirstOrDefault()?
                        .GetValueOrDefault("type")?.ToString() ?? "Line",
                    GeneratedAt = DateTime.UtcNow,
                    QueryHash = domainResponse.QueryHash,
                    CacheHit = domainResponse.CacheHit
                },
                DebugSql = _environment.IsDevelopment() ? domainResponse.ExecutionResult.GeneratedSql : null
            };

            return ResponseResult(Result.Success(apiResponse));
        }
        catch (Exception ex)
{
            _logger.LogError(ex, "Error executing chart {RecommendationId} for dataset {DatasetId}", 
                recommendationId, id);

            var traceId = GetTraceId();
            var errorResponse = ApiErrorResponse.FromMessage(
                "Erro interno ao executar gráfico. Verifique os logs para mais detalhes.",
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, errorResponse);
        }
    }

    [HttpPost("{id:guid}/charts/{recommendationId}/ai-summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateAiSummary(
        Guid id,
        string recommendationId,
        [FromBody] AiChartRequest? request)
    {
        request ??= new AiChartRequest();
        var language = ResolveLanguage();

        var filterErrors = new List<string>();
        var parsedFilters = ParseFilters(request.Filters.ToArray(), filterErrors);
        if (filterErrors.Count > 0)
        {
            return ResponseResult(Result.Failure<object>(filterErrors));
        }

        var result = await _aiInsightService.GenerateAiSummaryAsync(
            new LLMChartContextRequest
            {
                DatasetId = id,
                RecommendationId = recommendationId,
                Language = language,
                Aggregation = request.Aggregation,
                TimeBin = request.TimeBin,
                MetricY = request.MetricY,
                GroupBy = request.GroupBy,
                Filters = parsedFilters,
                ScenarioMeta = request.ScenarioMeta
            },
            HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            insightSummary = result.Data!.InsightSummary,
            meta = result.Data.Meta
        }));
    }

    [HttpPost("{id:guid}/charts/{recommendationId}/explain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExplainChart(
        Guid id,
        string recommendationId,
        [FromBody] AiChartRequest? request)
    {
        request ??= new AiChartRequest();
        var language = ResolveLanguage();

        var filterErrors = new List<string>();
        var parsedFilters = ParseFilters(request.Filters.ToArray(), filterErrors);
        if (filterErrors.Count > 0)
        {
            return ResponseResult(Result.Failure<object>(filterErrors));
        }

        var result = await _aiInsightService.ExplainChartAsync(
            new LLMChartContextRequest
            {
                DatasetId = id,
                RecommendationId = recommendationId,
                Language = language,
                Aggregation = request.Aggregation,
                TimeBin = request.TimeBin,
                MetricY = request.MetricY,
                GroupBy = request.GroupBy,
                Filters = parsedFilters,
                ScenarioMeta = request.ScenarioMeta
            },
            HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            explanation = result.Data!.Explanation.Explanation,
            keyTakeaways = result.Data.Explanation.KeyTakeaways,
            potentialCauses = result.Data.Explanation.PotentialCauses,
            caveats = result.Data.Explanation.Caveats,
            suggestedNextSteps = result.Data.Explanation.SuggestedNextSteps,
            questionsToAsk = result.Data.Explanation.QuestionsToAsk,
            meta = result.Data.Meta
        }));
    }

    [HttpPost("{id:guid}/ask")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AskDataset(
        Guid id,
        [FromBody] AskDatasetRequest? request)
    {
        request ??= new AskDatasetRequest();
        var language = ResolveLanguage();
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return ResponseResult(Result.Failure<object>("Question is required."));
        }

        var result = await _aiInsightService.AskAnalysisPlanAsync(
            new AskAnalysisPlanRequest
            {
                DatasetId = id,
                Language = language,
                Question = request.Question,
                CurrentView = request.CurrentView
            },
            HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            intent = result.Data!.Plan.Intent,
            suggestedChartType = result.Data.Plan.SuggestedChartType,
            proposedDimensions = result.Data.Plan.ProposedDimensions,
            suggestedFilters = result.Data.Plan.SuggestedFilters,
            reasoning = result.Data.Plan.Reasoning,
            meta = result.Data.Meta
        }));
    }

    [HttpPost("{id:guid}/simulate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Simulate(
        Guid id,
        [FromBody] ScenarioRequest request)
    {
        if (request == null)
        {
            return ResponseResult(Result.Failure<object>("Invalid simulation request body."));
        }

        var result = await _dataSetApplicationService.SimulateAsync(id, request);
        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        var simulation = result.Data!;

        return ResponseResult(Result.Success(new
        {
            datasetId = simulation.DatasetId,
            targetMetric = simulation.TargetMetric,
            targetDimension = simulation.TargetDimension,
            queryHash = simulation.QueryHash,
            rowCountReturned = simulation.RowCountReturned,
            duckDbMs = simulation.DuckDbMs,
            baselineSeries = simulation.BaselineSeries,
            simulatedSeries = simulation.SimulatedSeries,
            deltaSeries = simulation.DeltaSeries,
            deltaSummary = simulation.DeltaSummary,
            debugSql = _environment.IsDevelopment() ? simulation.GeneratedSql : null
        }));
    }

    private static List<ChartFilter> ParseFilters(string[]? filters, List<string> errors)
    {
        var parsed = new List<ChartFilter>();
        if (filters == null || filters.Length == 0)
        {
            return parsed;
        }

        if (filters.Length > 3)
        {
            errors.Add("No more than 3 filters are allowed.");
        }

        foreach (var raw in filters.Take(3))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var parts = raw.Split('|', 3, StringSplitOptions.None);
            if (parts.Length < 3)
            {
                errors.Add($"Invalid filter format '{raw}'. Use column|operator|value.");
                continue;
            }

            var column = parts[0].Trim();
            var opRaw = parts[1].Trim();
            var valueRaw = parts[2].Trim();

            if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(opRaw))
            {
                errors.Add($"Invalid filter '{raw}'. Column and operator are required.");
                continue;
            }

            if (!TryParseFilterOperator(opRaw, out var op))
            {
                errors.Add($"Invalid filter operator '{opRaw}'.");
                continue;
            }

            var values = valueRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .ToList();

            if (values.Count == 0)
            {
                errors.Add($"Filter '{raw}' must include at least one value.");
                continue;
            }

            if (op == FilterOperator.Between && values.Count != 2)
            {
                errors.Add($"Filter '{raw}' must provide exactly 2 values for 'between'.");
                continue;
            }

            if (op == FilterOperator.Contains && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for 'contains'.");
                continue;
            }

            if ((op == FilterOperator.Eq || op == FilterOperator.NotEq) && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for '{opRaw}'.");
                continue;
            }

            parsed.Add(new ChartFilter
            {
                Column = column,
                Operator = op,
                Values = values
            });
        }

        return parsed;
    }

    private string ResolveLanguage()
    {
        var languageFromQuery = HttpContext?.Request?.Query["lang"].ToString();
        if (string.IsNullOrWhiteSpace(languageFromQuery))
        {
            return "pt-br";
        }

        var normalized = languageFromQuery.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pt" => "pt-br",
            "pt-br" => "pt-br",
            "en" => "en",
            "en-us" => "en",
            _ => "pt-br"
        };
    }

    private static List<RawSortRule> ParseRawSort(
        string[]? sort,
        IReadOnlyDictionary<string, string> columnLookup,
        List<string> errors)
    {
        var result = new List<RawSortRule>();
        if (sort == null || sort.Length == 0)
        {
            return result;
        }

        foreach (var raw in sort.Take(3))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
            {
                continue;
            }

            var column = parts[0].Trim();
            if (!columnLookup.TryGetValue(column, out var resolvedColumn))
            {
                errors.Add($"Invalid sort column '{column}'.");
                continue;
            }

            var directionRaw = parts.Length > 1 ? parts[1].Trim() : "asc";
            var descending = string.Equals(directionRaw, "desc", StringComparison.OrdinalIgnoreCase);
            if (!descending && !string.Equals(directionRaw, "asc", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Invalid sort direction '{directionRaw}' for column '{column}'.");
                continue;
            }

            result.Add(new RawSortRule(resolvedColumn, descending));
        }

        return result;
    }

    private static string BuildRawRowsWhereClause(
        IReadOnlyCollection<ChartFilter> filters,
        string? search,
        IReadOnlyCollection<string> columns)
    {
        var expressions = new List<string>();

        foreach (var filter in filters)
        {
            var expression = BuildRawFilterExpression(filter);
            if (!string.IsNullOrWhiteSpace(expression))
            {
                expressions.Add(expression);
            }
        }

        var trimmedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch) && columns.Count > 0)
        {
            var searchLiteral = ToSqlLiteral($"%{trimmedSearch}%");
            var searchExpressions = columns
                .Select(column => $"CAST({EscapeIdentifier(column)} AS VARCHAR) ILIKE {searchLiteral}")
                .ToList();

            expressions.Add($"({string.Join(" OR ", searchExpressions)})");
        }

        return expressions.Count > 0
            ? $"\nWHERE {string.Join(" AND ", expressions)}"
            : string.Empty;
    }

    private static string BuildRawRowsOrderClause(
        IReadOnlyCollection<RawSortRule> sortRules,
        IReadOnlyList<string> columns)
    {
        if (sortRules.Count == 0)
        {
            if (columns.Count == 0)
            {
                return string.Empty;
            }

            return $"\nORDER BY {EscapeIdentifier(columns[0])} ASC";
        }

        var segments = sortRules
            .Select(rule => $"{EscapeIdentifier(rule.Column)} {(rule.Descending ? "DESC" : "ASC")}")
            .ToList();

        return $"\nORDER BY {string.Join(", ", segments)}";
    }

    private static string BuildRawFilterExpression(ChartFilter filter)
    {
        var columnExpr = $"CAST({EscapeIdentifier(filter.Column)} AS VARCHAR)";
        return filter.Operator switch
        {
            FilterOperator.Eq => BuildRawComparisonExpression(columnExpr, "=", filter.Values),
            FilterOperator.NotEq => BuildRawComparisonExpression(columnExpr, "<>", filter.Values),
            FilterOperator.In => BuildRawInExpression(columnExpr, filter.Values),
            FilterOperator.Between => BuildRawBetweenExpression(columnExpr, filter.Values),
            FilterOperator.Contains => $"{columnExpr} ILIKE {ToSqlLiteral($"%{filter.Values[0]}%")}",
            _ => string.Empty
        };
    }

    private static string BuildRawComparisonExpression(string columnExpr, string op, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (TryParseNumericValues(values, out var numericValues))
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            return $"{numericExpr} {op} {numericValues[0].ToString(CultureInfo.InvariantCulture)}";
        }

        return $"{columnExpr} {op} {ToSqlLiteral(values[0])}";
    }

    private static string BuildRawInExpression(string columnExpr, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (TryParseNumericValues(values, out var numericValues))
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            var numericList = string.Join(", ", numericValues.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            return $"{numericExpr} IN ({numericList})";
        }

        var literalList = string.Join(", ", values.Select(ToSqlLiteral));
        return $"{columnExpr} IN ({literalList})";
    }

    private static string BuildRawBetweenExpression(string columnExpr, IReadOnlyList<string> values)
    {
        if (values.Count < 2)
        {
            return string.Empty;
        }

        if (TryParseNumericValues(values, out var numericValues) && numericValues.Count >= 2)
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            return $"{numericExpr} BETWEEN {numericValues[0].ToString(CultureInfo.InvariantCulture)} AND {numericValues[1].ToString(CultureInfo.InvariantCulture)}";
        }

        return $"{columnExpr} BETWEEN {ToSqlLiteral(values[0])} AND {ToSqlLiteral(values[1])}";
    }

    private static bool TryParseNumericValues(IReadOnlyList<string> values, out List<double> numbers)
    {
        numbers = new List<double>();

        foreach (var value in values)
        {
            var normalized = value.Replace(",", ".", StringComparison.Ordinal);
            if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                numbers.Clear();
                return false;
            }

            numbers.Add(parsed);
        }

        return numbers.Count > 0;
    }

    private static async Task<string[]> ReadCsvHeadersAsync(string csvPath)
    {
        await using var stream = System.IO.File.OpenRead(csvPath);
        using var reader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return Array.Empty<string>();
        }

        csv.ReadHeader();
        return csv.HeaderRecord ?? Array.Empty<string>();
    }

    private static string EscapeIdentifier(string identifier)
    {
        var escaped = identifier.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string ToSqlLiteral(string value)
    {
        var escaped = value.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    private static bool TryParseFilterOperator(string input, out FilterOperator op)
    {
        return Enum.TryParse(input, true, out op);
    }

    private readonly record struct RawSortRule(string Column, bool Descending);
}

using InsightEngine.API.Models;
using InsightEngine.Application.Models.DataSet;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Formulas;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Queries.DataSet;
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
    private const string RawRowControlColumn = "__row_control_id";
    private readonly IDataSetApplicationService _dataSetApplicationService;
    private readonly IDataSetCleanupService _dataSetCleanupService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IIndexStore _indexStore;
    private readonly IFormulaInferenceEngine _formulaInferenceEngine;
    private readonly ILogger<DataSetController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly InsightEngineSettings _runtimeSettings;
    private readonly FormulaInferenceSettings _formulaInferenceSettings;
    private readonly InsightEngineFeatures _features;
    private const int RawRangeDistinctThreshold = 20;
    private int RawTopValuesLimit => Math.Clamp(_runtimeSettings.RawTopValuesLimit, 3, 50);
    private int RawTopRangesLimit => Math.Clamp(_runtimeSettings.RawTopRangesLimit, 3, 20);
    private int RawRangeBinCount => Math.Clamp(_runtimeSettings.RawRangeBinCount, 4, 20);

    public DataSetController(
        IDataSetApplicationService dataSetApplicationService,
        IDataSetCleanupService dataSetCleanupService,
        IFileStorageService fileStorageService,
        IDataSetSchemaStore schemaStore,
        IIndexStore indexStore,
        IFormulaInferenceEngine formulaInferenceEngine,
        IDomainNotificationHandler notificationHandler,
        IMediator mediator,
        ILogger<DataSetController> logger,
        IWebHostEnvironment environment,
        IOptions<InsightEngineSettings> runtimeSettings,
        IOptions<FormulaInferenceSettings> formulaInferenceSettings,
        InsightEngineFeatures features)
        : base(notificationHandler, mediator)
    {
        _dataSetApplicationService = dataSetApplicationService;
        _dataSetCleanupService = dataSetCleanupService;
        _fileStorageService = fileStorageService;
        _schemaStore = schemaStore;
        _indexStore = indexStore;
        _formulaInferenceEngine = formulaInferenceEngine;
        _logger = logger;
        _environment = environment;
        _runtimeSettings = runtimeSettings.Value;
        _formulaInferenceSettings = formulaInferenceSettings.Value;
        _features = features;
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
    /// Remove permanentemente um dataset e todos os artefatos relacionados.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var deletion = await _dataSetCleanupService.DeleteDatasetAsync(id);
            if (deletion is null)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, "not_found", $"Dataset not found: {id}");
            }

            if (!deletion.RemovedMetadataRecord)
            {
                return ErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "internal_error",
                    $"Failed to remove dataset metadata for: {id}");
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = deletion.DatasetId,
                    removedMetadataRecord = deletion.RemovedMetadataRecord,
                    deletedFile = deletion.DeletedFile,
                    deletedLegacyArtifacts = deletion.DeletedLegacyArtifacts,
                    clearedMetadataCache = deletion.ClearedMetadataCache,
                    clearedChartCache = deletion.ClearedChartCache
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao remover dataset.");
        }
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
            var index = await _indexStore.LoadAsync(id);
            var formulaInferenceSummary = BuildFormulaInferenceSummary(index?.FormulaInference?.Result);

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = profile.DatasetId,
                    rowCount = profile.RowCount,
                    sampleSize = profile.SampleSize,
                    targetColumn = profile.TargetColumn,
                    ignoredColumns = profile.IgnoredColumns,
                    schemaConfirmed = profile.SchemaConfirmed,
                    columns = profile.Columns.Select(c => new
                    {
                        name = c.Name,
                        inferredType = c.InferredType.ToString(),
                        confirmedType = (c.ConfirmedType ?? c.InferredType).NormalizeLegacy().ToString(),
                        isIgnored = c.IsIgnored,
                        isTarget = c.IsTarget,
                        currencyCode = c.CurrencyCode,
                        hasPercentSign = c.HasPercentSign,
                        percentageScaleHint = c.PercentageScaleHint?.ToString(),
                        nullRate = Math.Round(c.NullRate, 4),
                        distinctCount = c.DistinctCount,
                        topValues = c.TopValues,
                        topValueStats = c.TopValueStats.Select(item => new
                        {
                            value = item.Value,
                            count = item.Count
                        })
                    }),
                    indexGenerated = index is not null,
                    targetFormulaSuggestion = index?.TargetFormulaSuggestion,
                    formulaInferenceSummary
                },
                indexGenerated = index is not null,
                targetFormulaSuggestion = index?.TargetFormulaSuggestion,
                formulaInferenceSummary
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

    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetImportPreview(Guid id, [FromQuery] int sampleSize = 200)
    {
        try
        {
            var result = await _dataSetApplicationService.GetImportPreviewAsync(id, sampleSize);

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            var preview = result.Data;
            return Ok(new
            {
                success = true,
                data = new
                {
                    tempUploadId = preview.TempUploadId,
                    sampleSize = preview.SampleSize,
                    columns = preview.Columns.Select(column => new
                    {
                        name = column.Name,
                        inferredType = column.InferredType.ToString(),
                        confidence = column.Confidence,
                        reasons = column.Reasons,
                        hints = new
                        {
                            hasPercentSign = column.Hints.HasPercentSign,
                            hasCurrencySymbol = column.Hints.HasCurrencySymbol,
                            mostlyZeroToOne = column.Hints.MostlyZeroToOne,
                            mostlyZeroToHundred = column.Hints.MostlyZeroToHundred,
                            mostlyInteger = column.Hints.MostlyInteger,
                            consistentTwoDecimalPlaces = column.Hints.ConsistentTwoDecimalPlaces,
                            percentageScaleHint = column.Hints.PercentageScaleHint?.ToString(),
                            currencyCode = column.Hints.CurrencyCode
                        }
                    }),
                    sampleRows = preview.SampleRows,
                    suggestedTargetCandidates = preview.SuggestedTargetCandidates,
                    suggestedIgnoredCandidates = preview.SuggestedIgnoredCandidates,
                    suggestedUniqueKeyCandidates = preview.SuggestedUniqueKeyCandidates
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating import preview for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao gerar preview de importação do dataset.");
        }
    }

    [HttpPost("{id:guid}/index:build")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BuildIndex(Guid id, [FromBody] BuildIndexRequest? request)
    {
        try
        {
            var effectiveRequest = request ?? new BuildIndexRequest();
            var result = await _dataSetApplicationService.BuildIndexAsync(id, effectiveRequest);

            if (!result.IsSuccess)
            {
                var statusCode = result.Errors.Any(error => error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;

                return ErrorResponse(statusCode, result.Errors, statusCode == StatusCodes.Status404NotFound ? "not_found" : "validation_error");
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = result.Data!.DatasetId,
                    status = result.Data.Status,
                    builtAt = result.Data.BuiltAtUtc,
                    limitsUsed = result.Data.LimitsUsed
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building dataset index for {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao construir índice de metadados.");
        }
    }

    [HttpGet("{id:guid}/schema")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSchema(Guid id)
    {
        try
        {
            var result = await _dataSetApplicationService.GetSchemaAsync(id);
            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            var schema = result.Data;
            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = schema.DatasetId,
                    schemaVersion = schema.SchemaVersion,
                    schemaConfirmed = schema.SchemaConfirmed,
                    targetColumn = schema.TargetColumn,
                    uniqueKeyColumn = schema.UniqueKeyColumn,
                    currencyCode = schema.CurrencyCode,
                    finalizedAtUtc = schema.FinalizedAtUtc,
                    ignoredColumnsCount = schema.Columns.Count(column => column.IsIgnored),
                    columns = schema.Columns.Select(column => new
                    {
                        name = column.Name,
                        inferredType = column.InferredType.NormalizeLegacy().ToString(),
                        confirmedType = column.ConfirmedType.NormalizeLegacy().ToString(),
                        isIgnored = column.IsIgnored,
                        isTarget = column.IsTarget,
                        currencyCode = column.CurrencyCode,
                        hasPercentSign = column.HasPercentSign,
                        percentageScaleHint = column.PercentageScaleHint?.ToString()
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading schema for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao carregar schema do dataset.");
        }
    }

    [HttpPost("{id:guid}/finalize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FinalizeImport(Guid id, [FromBody] FinalizeImportRequest? request)
    {
        try
        {
            var finalizeRequest = request ?? new FinalizeImportRequest();
            var result = await _dataSetApplicationService.FinalizeImportAsync(id, finalizeRequest);
            if (!result.IsSuccess)
            {
                var notFound = result.Errors.Any(error =>
                    error.Contains("not found", StringComparison.OrdinalIgnoreCase));
                var statusCode = notFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                var code = notFound ? "not_found" : "validation_error";
                return ErrorResponse(statusCode, result.Errors, code);
            }

            var withIndexMode = string.Equals(finalizeRequest.ImportMode, "with-index", StringComparison.OrdinalIgnoreCase)
                || _features.ImportFinalizeWithIndexByDefault;

            object? indexBuild = null;
            if (withIndexMode)
            {
                var indexBuildResult = await _dataSetApplicationService.BuildIndexAsync(
                    id,
                    new BuildIndexRequest(),
                    HttpContext.RequestAborted);

                if (!indexBuildResult.IsSuccess)
                {
                    return ErrorResponse(StatusCodes.Status400BadRequest, indexBuildResult.Errors, "validation_error");
                }

                indexBuild = new
                {
                    triggered = true,
                    status = (indexBuildResult.Data?.Status ?? IndexBuildState.Ready).ToString().ToLowerInvariant(),
                    builtAtUtc = indexBuildResult.Data?.BuiltAtUtc,
                    limitsUsed = indexBuildResult.Data?.LimitsUsed
                };
            }

            object? formulaInference = null;
            if (finalizeRequest.FormulaInference?.Enabled == true)
            {
                formulaInference = await RunFinalizeFormulaInferenceAsync(
                    id,
                    result.Data!.TargetColumn,
                    finalizeRequest.FormulaInference,
                    HttpContext.RequestAborted);
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = result.Data!.DatasetId,
                    schemaVersion = result.Data.SchemaVersion,
                    targetColumn = result.Data.TargetColumn,
                    uniqueKeyColumn = result.Data.UniqueKeyColumn,
                    ignoredColumnsCount = result.Data.IgnoredColumnsCount,
                    storedColumnsCount = result.Data.StoredColumnsCount,
                    currencyCode = result.Data.CurrencyCode,
                    importMode = withIndexMode ? "with-index" : "standard",
                    indexBuild,
                    formulaInference
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing import for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao finalizar importação do dataset.");
        }
    }

    [HttpGet("{id:guid}/index")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetIndex(Guid id)
    {
        try
        {
            var result = await _dataSetApplicationService.GetIndexAsync(id);
            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            return Ok(new
            {
                success = true,
                data = result.Data,
                indexGenerated = true,
                targetFormulaSuggestion = result.Data?.TargetFormulaSuggestion,
                formulaInferenceSummary = BuildFormulaInferenceSummary(result.Data?.FormulaInference?.Result)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dataset index for {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao carregar índice de metadados.");
        }
    }

    [HttpGet("{id:guid}/index/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetIndexStatus(Guid id)
    {
        try
        {
            var result = await _dataSetApplicationService.GetIndexStatusAsync(id);
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
            _logger.LogError(ex, "Error retrieving dataset index status for {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao carregar status do índice de metadados.");
        }
    }

    /// <summary>
    /// Discovers best-fit formulas for a numeric target column.
    /// </summary>
    /// <remarks>
    /// This endpoint returns ranked best-fit equations and quality metrics.
    /// It does not guarantee the true business logic and should be used as an analytical signal.
    /// </remarks>
    [HttpGet("{id:guid}/formulas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFormulaDiscovery(
        Guid id,
        [FromQuery] string? target = null,
        [FromQuery] int topK = 10,
        [FromQuery] bool interactions = true,
        [FromQuery] bool ratios = false,
        [FromQuery] bool force = false,
        [FromQuery] int maxCandidates = 3,
        [FromQuery] int sampleCap = 50000)
    {
        try
        {
            var request = new FormulaDiscoveryRequest
            {
                Target = target,
                TopKFeatures = topK,
                EnableInteractions = interactions,
                EnableRatios = ratios,
                Force = force,
                MaxCandidates = maxCandidates,
                SampleCap = sampleCap
            };

            var result = await _dataSetApplicationService.GetFormulaDiscoveryAsync(id, request);
            if (!result.IsSuccess)
            {
                var errors = result.Errors ?? new List<string>();
                var hasNotFound = errors.Any(error => error.Contains("not found", StringComparison.OrdinalIgnoreCase));
                var statusCode = hasNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                return ErrorResponse(statusCode, errors, hasNotFound ? "not_found" : "validation_error");
            }

            return Ok(new
            {
                success = true,
                data = result.Data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering formulas for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Failed to discover formulas for this dataset.");
        }
    }

    [HttpPost("{id:guid}/formula-inference/run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RunFormulaInference(Guid id, [FromBody] FormulaInferenceRunRequest request)
    {
        try
        {
            if (request is null)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "Invalid request body.");
            }

            var schema = await _schemaStore.LoadAsync(id);
            var index = await _indexStore.LoadAsync(id);
            if (schema is null && index is null)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, "not_found", "Dataset metadata not found.");
            }

            var knownColumns = BuildKnownColumns(schema, index);
            if (knownColumns.Count == 0)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "No columns available for formula inference.");
            }

            var targetColumn = request.TargetColumn?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetColumn))
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "targetColumn is required.");
            }

            if (!knownColumns.TryGetValue(targetColumn, out var targetInfo))
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", $"Target column '{targetColumn}' was not found.");
            }

            if (targetInfo.IsIgnored)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "Target column cannot be ignored.");
            }

            if (!targetInfo.Type.IsNumericLike())
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "Target column must be numeric.");
            }

            var normalizedMode = (request.Mode ?? "Auto").Trim();
            if (!normalizedMode.Equals("Manual", StringComparison.OrdinalIgnoreCase)
                && !normalizedMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "mode must be 'Auto' or 'Manual'.");
            }

            if (index is null)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, "not_found", "Dataset index not found.");
            }

            if (normalizedMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            {
                var expression = request.ManualExpression?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(expression))
                {
                    return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "manualExpression is required in Manual mode.");
                }

                var ignoredColumns = knownColumns
                    .Where(item => item.Value.IsIgnored)
                    .Select(item => item.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var parseResult = TryParseManualExpression(expression, knownColumns.Keys, ignoredColumns);
                if (!parseResult.IsValid)
                {
                    return ErrorResponse(StatusCodes.Status400BadRequest, parseResult.Errors.ToList(), "validation_error");
                }

                var selected = new FormulaExpression
                {
                    ExpressionText = expression,
                    TargetColumn = targetInfo.Name,
                    UsedColumns = parseResult.UsedColumns.ToArray(),
                    Depth = parseResult.Depth,
                    OperatorsUsed = parseResult.OperatorsUsed.ToArray(),
                    EpsilonMaxAbsError = 0,
                    SampleRowsTested = 0,
                    RowsFailed = 0,
                    Confidence = FormulaConfidence.Medium,
                    Notes = "manual formula"
                };

                index.SelectedFormula = new SelectedFormulaIndexEntry
                {
                    SelectedAtUtc = DateTimeOffset.UtcNow,
                    Source = "Manual",
                    Formula = selected
                };

                await _indexStore.SaveAsync(index);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        mode = "Manual",
                        targetColumn = targetInfo.Name,
                        selectedFormula = index.SelectedFormula,
                        formulaInference = index.FormulaInference?.Result
                    }
                });
            }

            var includePercentageColumns = request.Options?.IncludePercentageColumns ?? false;
            var candidateColumns = knownColumns.Values
                .Where(column => !column.IsIgnored)
                .Where(column => !string.Equals(column.Name, targetInfo.Name, StringComparison.OrdinalIgnoreCase))
                .Where(column => column.Type.IsNumericLike())
                .Where(column => includePercentageColumns || column.Type.NormalizeLegacy() != InferredType.Percentage)
                .Select(column => column.Name)
                .ToArray();

            var settingsOverride = new FormulaInferenceSettings
            {
                EnabledDefault = _formulaInferenceSettings.EnabledDefault,
                MaxColumns = request.Options?.MaxColumns ?? _formulaInferenceSettings.MaxColumns,
                MaxDepth = request.Options?.MaxDepth ?? _formulaInferenceSettings.MaxDepth,
                MaxCandidatesReturned = _formulaInferenceSettings.MaxCandidatesReturned,
                SearchBudgetMs = _formulaInferenceSettings.SearchBudgetMs,
                InitialSampleRows = _formulaInferenceSettings.InitialSampleRows,
                ValidationSampleRows = _formulaInferenceSettings.ValidationSampleRows,
                EpsilonAbs = request.Options?.EpsilonAbs ?? _formulaInferenceSettings.EpsilonAbs,
                EpsilonAbsRelaxed = _formulaInferenceSettings.EpsilonAbsRelaxed,
                DivisionZeroEpsilon = _formulaInferenceSettings.DivisionZeroEpsilon,
                AllowConstants = false,
                AllowColumnReuse = _formulaInferenceSettings.AllowColumnReuse,
                BeamWidth = _formulaInferenceSettings.BeamWidth
            };

            var inference = await _formulaInferenceEngine.InferAsync(
                id,
                targetInfo.Name,
                candidateColumns,
                settingsOverride,
                HttpContext.RequestAborted);

            index = await _indexStore.LoadAsync(id);

            return Ok(new
            {
                success = true,
                data = new
                {
                    mode = "Auto",
                    targetColumn = targetInfo.Name,
                    result = inference,
                    selectedFormula = index?.SelectedFormula,
                    formulaInference = index?.FormulaInference?.Result ?? inference
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running formula inference for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Failed to run formula inference.");
        }
    }

    [HttpGet("{id:guid}/formula-inference")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFormulaInference(Guid id)
    {
        try
        {
            var index = await _indexStore.LoadAsync(id);
            if (index is null)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, "not_found", "Dataset index not found.");
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    formulaInference = index.FormulaInference?.Result,
                    selectedFormula = index.SelectedFormula
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving formula inference for dataset {DatasetId}", id);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Failed to retrieve formula inference metadata.");
        }
    }

    [HttpGet("{id:guid}/facets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFacets(
        Guid id,
        [FromQuery] string field,
        [FromQuery] int? top = null,
        [FromQuery(Name = "filter")] string[]? filter = null,
        [FromQuery] string[]? filters = null)
    {
        var csvPath = _fileStorageService.GetFullPath($"{id}.csv");
        if (!System.IO.File.Exists(csvPath))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "not_found", $"Dataset not found: {id}");
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "validation_error", "Query parameter 'field' is required.");
        }

        var effectiveTop = Math.Clamp(top ?? 20, 1, 100);
        var allFilters = (filter ?? Array.Empty<string>())
            .Concat(filters ?? Array.Empty<string>())
            .ToArray();

        try
        {
            var columns = await ReadCsvHeadersAsync(csvPath);
            var columnLookup = columns.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);

            if (!columnLookup.TryGetValue(field, out var resolvedField))
            {
                return ErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "validation_error",
                    $"Invalid facets field '{field}'.");
            }

            var errors = new List<string>();
            var rawFilters = ParseFilters(allFilters, errors);
            var parsedFilters = new List<ChartFilter>();
            foreach (var facetFilter in rawFilters)
            {
                if (!columnLookup.ContainsKey(facetFilter.Column))
                {
                    errors.Add($"Invalid filter column '{facetFilter.Column}'.");
                    continue;
                }

                parsedFilters.Add(new ChartFilter
                {
                    Column = columnLookup[facetFilter.Column],
                    Operator = facetFilter.Operator,
                    Values = facetFilter.Values,
                    LogicalOperator = facetFilter.LogicalOperator
                });
            }

            if (errors.Count > 0)
            {
                return ErrorResponse(StatusCodes.Status400BadRequest, errors, "validation_error");
            }

            var escapedPath = csvPath.Replace("'", "''");
            var whereClause = BuildRawRowsWhereClause(parsedFilters, null, columns);
            var escapedField = EscapeIdentifier(resolvedField);
            var fieldExpr = $"CAST({escapedField} AS VARCHAR)";

            var facetsSql = $@"
SELECT
    CASE
        WHEN COALESCE(TRIM({fieldExpr}), '') = '' THEN '(null)'
        ELSE {fieldExpr}
    END AS facet_value,
    COUNT(*) AS frequency
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause}
GROUP BY 1
ORDER BY frequency DESC, facet_value ASC
LIMIT {effectiveTop};
";

            var countSql = $@"
SELECT COUNT(*)
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause};
";

            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            long filteredRowCount;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = countSql;
                countCommand.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
                var countResult = countCommand.ExecuteScalar();
                filteredRowCount = Convert.ToInt64(countResult ?? 0);
            }

            var values = new List<RawFacetStat>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = facetsSql;
                command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var value = reader.IsDBNull(0) ? "(null)" : reader.GetValue(0)?.ToString() ?? "(null)";
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1) ?? 0);
                    values.Add(new RawFacetStat(value, count));
                }
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    datasetId = id,
                    field = resolvedField,
                    top = effectiveTop,
                    filteredRowCount,
                    values = values.Select(item => new
                    {
                        value = item.Value,
                        count = item.Count
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading facets for dataset {DatasetId} and field {Field}", id, field);
            return ErrorResponse(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Erro ao carregar facetas do dataset.");
        }
    }

    private async Task<object> RunFinalizeFormulaInferenceAsync(
        Guid datasetId,
        string? targetColumn,
        FinalizeImportFormulaInferenceOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetColumn))
            {
                return new
                {
                    triggered = true,
                    status = "skipped",
                    reason = "target_not_defined"
                };
            }

            var schema = await _schemaStore.LoadAsync(datasetId, cancellationToken);
            var index = await _indexStore.LoadAsync(datasetId, cancellationToken);

            if (index is null)
            {
                return new
                {
                    triggered = true,
                    status = "skipped",
                    reason = "index_not_found"
                };
            }

            var knownColumns = BuildKnownColumns(schema, index);
            if (!knownColumns.TryGetValue(targetColumn, out var targetInfo)
                || targetInfo.IsIgnored
                || !targetInfo.Type.IsNumericLike())
            {
                return new
                {
                    triggered = true,
                    status = "skipped",
                    reason = "invalid_target"
                };
            }

            var includePercentageColumns = options.IncludePercentageColumns ?? false;
            var candidateColumns = knownColumns.Values
                .Where(column => !column.IsIgnored)
                .Where(column => !string.Equals(column.Name, targetInfo.Name, StringComparison.OrdinalIgnoreCase))
                .Where(column => column.Type.IsNumericLike())
                .Where(column => includePercentageColumns || column.Type.NormalizeLegacy() != InferredType.Percentage)
                .Select(column => column.Name)
                .ToArray();

            if (candidateColumns.Length == 0)
            {
                return new
                {
                    triggered = true,
                    status = "skipped",
                    reason = "no_candidate_columns"
                };
            }

            var settingsOverride = new FormulaInferenceSettings
            {
                EnabledDefault = _formulaInferenceSettings.EnabledDefault,
                MaxColumns = options.MaxColumns ?? _formulaInferenceSettings.MaxColumns,
                MaxDepth = options.MaxDepth ?? _formulaInferenceSettings.MaxDepth,
                MaxCandidatesReturned = _formulaInferenceSettings.MaxCandidatesReturned,
                SearchBudgetMs = _formulaInferenceSettings.SearchBudgetMs,
                InitialSampleRows = _formulaInferenceSettings.InitialSampleRows,
                ValidationSampleRows = _formulaInferenceSettings.ValidationSampleRows,
                EpsilonAbs = options.EpsilonAbs ?? _formulaInferenceSettings.EpsilonAbs,
                EpsilonAbsRelaxed = _formulaInferenceSettings.EpsilonAbsRelaxed,
                DivisionZeroEpsilon = _formulaInferenceSettings.DivisionZeroEpsilon,
                AllowConstants = false,
                AllowColumnReuse = _formulaInferenceSettings.AllowColumnReuse,
                BeamWidth = _formulaInferenceSettings.BeamWidth
            };

            var inference = await _formulaInferenceEngine.InferAsync(
                datasetId,
                targetInfo.Name,
                candidateColumns,
                settingsOverride,
                cancellationToken);

            return new
            {
                triggered = true,
                status = "completed",
                result = inference
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formula inference from finalize failed for dataset {DatasetId}", datasetId);
            return new
            {
                triggered = true,
                status = "failed",
                reason = "execution_error"
            };
        }
    }

    private static Dictionary<string, FormulaColumnInfo> BuildKnownColumns(DatasetImportSchema? schema, DatasetIndex? index)
    {
        var result = new Dictionary<string, FormulaColumnInfo>(StringComparer.OrdinalIgnoreCase);

        if (schema is not null)
        {
            foreach (var column in schema.Columns)
            {
                result[column.Name] = new FormulaColumnInfo(
                    column.Name,
                    column.ConfirmedType.NormalizeLegacy(),
                    column.IsIgnored);
            }
        }

        if (index is not null)
        {
            foreach (var column in index.Columns)
            {
                if (!result.ContainsKey(column.Name))
                {
                    result[column.Name] = new FormulaColumnInfo(
                        column.Name,
                        column.InferredType.NormalizeLegacy(),
                        false);
                }
            }
        }

        return result;
    }

    private static ManualFormulaParseResult TryParseManualExpression(
        string expression,
        IEnumerable<string> knownColumns,
        HashSet<string> ignoredColumns)
    {
        var known = new HashSet<string>(knownColumns, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operators = new List<FormulaOperator>();

        if (string.IsNullOrWhiteSpace(expression))
        {
            return ManualFormulaParseResult.Invalid(["manualExpression is required."]);
        }

        var expectingOperand = true;
        var depth = 0;
        var maxDepth = 0;

        for (var i = 0; i < expression.Length;)
        {
            var c = expression[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                if (!expectingOperand)
                {
                    errors.Add("Unexpected '(' token.");
                    break;
                }

                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                i++;
                continue;
            }

            if (c == ')')
            {
                if (expectingOperand)
                {
                    errors.Add("Unexpected ')' token.");
                    break;
                }

                depth--;
                if (depth < 0)
                {
                    errors.Add("Unbalanced parentheses.");
                    break;
                }

                i++;
                continue;
            }

            if (c is '+' or '-' or '*' or '/')
            {
                if (expectingOperand)
                {
                    errors.Add("Unexpected operator token.");
                    break;
                }

                operators.Add(c switch
                {
                    '+' => FormulaOperator.Add,
                    '-' => FormulaOperator.Subtract,
                    '*' => FormulaOperator.Multiply,
                    _ => FormulaOperator.Divide
                });

                expectingOperand = true;
                i++;
                continue;
            }

            string token;
            if (c == '[')
            {
                var close = expression.IndexOf(']', i + 1);
                if (close <= i + 1)
                {
                    errors.Add("Invalid bracketed column token.");
                    break;
                }

                token = expression[(i + 1)..close].Trim();
                i = close + 1;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] is '_' or '.'))
                {
                    i++;
                }

                token = expression[start..i].Trim();
            }
            else
            {
                errors.Add($"Invalid token '{c}'.");
                break;
            }

            if (!expectingOperand)
            {
                errors.Add("Missing operator between operands.");
                break;
            }

            if (!known.Contains(token))
            {
                errors.Add($"Unknown column '{token}' in expression.");
                continue;
            }

            if (ignoredColumns.Contains(token))
            {
                errors.Add($"Ignored column '{token}' cannot be used in expression.");
                continue;
            }

            usedColumns.Add(token);
            expectingOperand = false;
        }

        if (depth != 0)
        {
            errors.Add("Unbalanced parentheses.");
        }

        if (expectingOperand)
        {
            errors.Add("Expression cannot end with an operator.");
        }

        if (usedColumns.Count == 0)
        {
            errors.Add("Expression must reference at least one valid column.");
        }

        if (errors.Count > 0)
        {
            return ManualFormulaParseResult.Invalid(errors);
        }

        return new ManualFormulaParseResult(
            true,
            usedColumns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            operators,
            Math.Max(1, maxDepth + (operators.Count > 0 ? 1 : 0)),
            Array.Empty<string>());
    }

    private sealed record FormulaColumnInfo(string Name, InferredType Type, bool IsIgnored);

    private sealed record ManualFormulaParseResult(
        bool IsValid,
        IReadOnlyList<string> UsedColumns,
        IReadOnlyList<FormulaOperator> OperatorsUsed,
        int Depth,
        IReadOnlyList<string> Errors)
    {
        public static ManualFormulaParseResult Invalid(IReadOnlyList<string> errors)
            => new(false, Array.Empty<string>(), Array.Empty<FormulaOperator>(), 0, errors);
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
        [FromQuery] string? search = null,
        [FromQuery] string? fieldStatsColumn = null)
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
                        fieldStats = (object?)null,
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
                    Values = filter.Values,
                    LogicalOperator = filter.LogicalOperator
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
WITH source_rows AS (
    SELECT
        ROW_NUMBER() OVER () AS {EscapeIdentifier(RawRowControlColumn)},
        *
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
)
SELECT {selectColumns}
FROM source_rows{whereClause}
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

            object? fieldStats = null;
            var requestedField = ResolveRequestedField(fieldStatsColumn, columns);
            if (!string.IsNullOrWhiteSpace(requestedField))
            {
                try
                {
                    fieldStats = BuildRawFieldStats(connection, escapedPath, whereClause, requestedField!);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Raw field stats failed for dataset {DatasetId} and column {Column}. Returning rows without fieldStats.",
                        id,
                        requestedField);
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
                    fieldStats,
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
            DefaultTimeoutSeconds = _runtimeSettings.DefaultTimeoutSeconds,
            RawTopValuesLimit = RawTopValuesLimit,
            RawTopRangesLimit = RawTopRangesLimit,
            RawRangeBinCount = RawRangeBinCount
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
            var index = await _indexStore.LoadAsync(id);

            if (!result.IsSuccess)
            {
                return ErrorResponse(StatusCodes.Status404NotFound, result.Errors, "not_found");
            }

            return Ok(new
            {
                success = true,
                data = result.Data,
                indexGenerated = index is not null,
                targetFormulaSuggestion = index?.TargetFormulaSuggestion,
                formulaInferenceSummary = BuildFormulaInferenceSummary(index?.FormulaInference?.Result)
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
        [FromQuery] ChartExecutionQueryRequest request)
    {
        _logger.LogInformation(
            "GetChart called - DatasetId: {DatasetId}, RecommendationId: {RecommendationId}, Aggregation: {Aggregation}, TimeBin: {TimeBin}, XColumn: {XColumn}, YColumn: {YColumn}, GroupBy: {GroupBy}, Filters: {FilterCount}, View: {View}, Percentile: {Percentile}, Mode: {Mode}",
            id,
            recommendationId,
            request.Aggregation ?? "null",
            request.TimeBin ?? "null",
            request.XColumn ?? "null",
            (request.MetricY ?? request.YColumn) ?? "null",
            request.GroupBy ?? "null",
            request.Filters?.Length ?? 0,
            request.View ?? "base",
            request.Percentile ?? "null",
            request.Mode ?? "none");

        try
        {
            var result = await _mediator.Send(new GetDataSetChartApiQuery
            {
                DatasetId = id,
                RecommendationId = recommendationId,
                Aggregation = request.Aggregation,
                TimeBin = request.TimeBin,
                XColumn = request.XColumn,
                YColumn = request.YColumn,
                MetricY = request.MetricY,
                GroupBy = request.GroupBy,
                Filters = request.Filters,
                View = request.View,
                Percentile = request.Percentile,
                Mode = request.Mode,
                PercentileTarget = request.PercentileTarget
            }, HttpContext.RequestAborted);

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
                    CacheHit = domainResponse.CacheHit,
                    Percentiles = domainResponse.Percentiles,
                    View = domainResponse.View,
                    TargetColumn = domainResponse.TargetColumn,
                    IgnoredColumnsCount = domainResponse.IgnoredColumnsCount,
                    ConfirmedSchema = domainResponse.SchemaConfirmed,
                    AggregationUsed = domainResponse.AggregationUsed,
                    TopN = domainResponse.TopN,
                    TimeBin = domainResponse.TimeBin,
                    PercentScaleHintBySeries = domainResponse.PercentScaleHintBySeries,
                    YAxisMapping = domainResponse.YAxisMapping,
                    AxisPolicy = domainResponse.AxisPolicy,
                    SeriesAxisAssignments = domainResponse.SeriesAxisAssignments
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
        [FromBody] AiChartRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new GenerateAiSummaryQuery
        {
            DatasetId = id,
            RecommendationId = recommendationId,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters.ToArray(),
            ScenarioMeta = request.ScenarioMeta
        }, HttpContext.RequestAborted);

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
        [FromBody] AiChartRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new ExplainChartQuery
        {
            DatasetId = id,
            RecommendationId = recommendationId,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters.ToArray(),
            ScenarioMeta = request.ScenarioMeta
        }, HttpContext.RequestAborted);

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

    [HttpPost("{id:guid}/charts/{recommendationId}/deep-insights")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateDeepInsights(
        Guid id,
        string recommendationId,
        [FromBody] DeepInsightsApiRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new GenerateDeepInsightsQuery
        {
            DatasetId = id,
            RecommendationId = recommendationId,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters.ToArray(),
            Scenario = request.Scenario,
            Horizon = request.Horizon,
            SensitiveMode = request.SensitiveMode,
            RequesterKey = ResolveRequesterKey()
        }, HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            var firstError = result.Errors.FirstOrDefault() ?? "Unable to generate deep insights.";
            if (firstError.Contains("budget", StringComparison.OrdinalIgnoreCase) ||
                firstError.Contains("cooldown", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorResponse(StatusCodes.Status429TooManyRequests, "rate_limited", firstError);
            }

            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            report = result.Data!.Report,
            meta = result.Data.Meta,
            explainability = result.Data.Explainability,
            evidencePack = request.IncludeEvidence ? result.Data.EvidencePack : null
        }));
    }

    [HttpPost("{id:guid}/insights/pack")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetInsightPack(
        Guid id,
        [FromBody] InsightPackRequest request) 
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new BuildInsightPackQuery
        {
            DatasetId = id,
            RecommendationId = request.RecommendationId,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters.ToArray(),
            Month = request.Month,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            SegmentColumn = request.SegmentColumn,
            SegmentValue = request.SegmentValue,
            OutputMode = request.OutputMode,
            Scenario = request.Scenario,
            Horizon = request.Horizon,
            SensitiveMode = request.SensitiveMode,
            RequesterKey = ResolveRequesterKey()
        }, HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            version = result.Data!.Pack.Version,
            pack = result.Data!.Pack,
            packV2 = result.Data.Pack.PackV2,
            meta = result.Data.Meta
        }));
    }

    [HttpGet("{id:guid}/insights/pack")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetInsightPackV2(
        Guid id,
        [FromQuery] InsightPackQueryRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new BuildInsightPackQuery
        {
            DatasetId = id,
            RecommendationId = request.RecommendationId,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters,
            Month = request.Month,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            SegmentColumn = request.SegmentColumn,
            SegmentValue = request.SegmentValue,
            OutputMode = request.OutputMode,
            Horizon = request.Horizon,
            SensitiveMode = request.SensitiveMode,
            RequesterKey = ResolveRequesterKey()
        }, HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            version = result.Data!.Pack.Version,
            pack = result.Data!.Pack,
            packV2 = result.Data.Pack.PackV2,
            meta = result.Data.Meta
        }));
    }

    [HttpPost("{id:guid}/insights/ask")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AskWithInsightPack(
        Guid id,
        [FromBody] InsightPackAskApiRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new AskWithInsightPackQuery
        {
            DatasetId = id,
            RecommendationId = request.RecommendationId,
            Question = request.Question,
            Language = language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = request.Filters.ToArray(),
            Month = request.Month,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            SegmentColumn = request.SegmentColumn,
            SegmentValue = request.SegmentValue,
            OutputMode = request.OutputMode,
            SensitiveMode = request.SensitiveMode
        }, HttpContext.RequestAborted);

        if (!result.IsSuccess)
        {
            return ResponseResult(Result.Failure<object>(result.Errors));
        }

        return ResponseResult(Result.Success(new
        {
            answer = result.Data!.Answer,
            answerJson = result.Data.AnswerJson,
            caveats = result.Data.Caveats,
            citations = result.Data.Citations,
            evidenceResolved = result.Data.EvidenceResolved,
            packVersion = result.Data.Meta.PackVersion,
            pack = result.Data.Pack,
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
        [FromBody] AskDatasetRequest request)
    {
        var language = ResolveLanguage();
        var result = await _mediator.Send(new AskDatasetAnalysisPlanQuery
        {
            DatasetId = id,
            Language = language,
            Question = request.Question,
            CurrentView = request.CurrentView
        }, HttpContext.RequestAborted);

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
        [FromBody] ScenarioSimulationRequest request)
    {
        var result = await _mediator.Send(new SimulateDataSetQuery
        {
            DatasetId = id,
            Request = new ScenarioRequest
            {
                TargetMetric = request.TargetMetric,
                TargetDimension = request.TargetDimension,
                Aggregation = request.Aggregation,
                PropagateTargetFormula = request.PropagateTargetFormula,
                Filters = request.Filters,
                Operations = request.Operations
            }
        }, HttpContext.RequestAborted);
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
            appliedFormulaExpression = simulation.AppliedFormulaExpression,
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

            var parts = raw.Split('|', StringSplitOptions.None);
            if (parts.Length < 3)
            {
                errors.Add($"Invalid filter format '{raw}'. Use column|operator|value.");
                continue;
            }

            var column = parts[0].Trim();
            var opRaw = parts[1].Trim();
            var logicalOperator = FilterLogicalOperator.And;
            var valueParts = parts.Skip(2).ToList();
            if (valueParts.Count > 1 &&
                TryParseFilterLogicalOperator(valueParts[^1], out var parsedLogicalOperator))
            {
                logicalOperator = parsedLogicalOperator;
                valueParts.RemoveAt(valueParts.Count - 1);
            }

            var valueRaw = string.Join("|", valueParts).Trim();

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

            if (op == FilterOperator.Between)
            {
                if (values.Count < 2 || values.Count % 2 != 0)
                {
                    errors.Add($"Filter '{raw}' must provide an even number of values (2, 4, 6...) for 'between'.");
                    continue;
                }
            }

            if ((op == FilterOperator.Gt ||
                 op == FilterOperator.Gte ||
                 op == FilterOperator.Lt ||
                 op == FilterOperator.Lte) && values.Count != 1)
            {
                errors.Add($"Filter '{raw}' must provide a single value for '{opRaw}'.");
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
                Values = values,
                LogicalOperator = logicalOperator
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

    private string ResolveRequesterKey()
    {
        var userId = HttpContext?.User?.Identity?.IsAuthenticated == true
            ? HttpContext.User.Identity?.Name
            : null;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        var remoteIp = HttpContext?.Connection?.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            return $"ip:{remoteIp}";
        }

        return "anonymous";
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

    private static string? ResolveRequestedField(string? requestedField, IReadOnlyCollection<string> columns)
    {
        if (columns.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(requestedField))
        {
            return columns.FirstOrDefault();
        }

        return columns.FirstOrDefault(column =>
            string.Equals(column, requestedField, StringComparison.OrdinalIgnoreCase));
    }

    private object BuildRawFieldStats(
        DuckDBConnection connection,
        string escapedPath,
        string whereClause,
        string column)
    {
        var escapedColumn = EscapeIdentifier(column);
        var columnExpr = $"CAST({escapedColumn} AS VARCHAR)";
        var parsedDateExpr = BuildParsedDateExpression(columnExpr);

        var summarySql = $@"
SELECT
    COUNT(*) AS total_count,
    SUM(CASE WHEN COALESCE(TRIM({columnExpr}), '') = '' THEN 1 ELSE 0 END) AS null_count,
    COUNT(DISTINCT CASE WHEN COALESCE(TRIM({columnExpr}), '') = '' THEN NULL ELSE {columnExpr} END) AS distinct_count,
    SUM(CASE WHEN TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE) IS NOT NULL THEN 1 ELSE 0 END) AS numeric_count,
    SUM(CASE WHEN {parsedDateExpr} IS NOT NULL THEN 1 ELSE 0 END) AS date_count
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause};
";

        long totalCount = 0;
        long nullCount = 0;
        long distinctCount = 0;
        long numericCount = 0;
        long dateCount = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = summarySql;
            command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                totalCount = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                nullCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                distinctCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                numericCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                dateCount = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
            }
        }

        var topValues = new List<RawDistinctStat>();
        var topValuesSql = $@"
SELECT
    {columnExpr} AS value,
    COUNT(*) AS frequency
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){AppendWherePredicate(whereClause, $"COALESCE(TRIM({columnExpr}), '') <> ''")}
GROUP BY 1
ORDER BY frequency DESC, value ASC
LIMIT {RawTopValuesLimit};
";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = topValuesSql;
            command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                topValues.Add(new RawDistinctStat(
                    reader.IsDBNull(0) ? string.Empty : reader.GetValue(0)?.ToString() ?? string.Empty,
                    reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1) ?? 0)));
            }
        }

        var nonNullCount = Math.Max(totalCount - nullCount, 0);
        var numericRatio = nonNullCount == 0 ? 0 : numericCount / (double)nonNullCount;
        var dateRatio = nonNullCount == 0 ? 0 : dateCount / (double)nonNullCount;

        var inferredType = "String";
        if (numericRatio >= 0.9)
        {
            inferredType = "Number";
        }
        else if (dateRatio >= 0.9)
        {
            inferredType = "Date";
        }
        else if (distinctCount <= 50)
        {
            inferredType = "Category";
        }

        var topRanges = new List<RawRangeStat>();
        if (distinctCount > RawRangeDistinctThreshold)
        {
            if (string.Equals(inferredType, "Number", StringComparison.OrdinalIgnoreCase))
            {
                topRanges = LoadNumericTopRanges(connection, escapedPath, whereClause, columnExpr);
            }
            else if (string.Equals(inferredType, "Date", StringComparison.OrdinalIgnoreCase))
            {
                topRanges = LoadDateTopRanges(connection, escapedPath, whereClause, columnExpr);
            }
        }

        return new
        {
            column,
            inferredType,
            distinctCount,
            nullCount,
            topValues = topValues.Select(item => new
            {
                value = item.Value,
                count = item.Count
            }),
            topRanges = topRanges.Select(item => new
            {
                label = item.Label,
                from = item.From,
                to = item.To,
                count = item.Count
            })
        };
    }

    private List<RawRangeStat> LoadNumericTopRanges(
        DuckDBConnection connection,
        string escapedPath,
        string whereClause,
        string columnExpr)
    {
        var sql = $@"
WITH values_cte AS (
    SELECT TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE) AS numeric_value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause}
),
normalized AS (
    SELECT numeric_value
    FROM values_cte
    WHERE numeric_value IS NOT NULL
),
stats AS (
    SELECT
        MIN(numeric_value) AS min_value,
        MAX(numeric_value) AS max_value
    FROM normalized
),
bucketed AS (
    SELECT
        CASE
            WHEN stats.max_value = stats.min_value THEN 0
            ELSE LEAST({RawRangeBinCount - 1},
                GREATEST(0, CAST(FLOOR((normalized.numeric_value - stats.min_value) / NULLIF((stats.max_value - stats.min_value) / {RawRangeBinCount}, 0)) AS INTEGER)))
        END AS bucket_index,
        normalized.numeric_value
    FROM normalized
    CROSS JOIN stats
)
SELECT
    bucket_index,
    COUNT(*) AS frequency,
    MIN(numeric_value) AS range_min,
    MAX(numeric_value) AS range_max
FROM bucketed
GROUP BY bucket_index
ORDER BY frequency DESC, bucket_index
LIMIT {RawTopRangesLimit};
";

        var ranges = new List<RawRangeStat>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3))
            {
                continue;
            }

            var min = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
            var max = Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture);
            var count = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1) ?? 0);

            var minText = min.ToString("0.####", CultureInfo.InvariantCulture);
            var maxText = max.ToString("0.####", CultureInfo.InvariantCulture);
            ranges.Add(new RawRangeStat(
                $"{minText} - {maxText}",
                minText,
                maxText,
                count));
        }

        return ranges
            .OrderBy(item => double.TryParse(item.From, NumberStyles.Any, CultureInfo.InvariantCulture, out var from) ? from : double.MaxValue)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<RawRangeStat> LoadDateTopRanges(
        DuckDBConnection connection,
        string escapedPath,
        string whereClause,
        string columnExpr)
    {
        var parsedDateExpr = BuildParsedDateExpression(columnExpr);
        var sql = $@"
WITH values_cte AS (
    SELECT {parsedDateExpr} AS ts_value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true){whereClause}
),
normalized AS (
    SELECT ts_value, EXTRACT(EPOCH FROM ts_value) AS epoch_value
    FROM values_cte
    WHERE ts_value IS NOT NULL
),
stats AS (
    SELECT
        MIN(epoch_value) AS min_value,
        MAX(epoch_value) AS max_value
    FROM normalized
),
bucketed AS (
    SELECT
        CASE
            WHEN stats.max_value = stats.min_value THEN 0
            ELSE LEAST({RawRangeBinCount - 1},
                GREATEST(0, CAST(FLOOR((normalized.epoch_value - stats.min_value) / NULLIF((stats.max_value - stats.min_value) / {RawRangeBinCount}, 0)) AS INTEGER)))
        END AS bucket_index,
        normalized.ts_value
    FROM normalized
    CROSS JOIN stats
)
SELECT
    bucket_index,
    COUNT(*) AS frequency,
    MIN(ts_value) AS range_min,
    MAX(ts_value) AS range_max
FROM bucketed
GROUP BY bucket_index
ORDER BY frequency DESC, bucket_index
LIMIT {RawTopRangesLimit};
";

        var ranges = new List<RawRangeStat>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Max(1, _runtimeSettings.DefaultTimeoutSeconds);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3))
            {
                continue;
            }

            var min = Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture);
            var max = Convert.ToDateTime(reader.GetValue(3), CultureInfo.InvariantCulture);
            var count = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1) ?? 0);

            var minText = min.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var maxText = max.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            ranges.Add(new RawRangeStat(
                $"{min:yyyy-MM-dd} - {max:yyyy-MM-dd}",
                minText,
                maxText,
                count));
        }

        return ranges
            .OrderBy(item => DateTime.TryParse(item.From, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var from) ? from : DateTime.MaxValue)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string AppendWherePredicate(string whereClause, string predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return whereClause;
        }

        if (string.IsNullOrWhiteSpace(whereClause))
        {
            return $"\nWHERE {predicate}";
        }

        return $"{whereClause} AND {predicate}";
    }

    private static string BuildRawRowsWhereClause(
        IReadOnlyCollection<ChartFilter> filters,
        string? search,
        IReadOnlyCollection<string> columns)
    {
        var expressions = new List<string>();

        var filterExpression = BuildCombinedFilterExpression(filters, BuildRawFilterExpression);
        if (!string.IsNullOrWhiteSpace(filterExpression))
        {
            expressions.Add(filterExpression);
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

            return $"\nORDER BY {EscapeIdentifier(columns[0])} ASC, {EscapeIdentifier(RawRowControlColumn)} ASC";
        }

        var segments = sortRules
            .Select(rule => $"{EscapeIdentifier(rule.Column)} {(rule.Descending ? "DESC" : "ASC")}")
            .ToList();

        segments.Add($"{EscapeIdentifier(RawRowControlColumn)} ASC");
        return $"\nORDER BY {string.Join(", ", segments)}";
    }

    private static string BuildRawFilterExpression(ChartFilter filter)
    {
        var columnExpr = $"CAST({EscapeIdentifier(filter.Column)} AS VARCHAR)";
        return filter.Operator switch
        {
            FilterOperator.Eq => BuildRawComparisonExpression(columnExpr, "=", filter.Values),
            FilterOperator.NotEq => BuildRawComparisonExpression(columnExpr, "<>", filter.Values),
            FilterOperator.Gt => BuildRawComparisonExpression(columnExpr, ">", filter.Values),
            FilterOperator.Gte => BuildRawComparisonExpression(columnExpr, ">=", filter.Values),
            FilterOperator.Lt => BuildRawComparisonExpression(columnExpr, "<", filter.Values),
            FilterOperator.Lte => BuildRawComparisonExpression(columnExpr, "<=", filter.Values),
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

        if (TryParseDateValues(values, out var dateValues))
        {
            var parsedDateExpr = BuildParsedDateExpression(columnExpr);
            var timestamp = dateValues[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return $"{parsedDateExpr} {op} TIMESTAMP {ToSqlLiteral(timestamp)}";
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

        if (TryParseDateValues(values, out var dateValues))
        {
            var parsedDateExpr = BuildParsedDateExpression(columnExpr);
            var timestampList = string.Join(", ", dateValues.Select(value =>
                $"TIMESTAMP {ToSqlLiteral(value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}"));
            return $"{parsedDateExpr} IN ({timestampList})";
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
            var ranges = BuildRangeExpressions(
                numericValues,
                (left, right) => $"{numericExpr} BETWEEN {left.ToString(CultureInfo.InvariantCulture)} AND {right.ToString(CultureInfo.InvariantCulture)}");

            return ranges.Count == 0
                ? string.Empty
                : ranges.Count == 1
                    ? ranges[0]
                    : $"({string.Join(" OR ", ranges)})";
        }

        if (TryParseDateValues(values, out var dateValues) && dateValues.Count >= 2)
        {
            var parsedDateExpr = BuildParsedDateExpression(columnExpr);
            var ranges = BuildRangeExpressions(
                dateValues,
                (left, right) =>
                    $"{parsedDateExpr} BETWEEN TIMESTAMP {ToSqlLiteral(left.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} AND TIMESTAMP {ToSqlLiteral(right.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}");

            return ranges.Count == 0
                ? string.Empty
                : ranges.Count == 1
                    ? ranges[0]
                    : $"({string.Join(" OR ", ranges)})";
        }

        var textRanges = BuildRangeExpressions(
            values,
            (left, right) => $"{columnExpr} BETWEEN {ToSqlLiteral(left)} AND {ToSqlLiteral(right)}");

        return textRanges.Count == 0
            ? string.Empty
            : textRanges.Count == 1
                ? textRanges[0]
                : $"({string.Join(" OR ", textRanges)})";
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

    private static bool TryParseDateValues(IReadOnlyList<string> values, out List<DateTime> parsedDates)
    {
        parsedDates = new List<DateTime>();
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;

        foreach (var value in values)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed) &&
                !DateTime.TryParse(value, CultureInfo.CurrentCulture, styles, out parsed))
            {
                parsedDates.Clear();
                return false;
            }

            parsedDates.Add(parsed);
        }

        return parsedDates.Count > 0;
    }

    private static List<string> BuildRangeExpressions<T>(IReadOnlyList<T> values, Func<T, T, string> rangeBuilder)
    {
        var expressions = new List<string>();
        if (values.Count < 2)
        {
            return expressions;
        }

        for (var index = 0; index + 1 < values.Count; index += 2)
        {
            expressions.Add(rangeBuilder(values[index], values[index + 1]));
        }

        return expressions;
    }

    private static string BuildCombinedFilterExpression(
        IReadOnlyCollection<ChartFilter> filters,
        Func<ChartFilter, string> expressionBuilder)
    {
        string? combined = null;

        foreach (var filter in filters)
        {
            var expression = expressionBuilder(filter);
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            if (combined == null)
            {
                combined = $"({expression})";
                continue;
            }

            var logicalOperator = filter.LogicalOperator == FilterLogicalOperator.Or ? "OR" : "AND";
            combined = $"({combined} {logicalOperator} ({expression}))";
        }

        return combined ?? string.Empty;
    }

    private static string BuildParsedDateExpression(string columnExpr)
    {
        return $@"COALESCE(
            TRY_CAST({columnExpr} AS TIMESTAMP),
            TRY_STRPTIME({columnExpr}, '%Y%m%d'),
            TRY_STRPTIME({columnExpr}, '%d/%m/%Y'),
            TRY_STRPTIME({columnExpr}, '%Y-%m-%d'),
            TRY_STRPTIME({columnExpr}, '%m/%d/%Y')
        )";
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
        var normalized = input?.Trim() ?? string.Empty;

        normalized = normalized switch
        {
            "==" => "Eq",
            "!=" => "NotEq",
            ">" => "Gt",
            ">=" => "Gte",
            "<" => "Lt",
            "<=" => "Lte",
            "Ln" => "In",
            _ => normalized
        };

        return Enum.TryParse(normalized, true, out op);
    }

    private static bool TryParseFilterLogicalOperator(string input, out FilterLogicalOperator logicalOperator)
    {
        return Enum.TryParse(input, true, out logicalOperator);
    }

    private static object? BuildFormulaInferenceSummary(FormulaInferenceResult? result)
    {
        if (result is null)
        {
            return null;
        }

        var bestCandidate = result.Candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.UsedColumns.Length)
            .ThenBy(candidate => candidate.Depth)
            .FirstOrDefault();

        return new
        {
            status = result.Status,
            targetColumn = result.TargetColumn,
            candidatesCount = result.Candidates.Count,
            generatedAt = result.GeneratedAt,
            bestCandidateExpressionText = bestCandidate?.ExpressionText,
            bestCandidateConfidence = bestCandidate?.Confidence,
            bestCandidateUsedColumns = bestCandidate?.UsedColumns ?? Array.Empty<string>(),
            warnings = result.Warnings
        };
    }

    private readonly record struct RawSortRule(string Column, bool Descending);
    private readonly record struct RawDistinctStat(string Value, long Count);
    private readonly record struct RawFacetStat(string Value, long Count);
    private readonly record struct RawRangeStat(string Label, string From, string To, long Count);
}

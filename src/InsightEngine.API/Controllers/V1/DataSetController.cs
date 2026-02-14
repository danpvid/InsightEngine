using InsightEngine.API.Models;
using InsightEngine.Application.Services;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly ILogger<DataSetController> _logger;
    private readonly IWebHostEnvironment _environment;

    // Limite de 20MB por arquivo (MVP)
    private const long MaxFileSize = 20L * 1024 * 1024;

    public DataSetController(
        IDataSetApplicationService dataSetApplicationService,
        IDomainNotificationHandler notificationHandler,
        IMediator mediator,
        ILogger<DataSetController> logger,
        IWebHostEnvironment environment)
        : base(notificationHandler, mediator)
    {
        _dataSetApplicationService = dataSetApplicationService;
        _logger = logger;
        _environment = environment;
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
    [RequestSizeLimit(MaxFileSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    [DisableRequestSizeLimit] // Para Kestrel
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            var result = await _dataSetApplicationService.UploadAsync(file);

            if (!result.IsSuccess)
            {
                return BadRequest(new
                {
                    success = false,
                    errors = result.Errors
                });
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
            
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Erro interno ao processar o arquivo.",
                error = ex.Message
            });
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
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    errors = result.Errors
                });
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
            
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Erro ao listar datasets.",
                error = ex.Message
            });
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
        return NotFound(new
        {
            success = false,
            message = "Dataset não encontrado."
        });
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
                return NotFound(new
                {
                    success = false,
                    errors = result.Errors
                });
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
            
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Erro ao gerar profile do dataset.",
                error = ex.Message
            });
        }
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
                return NotFound(new
                {
                    success = false,
                    errors = result.Errors
                });
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
            
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Erro ao gerar recomendações do dataset.",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Executa uma recomendação de gráfico e retorna EChartsOption completo com dados reais
    /// </summary>
    /// <param name="id">ID do dataset</param>
    /// <param name="recommendationId">ID da recomendação (ex: rec_001)</param>
    /// <returns>EChartsOption completo pronto para renderização</returns>
    /// <remarks>
    /// Exemplo de uso:
    /// 
    ///     GET /api/v1/datasets/{datasetId}/charts/rec_001
    ///     
    /// Dia 4 MVP:
    /// - Suporta apenas gráficos tipo Line (time series)
    /// - Suporta ECharts como biblioteca
    /// - Executa agregação via DuckDB
    /// - Retorna option com series.data preenchido
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
    public async Task<IActionResult> GetChart(Guid id, string recommendationId)
    {
        try
        {
            var result = await _dataSetApplicationService.GetChartAsync(id, recommendationId);

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
                Meta = new ChartExecutionMeta
                {
                    ExecutionMs = domainResponse.TotalExecutionMs,
                    DuckDbMs = domainResponse.ExecutionResult.DuckDbMs,
                    QueryHash = domainResponse.QueryHash,
                    DatasetVersion = null, // Não implementado ainda
                    RowCountReturned = domainResponse.ExecutionResult.RowCount,
                    ChartType = domainResponse.ExecutionResult.Option.Series?.FirstOrDefault()?
                        .GetValueOrDefault("type")?.ToString() ?? "line",
                    GeneratedAt = DateTime.UtcNow,
                    DebugSql = _environment.IsDevelopment() ? domainResponse.ExecutionResult.GeneratedSql : null
                }
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
}
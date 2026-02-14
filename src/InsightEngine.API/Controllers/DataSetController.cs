using InsightEngine.Application.Commands.DataSet;
using InsightEngine.Application.Models.DataSet;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers;

/// <summary>
/// Controller para gerenciamento de datasets (arquivos CSV)
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class DataSetController : BaseController
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly ILogger<DataSetController> _logger;

    // Limite de 500MB por arquivo
    private const long MaxFileSize = 500L * 1024 * 1024;

    public DataSetController(
        IDomainNotificationHandler notificationHandler,
        IMediator mediator,
        IDataSetRepository dataSetRepository,
        ILogger<DataSetController> logger) : base(notificationHandler, mediator)
    {
        _dataSetRepository = dataSetRepository;
        _logger = logger;
    }

    /// <summary>
    /// Upload de arquivo CSV (suporta arquivos grandes usando streaming)
    /// </summary>
    /// <param name="file">Arquivo CSV</param>
    /// <returns>Metadata do dataset criado</returns>
    /// <remarks>
    /// Exemplo de requisição:
    /// 
    ///     POST /api/dataset/upload
    ///     Content-Type: multipart/form-data
    ///     
    ///     file: [arquivo.csv]
    ///     
    /// Limite máximo: 500MB
    /// 
    /// O arquivo é salvo com um GUID único para:
    /// - Evitar colisões de nomes
    /// - Mitigar ataques de path traversal
    /// - Garantir unicidade
    /// 
    /// Retorna:
    /// - datasetId: Identificador único do dataset
    /// - originalFileName: Nome original do arquivo
    /// - storedPath: Caminho onde o arquivo foi armazenado
    /// - fileSizeInBytes: Tamanho do arquivo em bytes
    /// - createdAt: Data/hora do upload
    /// </remarks>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize)]
    [DisableRequestSizeLimit] // Para Kestrel
    [ProducesResponseType(typeof(DataSetUploadOutputModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            // Validações iniciais
            if (file == null || file.Length == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Nenhum arquivo foi enviado."
                });
            }

            // Valida tamanho máximo
            if (file.Length > MaxFileSize)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    success = false,
                    message = $"Arquivo muito grande. Tamanho máximo permitido: {MaxFileSize / (1024 * 1024)}MB"
                });
            }

            // Valida extensão
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (extension != ".csv")
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Apenas arquivos CSV são permitidos."
                });
            }

            _logger.LogInformation("Receiving file upload: {FileName}, Size: {Size} bytes",
                file.FileName, file.Length);

            // Cria e executa o comando usando streaming
            await using var fileStream = file.OpenReadStream();
            
            var command = new UploadDataSetCommand(
                fileStream,
                file.FileName,
                file.ContentType,
                file.Length
            );
            
            var result = await _mediator.Send(command);

            if (!result)
            {
                return ResponseCommand();
            }

            // Busca o dataset criado para retornar os metadados
            var datasets = await _dataSetRepository.GetAllAsync();
            var dataset = datasets.OrderByDescending(ds => ds.CreatedAt).FirstOrDefault();

            if (dataset == null)
            {
                return ResponseCommand();
            }

            var output = new DataSetUploadOutputModel(
                dataSetId: dataset.Id,
                originalFileName: dataset.OriginalFileName,
                storedPath: dataset.StoredPath,
                fileSizeInBytes: dataset.FileSizeInBytes,
                createdAt: dataset.CreatedAt
            );

            return Ok(new
            {
                success = true,
                message = "Arquivo enviado com sucesso.",
                data = new
                {
                    datasetId = output.DataSetId,
                    originalFileName = output.OriginalFileName,
                    storedPath = output.StoredPath,
                    fileSizeInBytes = output.FileSizeInBytes,
                    fileSizeMB = Math.Round(output.FileSizeInBytes / (1024.0 * 1024.0), 2),
                    createdAt = output.CreatedAt
                }
            });
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
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll()
    {
        var datasets = await _dataSetRepository.GetAllAsync();

        var result = datasets.Select(ds => new
        {
            datasetId = ds.Id,
            originalFileName = ds.OriginalFileName,
            storedFileName = ds.StoredFileName,
            fileSizeInBytes = ds.FileSizeInBytes,
            fileSizeMB = Math.Round(ds.FileSizeInBytes / (1024.0 * 1024.0), 2),
            createdAt = ds.CreatedAt
        });

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Obtém informações de um dataset específico
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dataset = await _dataSetRepository.GetByIdAsync(id);

        if (dataset == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Dataset não encontrado."
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                datasetId = dataset.Id,
                originalFileName = dataset.OriginalFileName,
                storedFileName = dataset.StoredFileName,
                storedPath = dataset.StoredPath,
                fileSizeInBytes = dataset.FileSizeInBytes,
                fileSizeMB = Math.Round(dataset.FileSizeInBytes / (1024.0 * 1024.0), 2),
                contentType = dataset.ContentType,
                createdAt = dataset.CreatedAt,
                updatedAt = dataset.UpdatedAt
            }
        });
    }
}

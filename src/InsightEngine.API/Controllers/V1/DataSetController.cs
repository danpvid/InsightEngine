using InsightEngine.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace InsightEngine.API.Controllers.V1;

/// <summary>
/// Controller para gerenciamento de datasets (arquivos CSV)
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/datasets")]
[AllowAnonymous]
public class DataSetController : ControllerBase
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ICsvProfiler _csvProfiler;
    private readonly ILogger<DataSetController> _logger;

    // Limite de 20MB por arquivo (MVP)
    private const long MaxFileSize = 20L * 1024 * 1024;

    public DataSetController(
        IFileStorageService fileStorageService,
        ICsvProfiler csvProfiler,
        ILogger<DataSetController> logger)
    {
        _fileStorageService = fileStorageService;
        _csvProfiler = csvProfiler;
        _logger = logger;
    }

    private class DatasetMetadata
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public long FileSizeInBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private string GetMetadataFilePath(Guid datasetId)
    {
        var directory = _fileStorageService.GetStoragePath();
        return Path.Combine(directory, $"{datasetId}.meta.json");
    }

    private async Task SaveMetadataAsync(DatasetMetadata metadata)
    {
        var metadataPath = GetMetadataFilePath(metadata.Id);
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        await System.IO.File.WriteAllTextAsync(metadataPath, json);
        _logger.LogInformation("Metadata saved: {MetadataPath}", metadataPath);
    }

    private async Task<DatasetMetadata?> LoadMetadataAsync(Guid datasetId)
    {
        var metadataPath = GetMetadataFilePath(datasetId);
        
        if (!System.IO.File.Exists(metadataPath))
        {
            _logger.LogWarning("Metadata not found: {MetadataPath}", metadataPath);
            return null;
        }

        var json = await System.IO.File.ReadAllTextAsync(metadataPath);
        return JsonSerializer.Deserialize<DatasetMetadata>(json);
    }

    private async Task<List<DatasetMetadata>> LoadAllMetadataAsync()
    {
        var directory = _fileStorageService.GetStoragePath();
        
        if (!Directory.Exists(directory))
        {
            return new List<DatasetMetadata>();
        }

        var metadataFiles = Directory.GetFiles(directory, "*.meta.json");
        var metadataList = new List<DatasetMetadata>();

        foreach (var file in metadataFiles)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(file);
                var metadata = JsonSerializer.Deserialize<DatasetMetadata>(json);
                if (metadata != null)
                {
                    metadataList.Add(metadata);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load metadata from {File}", file);
            }
        }

        return metadataList;
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

            // Gerar datasetId e nomes de arquivo
            var datasetId = Guid.NewGuid();
            var storedFileName = $"{datasetId}.csv";

            _logger.LogInformation("Generated datasetId: {DatasetId}, storedFileName: {StoredFileName}", 
                datasetId, storedFileName);

            // Salvar arquivo CSV usando streaming
            await using var fileStream = file.OpenReadStream();
            
            _logger.LogInformation("About to call SaveFileAsync with fileName: '{FileName}'", storedFileName);
            
            var (storedPath, fileSize) = await _fileStorageService.SaveFileAsync(
                fileStream: fileStream,
                fileName: storedFileName,
                cancellationToken: default);

            // Criar metadados
            var metadata = new DatasetMetadata
            {
                Id = datasetId,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                StoredPath = storedPath,
                FileSizeInBytes = fileSize,
                CreatedAt = DateTime.UtcNow
            };

            // Salvar metadados em JSON
            await SaveMetadataAsync(metadata);

            _logger.LogInformation("Dataset uploaded successfully: {DatasetId}", datasetId);

            var response = new
            {
                success = true,
                message = "Arquivo enviado com sucesso.",
                data = new
                {
                    datasetId = metadata.Id,
                    originalFileName = metadata.OriginalFileName,
                    storedFileName = metadata.StoredFileName,
                    sizeBytes = metadata.FileSizeInBytes,
                    createdAtUtc = metadata.CreatedAt
                }
            };

            return CreatedAtAction(
                nameof(GetById),
                new { id = metadata.Id, version = "1.0" },
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
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var datasets = await LoadAllMetadataAsync();

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
    public async Task<IActionResult> GetById(Guid id)
    {
        var dataset = await LoadMetadataAsync(id);

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
                createdAt = dataset.CreatedAt
            }
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
            _logger.LogInformation("Generating profile for dataset {DatasetId}", id);

            // Busca metadados
            var dataset = await LoadMetadataAsync(id);
            if (dataset == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Dataset não encontrado."
                });
            }

            // Verifica se o arquivo CSV existe
            if (!System.IO.File.Exists(dataset.StoredPath))
            {
                _logger.LogError("File not found for dataset {DatasetId}: {Path}", id, dataset.StoredPath);
                return NotFound(new
                {
                    success = false,
                    message = "Arquivo do dataset não encontrado no sistema."
                });
            }

            // Gera o profile
            var profile = await _csvProfiler.ProfileAsync(id, dataset.StoredPath);

            _logger.LogInformation(
                "Profile generated for dataset {DatasetId}: {RowCount} rows, {ColumnCount} columns",
                id, profile.RowCount, profile.Columns.Count);

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
}

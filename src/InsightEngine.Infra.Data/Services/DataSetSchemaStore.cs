using System.Text.Json;
using System.Text.Json.Serialization;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.ImportSchema;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class DataSetSchemaStore : IDataSetSchemaStore
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<DataSetSchemaStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DataSetSchemaStore(IFileStorageService fileStorageService, ILogger<DataSetSchemaStore> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task SaveAsync(DatasetImportSchema schema, CancellationToken cancellationToken = default)
    {
        var directory = GetDatasetDirectory(schema.DatasetId);
        Directory.CreateDirectory(directory);
        var schemaPath = GetSchemaPath(schema.DatasetId);
        var payload = JsonSerializer.Serialize(schema, _jsonOptions);
        await File.WriteAllTextAsync(schemaPath, payload, cancellationToken);

        _logger.LogInformation("Persisted import schema for dataset {DatasetId} at {SchemaPath}", schema.DatasetId, schemaPath);
    }

    public async Task<DatasetImportSchema?> LoadAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        var schemaPath = GetSchemaPath(datasetId);
        if (!File.Exists(schemaPath))
        {
            return null;
        }

        try
        {
            var payload = await File.ReadAllTextAsync(schemaPath, cancellationToken);
            return JsonSerializer.Deserialize<DatasetImportSchema>(payload, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load import schema for dataset {DatasetId}", datasetId);
            return null;
        }
    }

    private string GetDatasetDirectory(Guid datasetId)
    {
        return Path.Combine(_fileStorageService.GetStoragePath(), datasetId.ToString("D"));
    }

    private string GetSchemaPath(Guid datasetId)
    {
        return Path.Combine(GetDatasetDirectory(datasetId), "schema.json");
    }
}

using InsightEngine.Domain.Models.ImportSchema;

namespace InsightEngine.Domain.Interfaces;

public interface IDataSetSchemaStore
{
    Task SaveAsync(DatasetImportSchema schema, CancellationToken cancellationToken = default);
    Task<DatasetImportSchema?> LoadAsync(Guid datasetId, CancellationToken cancellationToken = default);
}

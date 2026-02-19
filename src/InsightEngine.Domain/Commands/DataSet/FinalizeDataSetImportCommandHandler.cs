using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.ImportSchema;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Commands.DataSet;

public class FinalizeDataSetImportCommandHandler : IRequestHandler<FinalizeDataSetImportCommand, Result<FinalizeDataSetImportResponse>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IDataSetSanitizer _dataSetSanitizer;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly ILogger<FinalizeDataSetImportCommandHandler> _logger;

    public FinalizeDataSetImportCommandHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        IDataSetSanitizer dataSetSanitizer,
        IDataSetSchemaStore schemaStore,
        ILogger<FinalizeDataSetImportCommandHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _csvProfiler = csvProfiler;
        _dataSetSanitizer = dataSetSanitizer;
        _schemaStore = schemaStore;
        _logger = logger;
    }

    public async Task<Result<FinalizeDataSetImportResponse>> Handle(
        FinalizeDataSetImportCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<FinalizeDataSetImportResponse>("Dataset not found.");
            }

            if (!File.Exists(dataSet.StoredPath))
            {
                return Result.Failure<FinalizeDataSetImportResponse>("Dataset file not found.");
            }

            var profile = await _csvProfiler.ProfileAsync(request.DatasetId, dataSet.StoredPath, cancellationToken);
            var allColumns = profile.Columns.Select(column => column.Name).ToList();
            var lookup = allColumns.ToDictionary(column => column, column => column, StringComparer.OrdinalIgnoreCase);

            var ignoredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ignored in request.IgnoredColumns.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()))
            {
                if (!lookup.TryGetValue(ignored, out var resolvedIgnored))
                {
                    return Result.Failure<FinalizeDataSetImportResponse>($"Ignored column '{ignored}' does not exist in dataset.");
                }

                ignoredColumns.Add(resolvedIgnored);
            }

            string? targetColumn = null;
            if (!string.IsNullOrWhiteSpace(request.TargetColumn))
            {
                if (!lookup.TryGetValue(request.TargetColumn.Trim(), out var resolvedTarget))
                {
                    return Result.Failure<FinalizeDataSetImportResponse>($"Target column '{request.TargetColumn}' does not exist in dataset.");
                }

                targetColumn = resolvedTarget;
            }

            if (!string.IsNullOrWhiteSpace(targetColumn) && ignoredColumns.Contains(targetColumn))
            {
                return Result.Failure<FinalizeDataSetImportResponse>("Target column cannot be in ignoredColumns.");
            }

            var overrides = new Dictionary<string, InferredType>(StringComparer.OrdinalIgnoreCase);
            foreach (var (columnName, typeName) in request.ColumnTypeOverrides)
            {
                if (!lookup.TryGetValue(columnName, out var resolvedColumn))
                {
                    return Result.Failure<FinalizeDataSetImportResponse>($"Override column '{columnName}' does not exist in dataset.");
                }

                if (!Enum.TryParse<InferredType>(typeName, true, out var parsedType))
                {
                    return Result.Failure<FinalizeDataSetImportResponse>($"Invalid column type override '{typeName}' for column '{columnName}'.");
                }

                overrides[resolvedColumn] = parsedType.NormalizeLegacy();
            }

            var storedColumnsCount = allColumns.Count - ignoredColumns.Count;
            if (storedColumnsCount <= 0)
            {
                return Result.Failure<FinalizeDataSetImportResponse>("At least one non-ignored column is required.");
            }

            var schemaColumns = profile.Columns
                .Select(column =>
                {
                    var confirmedType = overrides.TryGetValue(column.Name, out var overrideType)
                        ? overrideType
                        : column.InferredType.NormalizeLegacy();

                    return new DatasetImportSchemaColumn
                    {
                        Name = column.Name,
                        InferredType = column.InferredType.NormalizeLegacy(),
                        ConfirmedType = confirmedType,
                        IsIgnored = ignoredColumns.Contains(column.Name),
                        IsTarget = string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase),
                        CurrencyCode = confirmedType == InferredType.Money ? request.CurrencyCode : null,
                        HasPercentSign = confirmedType == InferredType.Percentage ? column.HasPercentSign : null
                    };
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(targetColumn))
            {
                var targetSchema = schemaColumns.FirstOrDefault(column =>
                    string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase));

                if (targetSchema is null)
                {
                    return Result.Failure<FinalizeDataSetImportResponse>("Target column was not found in finalized schema.");
                }

                if (!targetSchema.ConfirmedType.IsNumericLike())
                {
                    return Result.Failure<FinalizeDataSetImportResponse>("Target column must be numeric-like (Integer, Decimal, Money, Percentage).");
                }
            }

            if (ignoredColumns.Count > 0)
            {
                var updatedSize = await _dataSetSanitizer
                    .RewriteWithoutColumnsAsync(dataSet.StoredPath, ignoredColumns, cancellationToken);
                dataSet.UpdateFileInfo(updatedSize);
            }

            var schema = new DatasetImportSchema
            {
                DatasetId = request.DatasetId,
                SchemaVersion = 1,
                SchemaConfirmed = true,
                TargetColumn = targetColumn,
                CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "BRL" : request.CurrencyCode,
                FinalizedAtUtc = DateTime.UtcNow,
                Columns = schemaColumns
            };

            await _schemaStore.SaveAsync(schema, cancellationToken);

            dataSet.UpdateProfile(profile.RowCount, $"Rows: {profile.RowCount}, Columns: {storedColumnsCount}, Schema: v{schema.SchemaVersion}");
            _dataSetRepository.Update(dataSet);
            await _unitOfWork.CommitAsync();

            return Result.Success(new FinalizeDataSetImportResponse
            {
                DatasetId = request.DatasetId,
                SchemaVersion = schema.SchemaVersion,
                TargetColumn = targetColumn,
                IgnoredColumnsCount = ignoredColumns.Count,
                StoredColumnsCount = storedColumnsCount,
                CurrencyCode = schema.CurrencyCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize dataset import for {DatasetId}", request.DatasetId);
            return Result.Failure<FinalizeDataSetImportResponse>($"Failed to finalize dataset import: {ex.Message}");
        }
    }
}

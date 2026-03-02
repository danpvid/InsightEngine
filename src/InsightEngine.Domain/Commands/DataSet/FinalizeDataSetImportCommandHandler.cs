using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.Settings;
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
    private readonly IDuckDbMetadataAnalyzer _metadataAnalyzer;
    private readonly ICurrentUser _currentUser;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<FinalizeDataSetImportCommandHandler> _logger;

    public FinalizeDataSetImportCommandHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        IDataSetSanitizer dataSetSanitizer,
        IDataSetSchemaStore schemaStore,
        IDuckDbMetadataAnalyzer metadataAnalyzer,
        ICurrentUser currentUser,
        InsightEngineFeatures features,
        ILogger<FinalizeDataSetImportCommandHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _csvProfiler = csvProfiler;
        _dataSetSanitizer = dataSetSanitizer;
        _schemaStore = schemaStore;
        _metadataAnalyzer = metadataAnalyzer;
        _currentUser = currentUser;
        _features = features;
        _logger = logger;
    }

    public async Task<Result<FinalizeDataSetImportResponse>> Handle(
        FinalizeDataSetImportCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
            {
                return Result.Failure<FinalizeDataSetImportResponse>("Unauthorized");
            }

            var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
                ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
                : await _dataSetRepository.GetByIdAsync(request.DatasetId);
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

            var uniqueCandidates = await ResolveUniqueKeyCandidatesAsync(
                dataSet.StoredPath,
                profile,
                ignoredColumns,
                cancellationToken);

            var uniqueKeyColumn = ResolveUniqueKeyColumn(request.UniqueKeyColumn, uniqueCandidates, lookup);
            var syntheticKeyColumn = string.Empty;

            if (string.IsNullOrWhiteSpace(uniqueKeyColumn) && uniqueCandidates.Count == 0)
            {
                syntheticKeyColumn = BuildSyntheticKeyColumnName(allColumns);
                var updatedSize = await _dataSetSanitizer
                    .AddSequentialKeyColumnAsync(dataSet.StoredPath, syntheticKeyColumn, cancellationToken);
                dataSet.UpdateFileInfo(updatedSize);
                allColumns.Add(syntheticKeyColumn);
                lookup[syntheticKeyColumn] = syntheticKeyColumn;
                uniqueKeyColumn = syntheticKeyColumn;
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
                        HasPercentSign = confirmedType == InferredType.Percentage ? column.HasPercentSign : null,
                        PercentageScaleHint = confirmedType == InferredType.Percentage && !ignoredColumns.Contains(column.Name)
                            ? PercentageScaleHintDetector.Detect(column.Min, column.Max, column.Mean)
                            : null
                    };
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(syntheticKeyColumn))
            {
                schemaColumns.Add(new DatasetImportSchemaColumn
                {
                    Name = syntheticKeyColumn,
                    InferredType = InferredType.Integer,
                    ConfirmedType = InferredType.Integer,
                    IsIgnored = false,
                    IsTarget = false,
                    CurrencyCode = null,
                    HasPercentSign = null,
                    PercentageScaleHint = null
                });
            }

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
                UniqueKeyColumn = uniqueKeyColumn,
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
                UniqueKeyColumn = uniqueKeyColumn,
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

    private static string ResolveUniqueKeyColumn(
        string? requestedUniqueKeyColumn,
        IReadOnlyList<string> uniqueCandidates,
        IReadOnlyDictionary<string, string> lookup)
    {
        if (uniqueCandidates.Count == 1)
        {
            var single = uniqueCandidates[0];
            if (string.IsNullOrWhiteSpace(requestedUniqueKeyColumn))
            {
                return single;
            }

            if (!lookup.TryGetValue(requestedUniqueKeyColumn.Trim(), out var resolvedRequested)
                || !string.Equals(single, resolvedRequested, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unique key column must be '{single}'.");
            }

            return single;
        }

        if (uniqueCandidates.Count > 1)
        {
            if (string.IsNullOrWhiteSpace(requestedUniqueKeyColumn))
            {
                var options = string.Join(", ", uniqueCandidates.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException($"Multiple unique key candidates found: {options}. Please choose one in uniqueKeyColumn.");
            }

            if (!lookup.TryGetValue(requestedUniqueKeyColumn.Trim(), out var resolvedRequested)
                || !uniqueCandidates.Contains(resolvedRequested, StringComparer.OrdinalIgnoreCase))
            {
                var options = string.Join(", ", uniqueCandidates.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException($"Invalid uniqueKeyColumn '{requestedUniqueKeyColumn}'. Valid options: {options}.");
            }

            return resolvedRequested;
        }

        return string.Empty;
    }

    private async Task<List<string>> ResolveUniqueKeyCandidatesAsync(
        string csvPath,
        Domain.ValueObjects.DatasetProfile profile,
        HashSet<string> ignoredColumns,
        CancellationToken cancellationToken)
    {
        var indexColumns = profile.Columns
            .Where(column => !ignoredColumns.Contains(column.Name))
            .Select(column => new ColumnIndex
            {
                Name = column.Name,
                InferredType = (column.ConfirmedType ?? column.InferredType).NormalizeLegacy(),
                NullRate = column.NullRate,
                DistinctCount = column.DistinctCount
            })
            .ToList();

        if (indexColumns.Count == 0)
        {
            return [];
        }

        var sampleRows = Math.Max(5000, Math.Min(50000, profile.RowCount));
        var keyCandidates = await _metadataAnalyzer.ComputeCandidateKeysAsync(
            csvPath,
            indexColumns,
            sampleRows,
            maxSingleColumnCandidates: 30,
            maxCompositeCandidates: 0,
            cancellationToken: cancellationToken);

        return keyCandidates
            .Where(candidate => candidate.Columns.Count == 1)
            .Where(candidate => candidate.UniquenessRatio >= 0.999999)
            .Where(candidate => candidate.NullRate <= 0)
            .Select(candidate => candidate.Columns[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSyntheticKeyColumnName(IReadOnlyCollection<string> existingColumns)
    {
        const string baseName = "__row_id";
        var taken = new HashSet<string>(existingColumns, StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        var suffix = 1;
        while (taken.Contains($"{baseName}_{suffix}"))
        {
            suffix++;
        }

        return $"{baseName}_{suffix}";
    }
}

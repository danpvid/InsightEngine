using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetFormulaDiscoveryQueryHandler : IRequestHandler<GetDataSetFormulaDiscoveryQuery, Result<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>>
{
    private static readonly string[] PreferredTargetKeywords =
    [
        "revenue",
        "cost",
        "amount",
        "total",
        "score",
        "value"
    ];

    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexStore _indexStore;
    private readonly IDuckDbMetadataAnalyzer _metadataAnalyzer;
    private readonly IFormulaDiscoveryService _formulaDiscoveryService;
    private readonly ILogger<GetDataSetFormulaDiscoveryQueryHandler> _logger;

    public GetDataSetFormulaDiscoveryQueryHandler(
        IDataSetRepository dataSetRepository,
        IIndexStore indexStore,
        IDuckDbMetadataAnalyzer metadataAnalyzer,
        IFormulaDiscoveryService formulaDiscoveryService,
        ILogger<GetDataSetFormulaDiscoveryQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexStore = indexStore;
        _metadataAnalyzer = metadataAnalyzer;
        _formulaDiscoveryService = formulaDiscoveryService;
        _logger = logger;
    }

    public async Task<Result<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>> Handle(
        GetDataSetFormulaDiscoveryQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>("Dataset not found.");
            }

            if (!File.Exists(dataSet.StoredPath))
            {
                return Result.Failure<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>("Dataset file not found.");
            }

            var columns = await ResolveColumnsAsync(request.DatasetId, dataSet.StoredPath, request.SampleCap, cancellationToken);
            if (columns.Count == 0)
            {
                return Result.Failure<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>("No dataset columns available for formula discovery.");
            }

            var target = ResolveTargetColumn(request.Target, columns);
            if (string.IsNullOrWhiteSpace(target))
            {
                return Result.Failure<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>("No suitable numeric target column found for formula discovery.");
            }

            var result = await _formulaDiscoveryService.DiscoverAsync(
                request.DatasetId,
                target,
                request.ToOptions(),
                cancellationToken);

            return Result.Success(result, "Formula discovery completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover formulas for dataset {DatasetId}", request.DatasetId);
            return Result.Failure<Domain.Models.FormulaDiscovery.FormulaDiscoveryResult>($"Failed to discover formulas: {ex.Message}");
        }
    }

    private async Task<List<ColumnIndex>> ResolveColumnsAsync(
        Guid datasetId,
        string csvPath,
        int sampleCap,
        CancellationToken cancellationToken)
    {
        var index = await _indexStore.LoadAsync(datasetId, cancellationToken);
        if (index?.Columns?.Count > 0)
        {
            return index.Columns;
        }

        return await _metadataAnalyzer.ComputeColumnProfilesAsync(
            csvPath,
            maxColumns: 200,
            topValuesLimit: 20,
            sampleRows: Math.Clamp(sampleCap, 1000, 100000),
            cancellationToken: cancellationToken);
    }

    private static string ResolveTargetColumn(string? requestedTarget, IReadOnlyCollection<ColumnIndex> columns)
    {
        var numericColumns = columns
            .Where(column => column.InferredType == InferredType.Number)
            .ToList();

        if (numericColumns.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(requestedTarget))
        {
            var requested = numericColumns.FirstOrDefault(column =>
                string.Equals(column.Name, requestedTarget, StringComparison.OrdinalIgnoreCase));
            return requested?.Name ?? string.Empty;
        }

        var preferred = numericColumns
            .Where(column =>
            {
                var name = column.Name.ToLowerInvariant();
                return PreferredTargetKeywords.Any(keyword => name.Contains(keyword, StringComparison.Ordinal));
            })
            .OrderByDescending(column => column.NumericStats?.StdDev ?? 0d)
            .FirstOrDefault();

        if (preferred != null)
        {
            return preferred.Name;
        }

        return numericColumns
            .OrderByDescending(column => column.NumericStats?.StdDev ?? 0d)
            .ThenBy(column => column.Name)
            .First()
            .Name;
    }
}

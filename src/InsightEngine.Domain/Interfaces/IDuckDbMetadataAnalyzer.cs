using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Interfaces;

public interface IDuckDbMetadataAnalyzer
{
    Task<List<ColumnIndex>> ComputeColumnProfilesAsync(
        string csvPath,
        int maxColumns = 200,
        int topValuesLimit = 20,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default);

    Task<NumericStatsIndex?> ComputeNumericStatsAsync(
        string csvPath,
        string column,
        int sampleRows = 50000,
        bool includeDistributions = true,
        int histogramBins = 20,
        CancellationToken cancellationToken = default);

    Task<List<HistogramBinIndex>> ComputeHistogramAsync(
        string csvPath,
        string column,
        int bins = 20,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default);

    Task<DateStatsIndex?> ComputeDateStatsAsync(
        string csvPath,
        string column,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default);

    Task<StringStatsIndex?> ComputeStringStatsAsync(
        string csvPath,
        string column,
        bool includeStringPatterns = true,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default);

    Task<List<KeyCandidate>> ComputeCandidateKeysAsync(
        string csvPath,
        IReadOnlyCollection<ColumnIndex> columns,
        int sampleRows = 50000,
        int maxSingleColumnCandidates = 10,
        int maxCompositeCandidates = 10,
        CancellationToken cancellationToken = default);

    Task<CorrelationIndex> ComputeNumericCorrelationsAsync(
        string csvPath,
        IReadOnlyCollection<ColumnIndex> columns,
        int limitColumns = 50,
        int topK = 10,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default);
}

namespace InsightEngine.Domain.Interfaces;

public interface IDataSetSanitizer
{
    Task<long> RewriteWithoutColumnsAsync(
        string csvPath,
        IReadOnlyCollection<string> ignoredColumns,
        CancellationToken cancellationToken = default);

    Task<long> AddSequentialKeyColumnAsync(
        string csvPath,
        string keyColumnName,
        CancellationToken cancellationToken = default);
}

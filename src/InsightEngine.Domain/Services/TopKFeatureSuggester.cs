using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Services;

public sealed class TopKFeatureSuggester
{
    public int Suggest(
        IReadOnlyCollection<ColumnIndex> columns,
        string targetColumn,
        int maxK)
    {
        if (columns.Count == 0 || string.IsNullOrWhiteSpace(targetColumn))
        {
            return Math.Clamp(maxK, 1, 20);
        }

        var target = columns.FirstOrDefault(column =>
            string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return Math.Clamp(maxK, 1, 20);
        }

        var targetType = target.InferredType.NormalizeLegacy();
        var sameTypeCount = columns.Count(column =>
            !string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase)
            && !ColumnRoleHeuristics.IsRowIdLike(column.Name)
            && column.InferredType.NormalizeLegacy() == targetType);

        if (sameTypeCount <= 0)
        {
            return 1;
        }

        return Math.Min(Math.Max(1, sameTypeCount), Math.Max(1, maxK));
    }
}

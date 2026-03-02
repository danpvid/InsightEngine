namespace InsightEngine.Domain.Helpers;

public static class ColumnRoleHeuristics
{
    public static bool IsRowIdLike(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var normalized = columnName.Trim().ToLowerInvariant();
        return normalized is "__row_id" or "_row_id"
            || normalized.StartsWith("__row_id_", StringComparison.Ordinal)
            || normalized.StartsWith("_row_id_", StringComparison.Ordinal);
    }

    public static bool IsIdentifierLike(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var normalized = columnName.Trim().ToLowerInvariant();
        if (IsRowIdLike(normalized))
        {
            return true;
        }

        return normalized.Contains("id", StringComparison.Ordinal)
            || normalized.Contains("key", StringComparison.Ordinal)
            || normalized.Contains("uuid", StringComparison.Ordinal)
            || normalized.Contains("guid", StringComparison.Ordinal)
            || normalized.Contains("codigo", StringComparison.Ordinal)
            || normalized.EndsWith("_code", StringComparison.Ordinal);
    }
}

using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Helpers;

public static class DatasetSchemaProfileMapper
{
    public static DatasetProfile ApplySchema(DatasetProfile profile, DatasetImportSchema? schema)
    {
        if (schema == null)
        {
            profile.SchemaConfirmed = false;
            profile.IgnoredColumns = new List<string>();
            profile.TargetColumn = null;
            foreach (var column in profile.Columns)
            {
                column.ConfirmedType = column.InferredType.NormalizeLegacy();
            }

            return profile;
        }

        var schemaColumns = schema.Columns
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var column in profile.Columns)
        {
            if (!schemaColumns.TryGetValue(column.Name, out var mapped))
            {
                column.ConfirmedType = column.InferredType.NormalizeLegacy();
                continue;
            }

            column.InferredType = mapped.InferredType.NormalizeLegacy();
            column.ConfirmedType = mapped.ConfirmedType.NormalizeLegacy();
            column.IsIgnored = mapped.IsIgnored;
            column.IsTarget = mapped.IsTarget;
            column.CurrencyCode = mapped.CurrencyCode;
            column.HasPercentSign = mapped.HasPercentSign;
        }

        profile.Columns = profile.Columns.Where(column => !column.IsIgnored).ToList();
        profile.TargetColumn = schema.TargetColumn;
        profile.IgnoredColumns = schema.Columns
            .Where(column => column.IsIgnored)
            .Select(column => column.Name)
            .ToList();
        profile.SchemaConfirmed = schema.SchemaConfirmed;

        return profile;
    }
}

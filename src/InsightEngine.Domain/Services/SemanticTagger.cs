using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Services;

public class SemanticTagger : ISemanticTagger
{
    public SemanticTaggingResult Tag(IReadOnlyCollection<ColumnIndex> columns)
    {
        var result = new SemanticTaggingResult();
        if (columns.Count == 0)
        {
            return result;
        }

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var tags = InferColumnTags(column);
            column.SemanticTags = tags;
            result.ColumnTags[column.Name] = tags;

            foreach (var tag in tags)
            {
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
            }
        }

        var totalColumns = columns.Count;
        foreach (var tag in tagCounts.OrderByDescending(kvp => kvp.Value))
        {
            result.DatasetTags.Add(new DatasetTag
            {
                Name = tag.Key,
                Source = "semantic-tagger",
                Score = Math.Round(tag.Value / (double)totalColumns, 4)
            });
        }

        AddDomainHintTags(result.DatasetTags, result.ColumnTags);
        return result;
    }

    private static List<string> InferColumnTags(ColumnIndex column)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedName = column.Name.Trim().ToLowerInvariant();

        if (IsIdentifier(normalizedName, column))
        {
            tags.Add("identifier");
        }

        if (IsTimestamp(normalizedName, column))
        {
            tags.Add("timestamp");
        }

        if (IsAmount(normalizedName, column))
        {
            tags.Add("amount");
        }

        if (IsRate(normalizedName, column))
        {
            tags.Add("rate");
        }

        if (IsCategoryOrStatus(normalizedName, column))
        {
            tags.Add("category");
        }

        if (IsFreeText(column))
        {
            tags.Add("freeText");
        }

        if (column.InferredType == InferredType.Number && !tags.Contains("identifier") && !tags.Contains("rate"))
        {
            tags.Add("measure");
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsIdentifier(string normalizedName, ColumnIndex column)
    {
        var nameMatch = normalizedName.Contains("id") ||
                        normalizedName.Contains("uuid") ||
                        normalizedName.Contains("guid") ||
                        normalizedName.EndsWith("_key") ||
                        normalizedName.EndsWith("code");

        var highCardinality = column.DistinctCount >= 100 && column.NullRate <= 0.2;

        return nameMatch ||
               (column.InferredType is InferredType.String or InferredType.Category && highCardinality);
    }

    private static bool IsTimestamp(string normalizedName, ColumnIndex column)
    {
        if (column.InferredType == InferredType.Date)
        {
            return true;
        }

        return normalizedName.Contains("date") ||
               normalizedName.Contains("time") ||
               normalizedName.Contains("created") ||
               normalizedName.Contains("updated") ||
               normalizedName.Contains("dt_");
    }

    private static bool IsAmount(string normalizedName, ColumnIndex column)
    {
        if (column.InferredType != InferredType.Number)
        {
            return false;
        }

        return normalizedName.Contains("amount") ||
               normalizedName.Contains("price") ||
               normalizedName.Contains("cost") ||
               normalizedName.Contains("value") ||
               normalizedName.Contains("total") ||
               normalizedName.Contains("revenue") ||
               normalizedName.Contains("sales") ||
               normalizedName.Contains("margem");
    }

    private static bool IsRate(string normalizedName, ColumnIndex column)
    {
        if (column.InferredType != InferredType.Number)
        {
            return false;
        }

        return normalizedName.Contains("rate") ||
               normalizedName.Contains("pct") ||
               normalizedName.Contains("percent") ||
               normalizedName.Contains("ratio") ||
               normalizedName.Contains("taxa");
    }

    private static bool IsCategoryOrStatus(string normalizedName, ColumnIndex column)
    {
        if (column.InferredType == InferredType.Category)
        {
            return true;
        }

        return normalizedName.Contains("status") ||
               normalizedName.Contains("type") ||
               normalizedName.Contains("category") ||
               normalizedName.Contains("group") ||
               normalizedName.Contains("classe");
    }

    private static bool IsFreeText(ColumnIndex column)
    {
        if (column.InferredType != InferredType.String)
        {
            return false;
        }

        var avgLen = column.StringStats?.AvgLength ?? 0;
        var maxLen = column.StringStats?.MaxLength ?? 0;
        return avgLen >= 40 || maxLen >= 120;
    }

    private static void AddDomainHintTags(
        List<DatasetTag> datasetTags,
        IReadOnlyDictionary<string, List<string>> columnTags)
    {
        var hasTimestamp = columnTags.Values.Any(tags => tags.Contains("timestamp", StringComparer.OrdinalIgnoreCase));
        var hasAmount = columnTags.Values.Any(tags => tags.Contains("amount", StringComparer.OrdinalIgnoreCase));
        var hasCategory = columnTags.Values.Any(tags => tags.Contains("category", StringComparer.OrdinalIgnoreCase));
        var hasIdentifier = columnTags.Values.Any(tags => tags.Contains("identifier", StringComparer.OrdinalIgnoreCase));

        if (hasTimestamp)
        {
            datasetTags.Add(new DatasetTag { Name = "time-series", Source = "semantic-tagger", Score = 0.8 });
        }

        if (hasAmount && hasTimestamp)
        {
            datasetTags.Add(new DatasetTag { Name = "financial-trends", Source = "semantic-tagger", Score = 0.75 });
        }

        if (hasCategory && hasAmount)
        {
            datasetTags.Add(new DatasetTag { Name = "segmentation-ready", Source = "semantic-tagger", Score = 0.7 });
        }

        if (hasIdentifier)
        {
            datasetTags.Add(new DatasetTag { Name = "entity-centric", Source = "semantic-tagger", Score = 0.65 });
        }
    }
}

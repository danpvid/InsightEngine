namespace InsightEngine.Domain.Models.MetadataIndex;

public class SemanticTaggingResult
{
    public Dictionary<string, List<string>> ColumnTags { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<DatasetTag> DatasetTags { get; set; } = new();
}

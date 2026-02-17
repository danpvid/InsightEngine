using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Interfaces;

public interface ISemanticTagger
{
    SemanticTaggingResult Tag(IReadOnlyCollection<ColumnIndex> columns);
}

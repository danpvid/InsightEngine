using System.Security.Cryptography;
using System.Text;
using System.Linq;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Helpers;

/// <summary>
/// Helper para gerar hash de query specs para cache e deduplicação
/// </summary>
public static class QueryHashHelper
{
    /// <summary>
    /// Gera hash SHA256 estável de uma query spec
    /// </summary>
    public static string ComputeQueryHash(ChartRecommendation recommendation, Guid datasetId)
    {
        // Criar string canônica da query spec
        var seriesColumn = recommendation.Query.Series?.Column ?? string.Empty;
        var filtersCanonical = recommendation.Query.Filters.Count == 0
            ? string.Empty
            : string.Join(";", recommendation.Query.Filters.Select(f =>
                $"{f.Column}|{f.Operator}|{string.Join(",", f.Values)}"));

        var canonical = $"{datasetId}|" +
                       $"{recommendation.Chart.Type}|" +
                       $"{recommendation.Query.X.Column}|{recommendation.Query.X.Role}|{recommendation.Query.X.Bin}|" +
                       $"{recommendation.Query.Y.Column}|{recommendation.Query.Y.Role}|{recommendation.Query.Y.Aggregation}|" +
                       $"{seriesColumn}|" +
                       $"{filtersCanonical}";

        // Gerar SHA256
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = sha256.ComputeHash(bytes);
        
        // Converter para hex string
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public static string ComputeScenarioQueryHash(ScenarioRequest request, Guid datasetId)
    {
        var filtersCanonical = request.Filters.Count == 0
            ? string.Empty
            : string.Join(";", request.Filters.Select(f =>
                $"{f.Column}|{f.Operator}|{string.Join(",", f.Values)}"));

        var operationsCanonical = request.Operations.Count == 0
            ? string.Empty
            : string.Join(";", request.Operations.Select(op =>
                $"{op.Type}|{op.Column}|{string.Join(",", op.Values)}|{op.Factor}|{op.Constant}|{op.Min}|{op.Max}"));

        var canonical = $"{datasetId}|" +
                        $"simulate|" +
                        $"{request.TargetMetric}|{request.TargetDimension}|{request.Aggregation}|" +
                        $"{filtersCanonical}|" +
                        $"{operationsCanonical}";

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = sha256.ComputeHash(bytes);

        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

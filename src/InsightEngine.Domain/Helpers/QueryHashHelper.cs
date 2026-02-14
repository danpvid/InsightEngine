using System.Security.Cryptography;
using System.Text;
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
        var canonical = $"{datasetId}|" +
                       $"{recommendation.Chart.Type}|" +
                       $"{recommendation.Query.X.Column}|{recommendation.Query.X.Role}|{recommendation.Query.X.Bin}|" +
                       $"{recommendation.Query.Y.Column}|{recommendation.Query.Y.Role}|{recommendation.Query.Y.Aggregation}";

        // Gerar SHA256
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = sha256.ComputeHash(bytes);
        
        // Converter para hex string
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

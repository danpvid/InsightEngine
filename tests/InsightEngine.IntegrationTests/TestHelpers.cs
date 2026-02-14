using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChartExecutionResponse = InsightEngine.API.Models.ChartExecutionResponse;

namespace InsightEngine.IntegrationTests;

public static class TestHelpers
{
    /// <summary>
    /// JSON options matching the API configuration
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string CreateSimpleTestCsv(string? data = null)
    {
        data ??= @"20240101,15234.50,North
20240102,18567.30,North
20240103,22145.80,North
20240105,19876.20,North
20240108,25432.10,North";

        return $"date,sales,region\n{data}";
    }

    public static async Task<string> UploadTestDatasetAsync(HttpClient client, string? csvContent = null, string fileName = "test.csv")
    {
        csvContent ??= CreateSimpleTestCsv();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", fileName);

        var response = await client.PostAsync("/api/v1/datasets", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(JsonOptions);
        return result!.Data.DatasetId.ToString();
    }

    public static async Task<List<ChartRecommendation>?> GetRecommendationsAsync(HttpClient client, string datasetId)
    {
        var response = await client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChartRecommendation>>>(JsonOptions);
        return result?.Data;
    }

    public static async Task<ChartExecutionResponse?> ExecuteChartAsync(HttpClient client, string datasetId, string recommendationId)
    {
        var response = await client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{recommendationId}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(JsonOptions);
        return result?.Data;
    }
}

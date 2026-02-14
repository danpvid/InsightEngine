using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using ChartExecutionResponse = InsightEngine.API.Models.ChartExecutionResponse;

namespace InsightEngine.IntegrationTests;

/// <summary>
/// Testes específicos para execução de diferentes tipos de charts
/// </summary>
[Collection("RealApi")]
public class ChartExecutionTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;
    private string? _datasetId;

    public ChartExecutionTests(RealApiFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(_fixture.BaseUrl)
        };
        
        // Upload dataset uma vez para todos os testes
        _datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteLineChart_WithTimeSeriesData_ReturnsValidOption()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var lineRec = recommendations!.FirstOrDefault(r => r.Chart.Type == ChartType.Line);

        if (lineRec == null)
        {
            // Skip test se não houver recomendação Line
            return;
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{lineRec.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        result!.Data.Option.Should().NotBeNull();
        result.Data.Option.XAxis.Should().ContainKey("type");
        result.Data.Option.XAxis["type"].ToString().Should().Be("time");
        result.Data.Option.Series.Should().NotBeEmpty();
        result.Data.Option.Series![0]["type"].ToString().Should().Be("line");
        result.Data.Meta.ChartType.Should().Be("line"); // ECharts uses lowercase
        result.Data.Meta.DuckDbMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBarChart_WithCategoryData_ReturnsValidOption()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var barRec = recommendations!.FirstOrDefault(r => r.Chart.Type == ChartType.Bar);

        if (barRec == null)
        {
            // Skip test se não houver recomendação Bar
            return;
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{barRec.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        result!.Data.Option.Should().NotBeNull();
        result.Data.Option.XAxis.Should().ContainKey("type");
        result.Data.Option.XAxis["type"].ToString().Should().Be("category");
        result.Data.Option.XAxis.Should().ContainKey("data"); // Categories array
        result.Data.Option.Series.Should().NotBeEmpty();
        result.Data.Option.Series![0]["type"].ToString().Should().Be("bar");
        result.Data.Meta.ChartType.Should().Be("bar"); // ECharts uses lowercase
        result.Data.Meta.RowCountReturned.Should().BeLessOrEqualTo(20); // TopN limit
    }

    [Fact]
    public async Task ExecuteScatterChart_WithMeasureData_ReturnsValidOption()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var scatterRec = recommendations!.FirstOrDefault(r => r.Chart.Type == ChartType.Scatter);

        if (scatterRec == null)
        {
            // Skip test se não houver recomendação Scatter
            return;
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{scatterRec.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        result!.Data.Option.Should().NotBeNull();
        result.Data.Option.XAxis.Should().ContainKey("type");
        result.Data.Option.XAxis["type"].ToString().Should().Be("value");
        result.Data.Option.YAxis.Should().ContainKey("type");
        result.Data.Option.YAxis["type"].ToString().Should().Be("value");
        result.Data.Option.Series.Should().NotBeEmpty();
        result.Data.Option.Series![0]["type"].ToString().Should().Be("scatter");
        result.Data.Meta.ChartType.Should().Be("Scatter");
        result.Data.Meta.RowCountReturned.Should().BeLessOrEqualTo(2000); // MaxPoints sampling
    }

    [Fact]
    public async Task ExecuteHistogramChart_WithMeasureData_ReturnsValidOption()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var histogramRec = recommendations!.FirstOrDefault(r => r.Chart.Type == ChartType.Histogram);

        if (histogramRec == null)
        {
            // Skip test se não houver recomendação Histogram
            return;
        }

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{histogramRec.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        result!.Data.Option.Should().NotBeNull();
        result.Data.Option.XAxis.Should().ContainKey("type");
        result.Data.Option.XAxis["type"].ToString().Should().Be("category");
        result.Data.Option.XAxis.Should().ContainKey("data"); // Bin labels
        result.Data.Option.YAxis.Should().ContainKey("name");
        result.Data.Option.YAxis["name"].ToString().Should().Be("Frequency");
        result.Data.Option.Series.Should().NotBeEmpty();
        result.Data.Option.Series![0]["type"].ToString().Should().Be("bar");
        result.Data.Meta.ChartType.Should().Be("bar"); // Histogram uses bar series type
        result.Data.Meta.RowCountReturned.Should().BeLessOrEqualTo(20); // Default bins
    }

    [Fact]
    public async Task ExecuteChart_MultipleChartTypes_AllReturnValidMeta()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);

        // Act & Assert - Execute todos os tipos disponíveis
        foreach (var rec in recommendations!.Take(4)) // Limitar para não demorar muito
        {
            var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{rec.Id}");
            
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
            
            // Validar meta completo
            result!.Data.Meta.Should().NotBeNull();
            result.Data.Meta.RowCountReturned.Should().BeGreaterThan(0);
            result.Data.Meta.ExecutionMs.Should().BeGreaterThan(0);
            result.Data.Meta.DuckDbMs.Should().BeGreaterThan(0);
            result.Data.Meta.DuckDbMs.Should().BeLessThan(result.Data.Meta.ExecutionMs); // DuckDB time < total time
            result.Data.Meta.ChartType.Should().NotBeNullOrEmpty();
            result.Data.Meta.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            result.Data.Meta.QueryHash.Should().NotBeNullOrEmpty();
            result.Data.Meta.QueryHash.Should().HaveLength(64); // SHA256 hex string
        }
    }

    [Fact]
    public async Task ExecuteChart_SameRecommendationTwice_ReturnsSameQueryHash()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var rec = recommendations!.First();

        // Act - Execute twice
        var response1 = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{rec.Id}");
        var response2 = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{rec.Id}");

        // Assert
        var result1 = await response1.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        var result2 = await response2.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);

        result1!.Data.Meta.QueryHash.Should().Be(result2!.Data.Meta.QueryHash);
    }

    [Fact]
    public async Task ExecuteChart_ChecksDebugSqlInDevelopment()
    {
        // Arrange
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, _datasetId!);
        var rec = recommendations!.First();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{_datasetId}/charts/{rec.Id}");

        // Assert
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        // DebugSql deve estar presente em Development
        result!.Data.DebugSql.Should().NotBeNullOrEmpty();
        result.Data.DebugSql.Should().Contain("SELECT");
    }
}

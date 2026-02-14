using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;
using ChartExecutionResponse = InsightEngine.API.Models.ChartExecutionResponse;

namespace InsightEngine.IntegrationTests;

/// <summary>
/// Testes de performance e edge cases
/// </summary>
[Collection("RealApi")]
public class PerformanceTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;

    public PerformanceTests(RealApiFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(_fixture.BaseUrl),
            Timeout = TimeSpan.FromMinutes(2) // Timeout maior para performance tests
        };
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadDataset_MeasuresResponseTime()
    {
        // Arrange
        var csvContent = TestHelpers.CreateSimpleTestCsv();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "perf-test.csv");

        var sw = Stopwatch.StartNew();

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        sw.Stop();
        _output.WriteLine($"Upload took {sw.ElapsedMilliseconds}ms");
        
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete in under 5 seconds
    }

    [Fact]
    public async Task GetDatasetProfile_MeasuresResponseTime()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var sw = Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");

        // Assert
        sw.Stop();
        _output.WriteLine($"Profile took {sw.ElapsedMilliseconds}ms");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(3000); // Should complete in under 3 seconds
    }

    [Fact]
    public async Task GetRecommendations_MeasuresResponseTime()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var sw = Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");

        // Assert
        sw.Stop();
        _output.WriteLine($"Recommendations took {sw.ElapsedMilliseconds}ms");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    [Fact]
    public async Task ExecuteChart_MeasuresResponseTime()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var rec = recommendations!.First();

        var sw = Stopwatch.StartNew();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{rec.Id}");

        // Assert
        sw.Stop();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        _output.WriteLine($"Chart execution took {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"DuckDB time: {result!.Data.Meta.DuckDbMs}ms");
        _output.WriteLine($"Total execution time (meta): {result.Data.Meta.ExecutionMs}ms");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
        
        // Meta telemetry should be reasonable
        result.Data.Meta.DuckDbMs.Should().BeLessThan(2000);
        result.Data.Meta.ExecutionMs.Should().BeLessThan(3000);
    }

    [Fact]
    public async Task ExecuteMultipleCharts_SequentiallyMeasuresPerformance()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);

        var sw = Stopwatch.StartNew();
        var executions = 0;

        // Act - Execute all recommendations
        foreach (var rec in recommendations!.Take(5))
        {
            var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{rec.Id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            executions++;
        }

        // Assert
        sw.Stop();
        _output.WriteLine($"Executed {executions} charts in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {sw.ElapsedMilliseconds / executions}ms per chart");
        
        (sw.ElapsedMilliseconds / executions).Should().BeLessThan(5000);
    }

    [Fact]
    public async Task ExecuteSameChart_MultipleTimes_ConsistentPerformance()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var rec = recommendations!.First();

        var times = new List<long>();

        // Act - Execute 5 times
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{rec.Id}");
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            times.Add(sw.ElapsedMilliseconds);
        }

        // Assert
        var avgTime = times.Average();
        var maxTime = times.Max();
        var minTime = times.Min();

        _output.WriteLine($"Execution times: {string.Join(", ", times)}ms");
        _output.WriteLine($"Average: {avgTime:F2}ms, Min: {minTime}ms, Max: {maxTime}ms");
        
        // Performance should be consistent (max should not be > 3x min)
        maxTime.Should().BeLessThan(minTime * 3);
    }

    [Fact]
    public async Task ConcurrentExecutions_HandleMultipleRequestsEfficiently()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var rec = recommendations!.First();

        var sw = Stopwatch.StartNew();

        // Act - Execute 10 concurrent requests
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{rec.Id}")
        );

        var responses = await Task.WhenAll(tasks);

        // Assert
        sw.Stop();
        _output.WriteLine($"10 concurrent executions took {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {sw.ElapsedMilliseconds / 10}ms per request (concurrent)");

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
        sw.ElapsedMilliseconds.Should().BeLessThan(15000); // Should handle concurrent load
    }

    [Fact]
    public async Task LargeDataset_HandlesPerformanceGracefully()
    {
        // Arrange - CSV com 1000 linhas
        var csvContent = "date,sales,region\n";
        for (int i = 1; i <= 1000; i++)
        {
            csvContent += $"2024-{(i % 12) + 1:D2}-{(i % 28) + 1:D2},{1000 + (i % 5000)},Region{i % 5}\n";
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "large-dataset.csv");

        var sw = Stopwatch.StartNew();

        // Act
        var uploadResponse = await _client.PostAsync("/api/v1/datasets", content);

        sw.Stop();
        _output.WriteLine($"Large dataset upload took {sw.ElapsedMilliseconds}ms");

        if (uploadResponse.StatusCode != HttpStatusCode.Created)
        {
            _output.WriteLine($"Upload failed with status {uploadResponse.StatusCode}");
            return;
        }

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
        var datasetId = uploadResult!.Data.DatasetId.ToString();

        // Profile
        sw.Restart();
        var profileResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");
        sw.Stop();
        _output.WriteLine($"Large dataset profile took {sw.ElapsedMilliseconds}ms");

        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Chart execution
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var rec = recommendations!.First();

        sw.Restart();
        var chartResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{rec.Id}");
        sw.Stop();
        _output.WriteLine($"Large dataset chart execution took {sw.ElapsedMilliseconds}ms");

        chartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinimalDataset_SingleRow_HandlesGracefully()
    {
        // Arrange - CSV com apenas 1 linha de dados
        var csvContent = @"date,sales,region
2024-01-01,1000,SP";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "minimal.csv");

        // Act
        var uploadResponse = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        if (uploadResponse.StatusCode != HttpStatusCode.Created)
        {
            _output.WriteLine("Single row dataset rejected (expected behavior)");
            return;
        }

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
        var datasetId = uploadResult!.Data.DatasetId.ToString();

        // Should still be able to get profile
        var profileResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DatasetWithNullValues_HandlesGracefully()
    {
        // Arrange - CSV com valores nulos
        var csvContent = @"date,sales,region
2024-01-01,1000,SP
2024-01-02,,RJ
2024-01-03,3000,
,2000,MG";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "nulls.csv");

        // Act
        var uploadResponse = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        uploadResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);

        if (uploadResponse.StatusCode == HttpStatusCode.Created)
        {
            var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
            var datasetId = uploadResult!.Data.DatasetId.ToString();

            // Should handle nulls gracefully in profile
            var profileResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");
            profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

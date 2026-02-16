using FluentAssertions;
using InsightEngine.API.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
public class SimulationTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public SimulationTests(RealApiFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(_fixture.BaseUrl)
        };

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Simulate_WithMultiplyMetric_ReturnsBaselineAndSimulatedSeries()
    {
        var csv = string.Join('\n',
        [
            "date,sales,region",
            "2024-01-01,100,A",
            "2024-01-02,150,A",
            "2024-01-01,80,B",
            "2024-01-02,120,B"
        ]);

        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "simulate-multiply.csv");

        var payload = new
        {
            targetMetric = "sales",
            targetDimension = "region",
            aggregation = "Sum",
            operations = new[]
            {
                new
                {
                    type = "MultiplyMetric",
                    factor = 2.0
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/simulate", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SimulationResponseDto>>(TestHelpers.JsonOptions);
        result!.Data.Should().NotBeNull();
        result.Data!.BaselineSeries.Should().NotBeEmpty();
        result.Data.SimulatedSeries.Should().HaveCount(result.Data.BaselineSeries.Count);
        result.Data.DeltaSummary.AverageDeltaPercent.Should().BeGreaterThan(90);
        result.Data.QueryHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task Simulate_WithTooManyOperations_ReturnsBadRequest()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var payload = new
        {
            targetMetric = "sales",
            targetDimension = "region",
            operations = new[]
            {
                new { type = "MultiplyMetric", factor = 1.1 },
                new { type = "MultiplyMetric", factor = 1.2 },
                new { type = "MultiplyMetric", factor = 1.3 },
                new { type = "MultiplyMetric", factor = 1.4 }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/simulate", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(TestHelpers.JsonOptions);
        result!.Message.ToLowerInvariant().Should().Contain("operations");
    }

    public class SimulationResponseDto
    {
        public string QueryHash { get; set; } = string.Empty;
        public List<SeriesPointDto> BaselineSeries { get; set; } = new();
        public List<SeriesPointDto> SimulatedSeries { get; set; } = new();
        public DeltaSummaryDto DeltaSummary { get; set; } = new();
    }

    public class SeriesPointDto
    {
        public string Dimension { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class DeltaSummaryDto
    {
        public double AverageDeltaPercent { get; set; }
    }
}

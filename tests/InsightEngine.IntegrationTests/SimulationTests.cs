using FluentAssertions;
using InsightEngine.API.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        result!.Errors.Should().NotBeEmpty();
        result.Errors.Any(error => error.Message.ToLowerInvariant().Contains("operations")).Should().BeTrue();
    }

    [Fact]
    public async Task Simulate_WithPropagateTargetFormulaAndInputChange_RecomputesFormulaTarget()
    {
        var csv = string.Join('\n',
        [
            "region,unit_price,quantity,total",
            "A,10,2,20",
            "A,12,3,36",
            "B,8,5,40",
            "B,7,6,42"
        ]);

        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "simulate-formula-propagation.csv");

        var buildResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/index:build",
            new { sampleRows = 5000, includeStringPatterns = true, includeDistributions = true });
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var inferenceResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/formula-inference/run",
            new
            {
                targetColumn = "total",
                mode = "Auto",
                options = new { maxColumns = 3, maxDepth = 2, epsilonAbs = 0.0001, includePercentageColumns = false }
            });
        inferenceResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/simulate",
            new
            {
                targetMetric = "unit_price",
                targetDimension = "region",
                aggregation = "Sum",
                propagateTargetFormula = true,
                operations = new[]
                {
                    new { type = "MultiplyMetric", factor = 2.0 }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = payload.RootElement.GetProperty("data");
        data.GetProperty("targetMetric").GetString().Should().Be("total");
        data.GetProperty("appliedFormulaExpression").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("deltaSummary").GetProperty("changedPoints").GetInt32().Should().BeGreaterThan(0);
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

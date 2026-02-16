using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Enums;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using ChartExecutionResponse = InsightEngine.API.Models.ChartExecutionResponse;
using ChartRecommendation = InsightEngine.Domain.Models.ChartRecommendation;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
[Trait("Category", "Smoke")]
public class SmokeFlowTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public SmokeFlowTests(RealApiFixture fixture)
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
    public async Task HealthEndpoints_ReturnHealthyAndReadyStatus()
    {
        var healthResponse = await _client.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var readyResponse = await _client.GetAsync("/health/ready");
        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CoreJourney_UploadRecommendationsChartExploreAndSimulate_Succeeds()
    {
        // upload
        var csv = string.Join('\n',
        [
            "date,sales,profit,region,category",
            "2024-01-01,100,30,North,A",
            "2024-01-02,150,45,North,A",
            "2024-01-03,210,63,South,B",
            "2024-01-04,130,39,South,A",
            "2024-01-05,170,52,North,B",
            "2024-01-06,190,57,West,C",
            "2024-01-07,200,60,West,C"
        ]);

        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "smoke-flow.csv");

        // recommendations
        var recommendationsResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");
        recommendationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var recommendationsEnvelope = await recommendationsResponse.Content
            .ReadFromJsonAsync<ApiResponse<List<ChartRecommendation>>>(TestHelpers.JsonOptions);
        recommendationsEnvelope.Should().NotBeNull();
        recommendationsEnvelope!.Data.Should().NotBeEmpty();

        var recommendation = recommendationsEnvelope.Data!
            .FirstOrDefault(item => item.Chart.Type is ChartType.Line or ChartType.Bar)
            ?? recommendationsEnvelope.Data!.First();

        // chart (baseline)
        var baselineResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{recommendation.Id}");
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var baselineEnvelope = await baselineResponse.Content
            .ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        baselineEnvelope.Should().NotBeNull();
        baselineEnvelope!.Data.Should().NotBeNull();
        baselineEnvelope.Data!.Meta.RowCountReturned.Should().BeGreaterThan(0);
        baselineEnvelope.Data.Meta.QueryHash.Should().HaveLength(64);

        // chart (exploration params)
        var aggregation = recommendation.Chart.Type == ChartType.Bar ? "Count" : "Avg";
        var explorationUrl =
            $"/api/v1/datasets/{datasetId}/charts/{recommendation.Id}" +
            $"?aggregation={aggregation}" +
            "&metricY=sales" +
            "&groupBy=region" +
            "&filters=region|eq|North";

        var explorationResponse = await _client.GetAsync(explorationUrl);
        explorationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var explorationEnvelope = await explorationResponse.Content
            .ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        explorationEnvelope.Should().NotBeNull();
        explorationEnvelope!.Data.Should().NotBeNull();
        explorationEnvelope.Data!.Meta.RowCountReturned.Should().BeGreaterThan(0);
        explorationEnvelope.Data.Meta.QueryHash.Should().HaveLength(64);
        explorationEnvelope.Data.Meta.QueryHash.Should().NotBe(baselineEnvelope.Data.Meta.QueryHash);

        // simulation
        var simulationPayload = new
        {
            targetMetric = "sales",
            targetDimension = "region",
            aggregation = "Sum",
            operations = new[]
            {
                new
                {
                    type = "MultiplyMetric",
                    factor = 1.1
                }
            }
        };

        var simulationResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/simulate",
            simulationPayload);

        simulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var simulationEnvelope = await simulationResponse.Content
            .ReadFromJsonAsync<ApiResponse<SimulationResponseDto>>(TestHelpers.JsonOptions);
        simulationEnvelope.Should().NotBeNull();
        simulationEnvelope!.Data.Should().NotBeNull();
        simulationEnvelope.Data!.BaselineSeries.Should().NotBeEmpty();
        simulationEnvelope.Data.SimulatedSeries.Should().HaveCount(simulationEnvelope.Data.BaselineSeries.Count);
        simulationEnvelope.Data.DeltaSummary.AverageDeltaPercent.Should().BeGreaterThan(0);
    }

    public class SimulationResponseDto
    {
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

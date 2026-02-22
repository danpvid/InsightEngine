using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using ChartExecutionResponse = InsightEngine.API.Models.ChartExecutionResponse;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
public class DataSetIntegrationTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public DataSetIntegrationTests(RealApiFixture fixture)
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
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadDataset_WithValidCsv_ReturnsSuccess()
    {
        // Arrange
        var csvContent = TestHelpers.CreateSimpleTestCsv();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test.csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
        
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.DatasetId.Should().NotBeEmpty();
        result.Data.OriginalFileName.Should().Be("test.csv");
    }

    [Fact]
    public async Task UploadDataset_WithInvalidFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("invalid data"), "file", "test.txt");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDatasetProfile_AfterUpload_ReturnsProfileData()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<DatasetProfile>>(TestHelpers.JsonOptions);
        
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.RowCount.Should().BeGreaterThan(0);
        result.Data.Columns.Should().NotBeEmpty();
        result.Data.Columns.Should().HaveCount(3); // date, sales, region
    }

    [Fact]
    public async Task GetDatasetProfile_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var invalidId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{invalidId}/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDatasetRecommendations_ReturnsChartRecommendations()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ChartRecommendation>>>(TestHelpers.JsonOptions);
        
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Should().NotBeEmpty();
        
        // Verify recommendation structure
        var firstRec = result.Data.First();
        firstRec.Id.Should().NotBeNullOrEmpty();
        firstRec.Title.Should().NotBeNullOrEmpty();
        firstRec.Chart.Should().NotBeNull();
        firstRec.Chart.Type.Should().BeOneOf(ChartType.Line, ChartType.Bar, ChartType.Scatter, ChartType.Histogram);
        firstRec.Score.Should().BeInRange(0, 1);
        firstRec.ImpactScore.Should().BeInRange(0, 1);
        firstRec.ScoreCriteria.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteChart_WithValidRecommendation_ReturnsChartData()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var firstRec = recommendations!.First();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{firstRec.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Option.Should().NotBeNull();
        result.Data.Option.Series.Should().NotBeNull();
        result.Data.Meta.Should().NotBeNull();
        result.Data.Meta.RowCountReturned.Should().BeGreaterThan(0);
        result.Data.Meta.ExecutionMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteChart_WithInvalidRecommendationId_ReturnsBadRequest()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var invalidRecId = "invalid-recommendation-id"; // Formato inválido (não segue padrão rec_###)

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{invalidRecId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WorkflowTest_UploadProfileRecommendExecute_Success()
    {
        // 1. Upload dataset
        var csvContent = TestHelpers.CreateSimpleTestCsv();
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StringContent(csvContent), "file", "workflow-test.csv");
        
        var uploadResponse = await _client.PostAsync("/api/v1/datasets", uploadContent);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
        var datasetId = uploadResult!.Data.DatasetId.ToString();

        // 2. Get profile
        var profileResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/profile");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profileResult = await profileResponse.Content.ReadFromJsonAsync<ApiResponse<DatasetProfile>>(TestHelpers.JsonOptions);
        profileResult!.Data.Should().NotBeNull();

        // 3. Get recommendations
        var recsResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");
        recsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recsResult = await recsResponse.Content.ReadFromJsonAsync<ApiResponse<List<ChartRecommendation>>>(TestHelpers.JsonOptions);
        recsResult!.Data.Should().NotBeEmpty();

        // 4. Execute first chart
        var firstRec = recsResult.Data.First();
        var chartResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{firstRec.Id}");
        chartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var chartResult = await chartResponse.Content.ReadFromJsonAsync<ApiResponse<ChartExecutionResponse>>(TestHelpers.JsonOptions);
        chartResult!.Data.Option.Should().NotBeNull();
        chartResult.Data.Meta.Should().NotBeNull();
    }

    [Fact]
    public async Task ConcurrentRequests_MultipleClients_HandleSuccessfully()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var recommendations = await TestHelpers.GetRecommendationsAsync(_client, datasetId);
        var firstRec = recommendations!.First();

        // Act - Execute multiple concurrent requests
        var tasks = Enumerable.Range(0, 5).Select(_ => 
            _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{firstRec.Id}")
        );

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task GetSchema_ForLegacyDataset_BackfillsDefaultSchema()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/schema");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = payload.RootElement.GetProperty("data");

        data.GetProperty("datasetId").GetGuid().ToString().Should().Be(datasetId);
        data.GetProperty("schemaConfirmed").GetBoolean().Should().BeFalse();
        data.GetProperty("ignoredColumnsCount").GetInt32().Should().Be(0);

        var columns = data.GetProperty("columns").EnumerateArray().ToList();
        columns.Should().HaveCount(3);
        columns.Should().Contain(column => column.GetProperty("name").GetString() == "sales");
        data.GetProperty("targetColumn").GetString().Should().Be("sales");
    }

    [Fact]
    public async Task PreviewAndFinalize_Workflow_Succeeds()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var previewResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/preview?sampleSize=5");
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalizePayload = new
        {
            targetColumn = "sales",
            ignoredColumns = Array.Empty<string>(),
            columnTypeOverrides = new Dictionary<string, string>(),
            currencyCode = "BRL"
        };

        var finalizeResponse = await _client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/finalize", finalizePayload);
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var finalizePayloadJson = JsonDocument.Parse(await finalizeResponse.Content.ReadAsStringAsync());
        var finalizeData = finalizePayloadJson.RootElement.GetProperty("data");
        finalizeData.GetProperty("targetColumn").GetString().Should().Be("sales");
        finalizeData.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task FormulaInference_RunAndGetEndpoints_ShouldReturnPersistedMetadata()
    {
        var csv = "unit_price,quantity,total\n10,2,20\n12,3,36\n15,4,60\n8,5,40\n7,6,42\n";
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "formula-run.csv");

        var buildResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/index:build",
            new
            {
                maxColumnsForCorrelation = 10,
                topKEdgesPerColumn = 5,
                sampleRows = 5000,
                includeStringPatterns = true,
                includeDistributions = true
            });
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var runResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/formula-inference/run",
            new
            {
                targetColumn = "total",
                mode = "Auto",
                options = new
                {
                    maxColumns = 3,
                    maxDepth = 2,
                    epsilonAbs = 0.0001,
                    includePercentageColumns = false
                }
            });

        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var runPayload = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
        var runData = runPayload.RootElement.GetProperty("data");
        runData.GetProperty("targetColumn").GetString().Should().Be("total");
        runData.TryGetProperty("formulaInference", out var formulaInferenceElement).Should().BeTrue();
        formulaInferenceElement.ValueKind.Should().NotBe(JsonValueKind.Null);

        var getResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/formula-inference");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var getPayload = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var getData = getPayload.RootElement.GetProperty("data");
        getData.TryGetProperty("formulaInference", out var persistedInference).Should().BeTrue();
        persistedInference.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task FinalizeImport_WithFormulaInferenceEnabled_ShouldTriggerInferenceFlow()
    {
        var csv = "unit_price,quantity,total\n10,2,20\n12,3,36\n15,4,60\n8,5,40\n7,6,42\n";
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "formula-finalize.csv");

        var buildResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/index:build",
            new
            {
                maxColumnsForCorrelation = 10,
                topKEdgesPerColumn = 5,
                sampleRows = 5000,
                includeStringPatterns = true,
                includeDistributions = true
            });
        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalizeResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/finalize",
            new
            {
                targetColumn = "total",
                ignoredColumns = Array.Empty<string>(),
                columnTypeOverrides = new Dictionary<string, string>(),
                currencyCode = "BRL",
                formulaInference = new
                {
                    enabled = true,
                    maxColumns = 3,
                    maxDepth = 2,
                    epsilonAbs = 0.0001,
                    includePercentageColumns = false
                }
            });

        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var finalizeJson = JsonDocument.Parse(await finalizeResponse.Content.ReadAsStringAsync());
        var data = finalizeJson.RootElement.GetProperty("data");
        data.GetProperty("targetColumn").GetString().Should().Be("total");
        data.TryGetProperty("formulaInference", out var formulaInference).Should().BeTrue();
        formulaInference.GetProperty("triggered").GetBoolean().Should().BeTrue();
        formulaInference.GetProperty("status").GetString().Should().Be("completed");
    }
}

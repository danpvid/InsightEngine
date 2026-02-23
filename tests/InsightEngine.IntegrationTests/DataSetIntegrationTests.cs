using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Globalization;
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
        var uniqueKeyColumn = await ResolveUniqueKeyFromPreviewAsync(datasetId, 5);

        var finalizePayload = new
        {
            targetColumn = "sales",
            uniqueKeyColumn,
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
        var uniqueKeyColumn = await ResolveUniqueKeyFromPreviewAsync(datasetId, 5);

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
                uniqueKeyColumn,
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

    [Fact]
    public async Task FinalizeImport_WithoutUniqueCandidate_CreatesSequentialKey_AndBuildIndexSucceeds()
    {
        var csv = "a,b,c\n1,10,x\n1,10,x\n1,20,y\n2,20,y\n";
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "row-id-regression.csv");

        var finalizeResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/finalize",
            new
            {
                targetColumn = (string?)null,
                uniqueKeyColumn = (string?)null,
                ignoredColumns = Array.Empty<string>(),
                columnTypeOverrides = new Dictionary<string, string>(),
                currencyCode = "BRL"
            });

        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var finalizeJson = JsonDocument.Parse(await finalizeResponse.Content.ReadAsStringAsync());
        var finalizeData = finalizeJson.RootElement.GetProperty("data");
        var uniqueKeyColumn = finalizeData.GetProperty("uniqueKeyColumn").GetString();
        uniqueKeyColumn.Should().NotBeNullOrWhiteSpace();
        uniqueKeyColumn!.Should().StartWith("__row_id");

        var buildResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/index:build",
            new
            {
                sampleRows = 5000,
                includeStringPatterns = true,
                includeDistributions = true
            });

        buildResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var buildJson = JsonDocument.Parse(await buildResponse.Content.ReadAsStringAsync());
        buildJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeImport_WithWithIndexMode_ShouldReturnIndexBuildInfo()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var uniqueKeyColumn = await ResolveUniqueKeyFromPreviewAsync(datasetId, 5);

        var finalizeResponse = await _client.PostAsJsonAsync(
            $"/api/v1/datasets/{datasetId}/finalize",
            new
            {
                importMode = "with-index",
                targetColumn = "sales",
                uniqueKeyColumn,
                ignoredColumns = Array.Empty<string>(),
                columnTypeOverrides = new Dictionary<string, string>(),
                currencyCode = "BRL"
            });

        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var finalizeJson = JsonDocument.Parse(await finalizeResponse.Content.ReadAsStringAsync());
        var data = finalizeJson.RootElement.GetProperty("data");
        data.GetProperty("importMode").GetString().Should().Be("with-index");
        data.TryGetProperty("indexBuild", out var indexBuild).Should().BeTrue();
        indexBuild.GetProperty("triggered").GetBoolean().Should().BeTrue();
        indexBuild.GetProperty("status").GetString().Should().Be("ready");
    }

    [Fact]
    public async Task RawRows_FieldStatsTopRanges_ShouldBeSortedByRangeStart()
    {
        var csv = string.Join('\n',
        [
            "bucket,sales",
            "A,1",
            "A,2",
            "A,3",
            "A,100",
            "B,110",
            "B,120",
            "B,130",
            "C,240",
            "C,250",
            "C,260",
            "D,390",
            "D,400",
            "D,410",
            "E,520",
            "E,530",
            "E,540",
            "F,650",
            "F,660",
            "F,670",
            "G,780",
            "G,790",
            "G,800",
            "H,910",
            "H,920",
            "H,930"
        ]);

        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client, csv, "raw-ranges-sort.csv");
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/rows?fieldStatsColumn=sales&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var topRanges = payload.RootElement
            .GetProperty("data")
            .GetProperty("fieldStats")
            .GetProperty("topRanges")
            .EnumerateArray()
            .Select(item => item.GetProperty("from").GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => double.Parse(value!, NumberStyles.Any, CultureInfo.InvariantCulture))
            .ToArray();

        topRanges.Should().NotBeEmpty();
        topRanges.Should().BeInAscendingOrder();
    }

    private async Task<string?> ResolveUniqueKeyFromPreviewAsync(string datasetId, int sampleSize)
    {
        var previewResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/preview?sampleSize={sampleSize}");
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        if (!previewJson.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        if (!data.TryGetProperty("suggestedUniqueKeyCandidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return candidates
            .EnumerateArray()
            .Select(item => item.GetString())
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }
}

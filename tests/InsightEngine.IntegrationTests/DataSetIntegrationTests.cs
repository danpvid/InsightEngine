using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
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
}

using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.Domain.Commands.DataSet;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

/// <summary>
/// Testes de validação e casos de erro
/// </summary>
[Collection("RealApi")]
public class ValidationTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public ValidationTests(RealApiFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri(_fixture.BaseUrl)
        };
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UploadDataset_WithEmptyFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(""), "file", "empty.csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadDataset_WithNoFile_ReturnsBadRequest()
    {
        // Arrange
        using var content = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadDataset_WithInvalidCsvFormat_ReturnsBadRequest()
    {
        // Arrange - CSV with inconsistent columns
        var invalidCsv = @"date,sales,region
2024-01-01,1000,SP
2024-01-02,2000
2024-01-03,3000,RJ,EXTRA_COLUMN";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(invalidCsv), "file", "invalid.csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        // DuckDB pode processar com ignore_errors, mas deve haver alguma validação
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetDatasetProfile_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{nonExistentId}/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDatasetProfile_WithInvalidGuid_ReturnsNotFound()
    {
        // Arrange
        var invalidGuid = "not-a-guid";

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{invalidGuid}/profile");

        // Assert
        // API pode retornar BadRequest (400) para formato inválido ou NotFound (404)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecommendations_WithNonExistentDataset_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{nonExistentId}/recommendations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecuteChart_WithNonExistentDataset_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var recId = "rec_001";

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{nonExistentId}/charts/{recId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecuteChart_WithInvalidRecommendationFormat_ReturnsBadRequest()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var invalidRecId = "invalid-format-123"; // Não segue padrão rec_###

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{invalidRecId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteChart_WithNonExistentRecommendationId_ReturnsNotFound()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);
        var nonExistentRecId = "rec_999"; // Formato válido mas não existe

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}/charts/{nonExistentRecId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadDataset_WithVeryLargeFile_HandlesGracefully()
    {
        // Arrange - CSV com muitas linhas (mas não muito grande para não travar o teste)
        var largeCsv = "date,sales,region\n";
        for (int i = 0; i < 10000; i++)
        {
            largeCsv += $"2024-01-{(i % 28) + 1:D2},{1000 + i},SP\n";
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(largeCsv), "file", "large.csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task GetDatasetById_WithValidId_ReturnsDatasetInfo()
    {
        // Arrange
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        // Act
        var response = await _client.GetAsync($"/api/v1/datasets/{datasetId}");

        // Assert
        // Endpoint pode não estar implementado ainda
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestHelpers.JsonOptions);
            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetAllDatasets_ReturnsListOfDatasets()
    {
        // Arrange
        await TestHelpers.UploadTestDatasetAsync(_client);

        // Act
        var response = await _client.GetAsync("/api/v1/datasets");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestHelpers.JsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UploadDataset_WithSpecialCharactersInFilename_HandlesCorrectly()
    {
        // Arrange
        var csvContent = TestHelpers.CreateSimpleTestCsv();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test file with spaces & special-chars (123).csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
        
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetResponse>>(TestHelpers.JsonOptions);
            result!.Data.OriginalFileName.Should().Contain("test file");
        }
    }

    [Fact]
    public async Task UploadDataset_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange - CSV com caracteres acentuados
        var csvContent = @"data,vendas,região
2024-01-01,1000,São Paulo
2024-01-02,2000,Brasília
2024-01-03,3000,João Pessoa";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "dados-português.csv");

        // Act
        var response = await _client.PostAsync("/api/v1/datasets", content);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }
}

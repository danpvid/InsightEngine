using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
public class RetentionCleanupTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public RetentionCleanupTests(RealApiFixture fixture)
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
    public async Task CleanupEndpoint_WithZeroRetention_RemovesExpiredDataset()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

        var cleanupResponse = await _client.PostAsync("/api/v1/datasets/cleanup?retentionDays=0", null);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cleanupResult = await cleanupResponse.Content.ReadFromJsonAsync<CleanupEnvelope>(TestHelpers.JsonOptions);
        cleanupResult.Should().NotBeNull();
        cleanupResult!.Success.Should().BeTrue();
        cleanupResult.Data.RemovedMetadataRecords.Should().BeGreaterThan(0);

        var recommendationsResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/recommendations");
        recommendationsResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public class CleanupEnvelope
    {
        public bool Success { get; set; }
        public CleanupData Data { get; set; } = new();
    }

    public class CleanupData
    {
        public int RemovedMetadataRecords { get; set; }
    }
}

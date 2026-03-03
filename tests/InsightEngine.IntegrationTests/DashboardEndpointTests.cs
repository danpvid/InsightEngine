using FluentAssertions;
using InsightEngine.API.Models;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
public class DashboardEndpointTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public DashboardEndpointTests(RealApiFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Dashboard_ShouldReturnNotFound_WhenDatasetBelongsToAnotherUser()
    {
        var userA = await RegisterUserAsync($"dash_owner_a_{Guid.NewGuid():N}@test.local");
        var userB = await RegisterUserAsync($"dash_owner_b_{Guid.NewGuid():N}@test.local");

        var datasetId = await UploadDatasetAsync(CreateAuthorizedClient(userA.AccessToken));
        using var userBClient = CreateAuthorizedClient(userB.AccessToken);

        var response = await userBClient.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_ShouldReturnComposedData_WhenIndexExists()
    {
        var user = await RegisterUserAsync($"dash_index_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);

        var csv = """
                  date,sales,cost,quantity,region
                  2024-01-01,100,60,10,North
                  2024-01-02,120,70,11,North
                  2024-01-03,140,80,12,South
                  2024-01-04,160,95,13,South
                  2024-01-05,170,100,15,West
                  """;
        var datasetId = await UploadDatasetAsync(client, csv, "dashboard-index.csv");

        var buildIndexResponse = await client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/index:build", new { });
        buildIndexResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = payload.RootElement.GetProperty("data");
        data.GetProperty("dataset").GetProperty("id").GetGuid().Should().Be(datasetId);
        data.GetProperty("metadata").GetProperty("indexAvailable").GetBoolean().Should().BeTrue();
        data.GetProperty("kpis").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Dashboard_ShouldHandleMissingIndexGracefully()
    {
        var user = await RegisterUserAsync($"dash_no_index_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);

        var datasetId = await UploadDatasetAsync(client, TestHelpers.CreateSimpleTestCsv(), "dashboard-no-index.csv");
        var response = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = payload.RootElement.GetProperty("data");
        data.GetProperty("metadata").GetProperty("indexAvailable").GetBoolean().Should().BeFalse();
        data.GetProperty("charts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Dashboard_ShouldReturnBadRequest_WhenDatasetIdIsMissing()
    {
        var user = await RegisterUserAsync($"dash_validation_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);

        var response = await client.GetAsync("/api/v1/dashboard?datasetId=");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dashboard_ShouldReturnSamePayload_WhenCacheIsHit()
    {
        var user = await RegisterUserAsync($"dash_cache_hit_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);
        var datasetId = await UploadDatasetAsync(client, TestHelpers.CreateSimpleTestCsv(), "dashboard-cache-hit.csv");

        var buildIndexResponse = await client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/index:build", new { });
        buildIndexResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstRecommendationsAt = firstJson.RootElement.GetProperty("data").GetProperty("generation").GetProperty("recommendationsGeneratedAt").GetString();

        var second = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondRecommendationsAt = secondJson.RootElement.GetProperty("data").GetProperty("generation").GetProperty("recommendationsGeneratedAt").GetString();

        secondRecommendationsAt.Should().Be(firstRecommendationsAt);
    }

    [Fact]
    public async Task Dashboard_ShouldInvalidateCache_WhenIndexIsRegenerated()
    {
        var user = await RegisterUserAsync($"dash_cache_invalidate_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);
        var datasetId = await UploadDatasetAsync(client, TestHelpers.CreateSimpleTestCsv(), "dashboard-cache-invalidate.csv");

        var buildFirst = await client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/index:build", new { });
        buildFirst.StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstRecommendationsAt = firstJson.RootElement.GetProperty("data").GetProperty("generation").GetProperty("recommendationsGeneratedAt").GetString();

        await Task.Delay(1100);

        var buildSecond = await client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/index:build", new { });
        buildSecond.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondRecommendationsAt = secondJson.RootElement.GetProperty("data").GetProperty("generation").GetProperty("recommendationsGeneratedAt").GetString();

        secondRecommendationsAt.Should().NotBe(firstRecommendationsAt);
    }

    [Fact]
    public async Task Dashboard_ShouldRecompute_WhenOnlyLegacyCacheVersionExists()
    {
        var user = await RegisterUserAsync($"dash_cache_version_{Guid.NewGuid():N}@test.local");
        using var client = CreateAuthorizedClient(user.AccessToken);
        var datasetId = await UploadDatasetAsync(client, TestHelpers.CreateSimpleTestCsv(), "dashboard-cache-version.csv");
        var buildIndexResponse = await client.PostAsJsonAsync($"/api/v1/datasets/{datasetId}/index:build", new { });
        buildIndexResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var userId = ExtractUserId(user.AccessToken);
        await InsertLegacyDashboardCacheAsync(userId, datasetId);

        var response = await client.GetAsync($"/api/v1/dashboard?datasetId={datasetId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dbPath = ResolveMetadataDbPath();
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DashboardCache WHERE OwnerUserId = $owner AND DatasetId = $dataset AND Version = $version;";
        command.Parameters.AddWithValue("$owner", userId.ToString());
        command.Parameters.AddWithValue("$dataset", datasetId.ToString());
        command.Parameters.AddWithValue("$version", "dashboard-v2");
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0);
    }

    private async Task<AuthTokensData> RegisterUserAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123!",
            displayName = "Dashboard Test User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthTokensData>>(TestHelpers.JsonOptions);
        payload.Should().NotBeNull();
        payload!.Data.Should().NotBeNull();
        return payload.Data!;
    }

    private static async Task<Guid> UploadDatasetAsync(HttpClient client, string? csv = null, string fileName = "dashboard.csv")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(csv ?? TestHelpers.CreateSimpleTestCsv()), "file", fileName);

        var upload = await client.PostAsync("/api/v1/datasets", content);
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await upload.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetData>>(TestHelpers.JsonOptions);
        payload.Should().NotBeNull();
        return payload!.Data!.DatasetId;
    }

    private HttpClient CreateAuthorizedClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static Guid ExtractUserId(string accessToken)
    {
        var parts = accessToken.Split('.');
        var payload = parts[1];
        var normalized = payload.Replace('-', '+').Replace('_', '/');
        while (normalized.Length % 4 != 0)
        {
            normalized += "=";
        }

        var bytes = Convert.FromBase64String(normalized);
        using var json = JsonDocument.Parse(bytes);
        var sub = json.RootElement.GetProperty("sub").GetString();
        return Guid.Parse(sub!);
    }

    private static async Task InsertLegacyDashboardCacheAsync(Guid ownerUserId, Guid datasetId)
    {
        var dbPath = ResolveMetadataDbPath();
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO DashboardCache
            (Id, OwnerUserId, DatasetId, Version, PayloadJson, SourceDatasetUpdatedAt, SourceFingerprint, CreatedAt)
            VALUES
            ($id, $owner, $dataset, $version, $payload, $sourceUpdatedAt, $fingerprint, $createdAt);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$owner", ownerUserId.ToString());
        command.Parameters.AddWithValue("$dataset", datasetId.ToString());
        command.Parameters.AddWithValue("$version", "dashboard-v1-legacy");
        command.Parameters.AddWithValue("$payload", "{\"kpis\":[],\"charts\":[],\"tables\":{\"topFeatures\":[],\"dataQuality\":[],\"topCategories\":[]},\"insights\":{\"warnings\":[],\"executiveBullets\":[],\"nextActions\":[]},\"metadata\":{\"indexAvailable\":true,\"recommendationsAvailable\":false,\"formulaAvailable\":false},\"generation\":{}}");
        command.Parameters.AddWithValue("$sourceUpdatedAt", DateTime.UtcNow.AddDays(1).ToString("O"));
        command.Parameters.AddWithValue("$fingerprint", "legacy");
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveMetadataDbPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "InsightEngine.API", "bin");
            if (Directory.Exists(candidate))
            {
                var file = Directory.GetFiles(candidate, "insightengine-metadata.db", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(file))
                {
                    return file;
                }
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Metadata database not found.");
    }

    private class AuthTokensData
    {
        public string AccessToken { get; set; } = string.Empty;
    }

    private class UploadDataSetData
    {
        public Guid DatasetId { get; set; }
    }
}

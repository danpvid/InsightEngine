using FluentAssertions;
using InsightEngine.API.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

[Collection("RealApi")]
public class AuthFlowAndOwnershipTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public AuthFlowAndOwnershipTests(RealApiFixture fixture)
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
    public async Task Register_Login_Refresh_Logout_ShouldRotateAndRevokeRefreshToken()
    {
        var email = $"user_{Guid.NewGuid():N}@test.local";
        const string password = "StrongPass123!";

        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            displayName = "User Register"
        });

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<ApiResponse<AuthTokensData>>(TestHelpers.JsonOptions);
        registerPayload.Should().NotBeNull();
        registerPayload!.Data.Should().NotBeNull();

        var initialAccess = registerPayload.Data!.AccessToken;
        var initialRefresh = registerPayload.Data.RefreshToken;

        var refreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            accessToken = initialAccess,
            refreshToken = initialRefresh
        });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<ApiResponse<AuthTokensData>>(TestHelpers.JsonOptions);
        refreshPayload.Should().NotBeNull();
        refreshPayload!.Data!.RefreshToken.Should().NotBe(initialRefresh);

        var staleRefreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            accessToken = initialAccess,
            refreshToken = initialRefresh
        });
        staleRefreshResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var logoutResponse = await _client.PostAsJsonAsync("/api/v1/auth/logout", new
        {
            refreshToken = refreshPayload.Data.RefreshToken
        });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var revokedRefreshResponse = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            accessToken = refreshPayload.Data.AccessToken,
            refreshToken = refreshPayload.Data.RefreshToken
        });
        revokedRefreshResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Datasets_ShouldBeIsolatedByOwner_WhenAuthenticated()
    {
        var userA = await RegisterUserAsync($"owner_a_{Guid.NewGuid():N}@test.local");
        var userB = await RegisterUserAsync($"owner_b_{Guid.NewGuid():N}@test.local");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(TestHelpers.CreateSimpleTestCsv()), "file", "owner-a.csv");

        using var userAClient = CreateAuthorizedClient(userA.AccessToken);
        var uploadResponse = await userAClient.PostAsync("/api/v1/datasets", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UploadDataSetData>>(TestHelpers.JsonOptions);
        uploadPayload.Should().NotBeNull();
        var ownedDatasetId = uploadPayload!.Data!.DatasetId;

        var userAList = await userAClient.GetAsync("/api/v1/datasets");
        userAList.StatusCode.Should().Be(HttpStatusCode.OK);
        var userAListPayload = await userAList.Content.ReadFromJsonAsync<ApiResponse<List<DatasetSummaryData>>>(TestHelpers.JsonOptions);
        userAListPayload!.Data!.Select(x => x.DatasetId).Should().Contain(ownedDatasetId);

        using var userBClient = CreateAuthorizedClient(userB.AccessToken);
        var userBList = await userBClient.GetAsync("/api/v1/datasets");
        userBList.StatusCode.Should().Be(HttpStatusCode.OK);
        var userBListPayload = await userBList.Content.ReadFromJsonAsync<ApiResponse<List<DatasetSummaryData>>>(TestHelpers.JsonOptions);
        userBListPayload!.Data!.Select(x => x.DatasetId).Should().NotContain(ownedDatasetId);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_WhenDtoInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "invalid-email",
            password = "123",
            displayName = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<AuthTokensData> RegisterUserAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "StrongPass123!",
            displayName = "Integration User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthTokensData>>(TestHelpers.JsonOptions);
        payload.Should().NotBeNull();
        payload!.Data.Should().NotBeNull();
        return payload.Data!;
    }

    private HttpClient CreateAuthorizedClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private class AuthTokensData
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }

    private class UploadDataSetData
    {
        public Guid DatasetId { get; set; }
    }

    private class DatasetSummaryData
    {
        public Guid DatasetId { get; set; }
    }
}

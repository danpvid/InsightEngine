using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Services;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class SemanticTaggerTests
{
    [Fact]
    public void Tag_ShouldAssignExpectedColumnAndDatasetTags()
    {
        var columns = new List<ColumnIndex>
        {
            new() { Name = "customer_id", InferredType = InferredType.String, DistinctCount = 5000, NullRate = 0 },
            new() { Name = "created_at", InferredType = InferredType.Date, DistinctCount = 365, NullRate = 0 },
            new() { Name = "total_amount", InferredType = InferredType.Number, DistinctCount = 4000, NullRate = 0.01 },
            new()
            {
                Name = "description",
                InferredType = InferredType.String,
                DistinctCount = 4500,
                NullRate = 0.05,
                StringStats = new StringStatsIndex { AvgLength = 80, MaxLength = 240 }
            }
        };

        var tagger = new SemanticTagger();
        var result = tagger.Tag(columns);

        result.ColumnTags["customer_id"].Should().Contain("identifier");
        result.ColumnTags["created_at"].Should().Contain("timestamp");
        result.ColumnTags["total_amount"].Should().Contain("amount");
        result.ColumnTags["description"].Should().Contain("freeText");

        result.DatasetTags.Select(tag => tag.Name).Should().Contain("time-series");
        result.DatasetTags.Select(tag => tag.Name).Should().Contain("financial-trends");
    }
}

public class DuckDbMetadataAnalyzerTests
{
    [Fact]
    public async Task ComputeCandidateKeysAsync_ShouldIdentifyHighUniquenessColumn()
    {
        var csvPath = CreateTempCsv("""
            order_id,customer,status,amount
            O001,C001,OPEN,10
            O002,C001,CLOSED,20
            O003,C002,OPEN,10
            O004,C003,OPEN,30
            O005,C004,CLOSED,25
            """);

        try
        {
            var analyzer = new DuckDbMetadataAnalyzer(NullLogger<DuckDbMetadataAnalyzer>.Instance);
            var columns = await analyzer.ComputeColumnProfilesAsync(csvPath, sampleRows: 5000);
            var keyCandidates = await analyzer.ComputeCandidateKeysAsync(csvPath, columns, sampleRows: 5000);

            keyCandidates.Should().NotBeEmpty();
            keyCandidates.Any(candidate =>
                    candidate.Columns.Count == 1 &&
                    candidate.Columns[0].Equals("order_id", StringComparison.OrdinalIgnoreCase) &&
                    candidate.UniquenessRatio > 0.9)
                .Should()
                .BeTrue();
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task ComputeNumericCorrelationsAsync_ShouldReturnCappedTopEdges()
    {
        var csvPath = CreateCorrelationCsv();

        try
        {
            var analyzer = new DuckDbMetadataAnalyzer(NullLogger<DuckDbMetadataAnalyzer>.Instance);
            var columns = await analyzer.ComputeColumnProfilesAsync(csvPath, sampleRows: 5000);
            var correlationIndex = await analyzer.ComputeNumericCorrelationsAsync(
                csvPath,
                columns,
                limitColumns: 4,
                topK: 1,
                sampleRows: 5000);

            correlationIndex.Edges.Should().NotBeEmpty();
            correlationIndex.Edges.Count.Should().BeLessOrEqualTo(4);

            var strongestPearson = correlationIndex.Edges
                .Where(edge => edge.Method == CorrelationMethod.Pearson)
                .OrderByDescending(edge => Math.Abs(edge.Score))
                .FirstOrDefault();

            strongestPearson.Should().NotBeNull();
            Math.Abs(strongestPearson!.Score).Should().BeGreaterThan(0.95);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static string CreateCorrelationCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var lines = new List<string> { "a,b,c,d" };

        for (var i = 1; i <= 120; i++)
        {
            var a = i;
            var b = i * 2;
            var c = 240 - i;
            var d = i % 7;
            lines.Add($"{a},{b},{c},{d}");
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    private static string CreateTempCsv(string csv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, csv.Replace("            ", string.Empty));
        return path;
    }
}

public class IndexStoreTests
{
    [Fact]
    public async Task SaveAndLoad_ShouldPersistIndexAndStatusFiles()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"insight-index-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storagePath);

        try
        {
            var fileStorage = new InMemoryFileStorageService(storagePath);
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var store = new IndexStore(
                fileStorage,
                memoryCache,
                Options.Create(new InsightEngineSettings { CacheTtlSeconds = 120 }),
                NullLogger<IndexStore>.Instance);

            var datasetId = Guid.NewGuid();
            var index = new DatasetIndex
            {
                DatasetId = datasetId,
                BuiltAtUtc = DateTime.UtcNow,
                Version = "metadata-index/v1",
                RowCount = 10,
                ColumnCount = 2
            };

            await store.SaveAsync(index);
            await store.SaveStatusAsync(new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = IndexBuildState.Ready,
                BuiltAtUtc = index.BuiltAtUtc,
                Message = "ready"
            });

            var loadedIndex = await store.LoadAsync(datasetId);
            var loadedStatus = await store.LoadStatusAsync(datasetId);

            loadedIndex.Should().NotBeNull();
            loadedIndex!.DatasetId.Should().Be(datasetId);
            loadedStatus.Status.Should().Be(IndexBuildState.Ready);

            var indexPath = Path.Combine(storagePath, datasetId.ToString("D"), "index.json");
            var statusPath = Path.Combine(storagePath, datasetId.ToString("D"), "index.status.json");
            File.Exists(indexPath).Should().BeTrue();
            File.Exists(statusPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
            }
        }
    }

    private sealed class InMemoryFileStorageService : IFileStorageService
    {
        private readonly string _storagePath;

        public InMemoryFileStorageService(string storagePath)
        {
            _storagePath = storagePath;
        }

        public Task<(string storedPath, long fileSize)> SaveFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteFileAsync(string fileName)
            => throw new NotSupportedException();

        public Task<Stream?> GetFileStreamAsync(string fileName)
            => throw new NotSupportedException();

        public Task<bool> FileExistsAsync(string fileName)
            => Task.FromResult(File.Exists(GetFullPath(fileName)));

        public string GetFullPath(string fileName) => Path.Combine(_storagePath, fileName);

        public string GetStoragePath() => _storagePath;
    }
}

[Collection("RealApi")]
public class MetadataIndexSmokeFlowTests : IAsyncLifetime
{
    private readonly RealApiFixture _fixture;
    private HttpClient _client = null!;

    public MetadataIndexSmokeFlowTests(RealApiFixture fixture)
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
    public async Task BuildAndFetchIndex_ShouldProduceIndexArtifacts()
    {
        var datasetId = await TestHelpers.UploadTestDatasetAsync(_client);

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

        var buildContent = await buildResponse.Content.ReadAsStringAsync();
        buildResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "index build should succeed for the uploaded dataset. Response body: {0}",
            buildContent);

        var statusResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/index/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusEnvelope = await statusResponse.Content.ReadFromJsonAsync<ApiEnvelope<DatasetIndexStatus>>(TestHelpers.JsonOptions);
        statusEnvelope.Should().NotBeNull();
        statusEnvelope!.Success.Should().BeTrue();
        statusEnvelope.Data.Status.Should().Be(IndexBuildState.Ready);

        var indexResponse = await _client.GetAsync($"/api/v1/datasets/{datasetId}/index");
        indexResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var indexEnvelope = await indexResponse.Content.ReadFromJsonAsync<ApiEnvelope<DatasetIndex>>(TestHelpers.JsonOptions);
        indexEnvelope.Should().NotBeNull();
        indexEnvelope!.Success.Should().BeTrue();
        indexEnvelope.Data.DatasetId.ToString().Should().Be(datasetId);
        indexEnvelope.Data.Columns.Should().NotBeEmpty();
        indexEnvelope.Data.Limits.Should().NotBeNull();
    }

    private sealed class ApiEnvelope<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; } = default!;
    }
}

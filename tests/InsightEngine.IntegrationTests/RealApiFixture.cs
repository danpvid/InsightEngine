using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class RealApiFixture : IAsyncLifetime
{
    private Process? _apiProcess;
    private readonly string _apiUrl = "http://localhost:5123";
    private readonly HashSet<string> _baselineDatasetIds = new(StringComparer.OrdinalIgnoreCase);
    
    public string BaseUrl => _apiUrl;
    
    public async Task InitializeAsync()
    {
        // Start the API in background
        var apiPath = ResolveApiProjectPath();
        
        _apiProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-build --urls {_apiUrl}",
                WorkingDirectory = apiPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        _apiProcess.Start();
        
        // Wait for API to be ready (max 30 seconds)
        var httpClient = new HttpClient();
        var started = false;
        for (int i = 0; i < 60; i++)
        {
            try
            {
                var response = await httpClient.GetAsync($"{_apiUrl}/swagger/index.html");
                if (response.IsSuccessStatusCode)
                {
                    started = true;
                    break;
                }
            }
            catch
            {
                // API not ready yet
            }
            
            await Task.Delay(500);
        }
        
        if (!started)
        {
            throw new Exception("API failed to start within 30 seconds");
        }

        var baselineIds = await ListDatasetIdsAsync();
        _baselineDatasetIds.Clear();
        foreach (var datasetId in baselineIds)
        {
            _baselineDatasetIds.Add(datasetId);
        }
    }

    private static string ResolveApiProjectPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "InsightEngine.API");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/InsightEngine.API from test runtime directory.");
    }
    
    public async Task DisposeAsync()
    {
        await CleanupDatasetsCreatedDuringTestsAsync();

        if (_apiProcess != null && !_apiProcess.HasExited)
        {
            _apiProcess.Kill(entireProcessTree: true);
            _apiProcess.WaitForExit(5000);
            _apiProcess.Dispose();
        }
    }

    private async Task CleanupDatasetsCreatedDuringTestsAsync()
    {
        if (_apiProcess is null || _apiProcess.HasExited)
        {
            return;
        }

        try
        {
            var currentIds = await ListDatasetIdsAsync();
            var createdDuringTests = currentIds
                .Except(_baselineDatasetIds, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (createdDuringTests.Count == 0)
            {
                return;
            }

            using var client = new HttpClient { BaseAddress = new Uri(_apiUrl) };
            foreach (var datasetId in createdDuringTests)
            {
                try
                {
                    await client.DeleteAsync($"/api/v1/datasets/{datasetId}");
                }
                catch
                {
                    // Ignore cleanup failures to avoid masking test results.
                }
            }
        }
        catch
        {
            // Ignore cleanup failures to avoid masking test results.
        }
    }

    private async Task<HashSet<string>> ListDatasetIdsAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_apiUrl) };
        using var response = await client.GetAsync("/api/v1/datasets");
        if (!response.IsSuccessStatusCode)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        var datasetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!json.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return datasetIds;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("datasetId", out var datasetIdElement))
            {
                continue;
            }

            var datasetId = datasetIdElement.GetString();
            if (!string.IsNullOrWhiteSpace(datasetId))
            {
                datasetIds.Add(datasetId);
            }
        }

        return datasetIds;
    }
}

[CollectionDefinition("RealApi")]
public class RealApiCollection : ICollectionFixture<RealApiFixture>
{
}

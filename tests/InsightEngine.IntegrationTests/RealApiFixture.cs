using System.Diagnostics;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class RealApiFixture : IAsyncLifetime
{
    private Process? _apiProcess;
    private readonly string _apiUrl = "http://localhost:5123";
    
    public string BaseUrl => _apiUrl;
    
    public async Task InitializeAsync()
    {
        // Start the API in background
        var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "InsightEngine.API");
        var apiPath = Path.GetFullPath(projectPath);
        
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
    }
    
    public Task DisposeAsync()
    {
        if (_apiProcess != null && !_apiProcess.HasExited)
        {
            _apiProcess.Kill(entireProcessTree: true);
            _apiProcess.WaitForExit(5000);
            _apiProcess.Dispose();
        }
        
        return Task.CompletedTask;
    }
}

[CollectionDefinition("RealApi")]
public class RealApiCollection : ICollectionFixture<RealApiFixture>
{
}

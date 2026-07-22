using System.Net.Http.Json;

namespace Zip02.Tests.AwsTestHarness;

public sealed class LocalStackFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    public Uri Endpoint { get; } = new(Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566");

    public bool IsReady { get; private set; }

    public async Task InitializeAsync()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = Endpoint,
            Timeout = TimeSpan.FromSeconds(5)
        };

        var stopAt = DateTimeOffset.UtcNow.Add(StartupTimeout);
        while (DateTimeOffset.UtcNow < stopAt)
        {
            try
            {
                var response = await httpClient.GetAsync("/_localstack/health");
                if (response.IsSuccessStatusCode)
                {
                    var health = await response.Content.ReadFromJsonAsync<LocalStackHealthResponse>();
                    if (health?.Services is not null &&
                        IsServiceRunning(health.Services, "dynamodb") &&
                        IsServiceRunning(health.Services, "sns") &&
                        IsServiceRunning(health.Services, "events"))
                    {
                        IsReady = true;
                        return;
                    }
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(RetryDelay);
        }

        IsReady = false;
    }


    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private static bool IsServiceRunning(IReadOnlyDictionary<string, string> services, string serviceName)
    {
        return services.TryGetValue(serviceName, out var status) &&
               string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LocalStackHealthResponse
    {
        public IReadOnlyDictionary<string, string>? Services { get; init; }
    }
}

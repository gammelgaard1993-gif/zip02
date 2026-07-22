using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Testcontainers.LocalStack;

namespace Zip02.Tests.Infrastructure;

/// <summary>
/// Shared xUnit class fixture that starts a LocalStack container once per test class
/// and provisions the DynamoDB single table used by all integration tests.
/// Tests must declare: <c>IClassFixture&lt;LocalStackFixture&gt;</c>
/// Call <see cref="SkipIfUnavailable"/> at the start of each test when Docker may not be present.
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private LocalStackContainer? _container;

    public bool IsAvailable { get; private set; }

    public string ServiceUrl { get; private set; } = "http://localhost:4566";

    public string TableName { get; } = $"zip02-integration-{Guid.NewGuid():N}";

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new LocalStackBuilder()
                .WithImage("localstack/localstack:3")
                .Build();

            await _container.StartAsync();

            ServiceUrl = _container.GetConnectionString();
            IsAvailable = true;

            DynamoDb = BuildClient(ServiceUrl);

            await DynamoDbTableProvisioner.CreateTableAsync(DynamoDb, TableName);
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        DynamoDb?.Dispose();

        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns false when LocalStack is unavailable.
    /// Tests should guard with: <c>if (!fixture.IsAvailable) return;</c>
    /// </summary>
    public bool CheckAvailable() => IsAvailable;

    internal static IAmazonDynamoDB BuildClient(string serviceUrl)
    {
        var credentials = new BasicAWSCredentials("test", "test");
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = RegionEndpoint.EUWest1.SystemName
        };
        return new AmazonDynamoDBClient(credentials, config);
    }
}

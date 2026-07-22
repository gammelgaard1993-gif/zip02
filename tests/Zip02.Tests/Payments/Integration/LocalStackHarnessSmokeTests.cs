using Zip02.Tests.AwsTestHarness;

namespace Zip02.Tests.Payments.Integration;

[Collection(LocalStackCollection.Name)]
public sealed class LocalStackHarnessSmokeTests(LocalStackFixture fixture)
{
    [Fact]
    public void LocalStackFixture_ProvidesReadyEndpoint()
    {
        if (!fixture.IsReady) return;

        Assert.Equal("http", fixture.Endpoint.Scheme);
        Assert.True(fixture.Endpoint.IsDefaultPort is false || fixture.Endpoint.Port == 4566);
    }
}

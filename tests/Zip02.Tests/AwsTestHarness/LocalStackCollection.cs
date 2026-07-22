namespace Zip02.Tests.AwsTestHarness;

[CollectionDefinition(Name)]
public sealed class LocalStackCollection : ICollectionFixture<LocalStackFixture>
{
    public const string Name = "LocalStack";
}

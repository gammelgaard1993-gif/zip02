using zip02.Services.Events;
using zip02.Services.Events.DynamoDB;
using Zip02.Tests.Infrastructure;

namespace Zip02.Tests.Events.Integration;

[Trait("Category", "Integration")]
public sealed class DynamoDbEventStoreTests : IClassFixture<LocalStackFixture>
{
    private readonly LocalStackFixture _localStack;

    public DynamoDbEventStoreTests(LocalStackFixture localStack)
    {
        _localStack = localStack;
    }

    [Fact]
    public void Create_ThenGet_RoundTripsEvent()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbEventStore(_localStack.DynamoDb, _localStack.TableName);

        var created = store.Create(new CreateEventRequest
        {
            Name = "Dynamo Event",
            StartAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            EndAtUtc = DateTimeOffset.UtcNow.AddHours(3),
            Capacity = 120,
            Geofence = new GeofenceRequest
            {
                Latitude = 55.6761,
                Longitude = 12.5683,
                RadiusMeters = 500
            }
        });

        var loaded = store.Get(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Equal("Dynamo Event", loaded.Name);
        Assert.Equal(120, loaded.Capacity);
        Assert.Equal(55.6761, loaded.Geofence.Latitude, 4);
        Assert.Equal(12.5683, loaded.Geofence.Longitude, 4);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbEventStore(_localStack.DynamoDb, _localStack.TableName);

        var loaded = store.Get(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public void Update_ExistingEvent_ChangesSelectedFields()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbEventStore(_localStack.DynamoDb, _localStack.TableName);

        var created = store.Create(new CreateEventRequest
        {
            Name = "Initial Name",
            StartAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            EndAtUtc = DateTimeOffset.UtcNow.AddHours(4),
            Capacity = 100,
            Geofence = new GeofenceRequest
            {
                Latitude = 55.0,
                Longitude = 12.0,
                RadiusMeters = 250
            }
        });

        var updated = store.Update(created.Id, new UpdateEventRequest
        {
            Name = "Updated Name",
            Capacity = 175,
            Geofence = new GeofenceRequest
            {
                Latitude = 56.0,
                Longitude = 13.0,
                RadiusMeters = 300
            }
        });

        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(175, updated.Capacity);
        Assert.Equal(56.0, updated.Geofence.Latitude, 3);
        Assert.Equal(13.0, updated.Geofence.Longitude, 3);
        Assert.Equal(300, updated.Geofence.RadiusMeters, 3);

        var loaded = store.Get(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated Name", loaded!.Name);
        Assert.Equal(175, loaded.Capacity);
    }

    [Fact]
    public void Update_UnknownEvent_ReturnsNull()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbEventStore(_localStack.DynamoDb, _localStack.TableName);

        var updated = store.Update(Guid.NewGuid(), new UpdateEventRequest
        {
            Name = "Does Not Matter"
        });

        Assert.Null(updated);
    }
}

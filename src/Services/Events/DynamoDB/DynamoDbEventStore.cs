using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace zip02.Services.Events.DynamoDB;

/// <summary>
/// DynamoDB-backed implementation of <see cref="IEventStore"/>.
/// Single-table item shape:
///   PK = EVENT#&lt;eventId:N&gt;
///   SK = EVENT
/// </summary>
public sealed class DynamoDbEventStore(IAmazonDynamoDB dynamoDb, string tableName) : IEventStore
{
    private const string Pk = "PK";
    private const string Sk = "SK";
    private const string SkValue = "EVENT";

    private static string EventPk(Guid id) => $"EVENT#{id:N}";

    public EventResponse Create(CreateEventRequest request)
    {
        var evt = new EventResponse
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            StartAtUtc = request.StartAtUtc!.Value,
            EndAtUtc = request.EndAtUtc!.Value,
            Capacity = request.Capacity!.Value,
            Geofence = new GeofenceResponse
            {
                Latitude = request.Geofence!.Latitude!.Value,
                Longitude = request.Geofence.Longitude!.Value,
                RadiusMeters = request.Geofence.RadiusMeters!.Value
            }
        };

        dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(evt),
            ConditionExpression = "attribute_not_exists(PK)"
        }).GetAwaiter().GetResult();

        return evt;
    }

    public EventResponse? Get(Guid id)
    {
        var result = dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = Key(id)
        }).GetAwaiter().GetResult();

        return result.IsItemSet ? FromItem(result.Item) : null;
    }

    public EventResponse? Update(Guid id, UpdateEventRequest request)
    {
        while (true)
        {
            var current = Get(id);
            if (current is null)
            {
                return null;
            }

            var updated = new EventResponse
            {
                Id = current.Id,
                Name = string.IsNullOrWhiteSpace(request.Name) ? current.Name : request.Name.Trim(),
                StartAtUtc = request.StartAtUtc ?? current.StartAtUtc,
                EndAtUtc = request.EndAtUtc ?? current.EndAtUtc,
                Capacity = request.Capacity ?? current.Capacity,
                Geofence = request.Geofence is null
                    ? current.Geofence
                    : new GeofenceResponse
                    {
                        Latitude = request.Geofence.Latitude!.Value,
                        Longitude = request.Geofence.Longitude!.Value,
                        RadiusMeters = request.Geofence.RadiusMeters!.Value
                    }
            };

            try
            {
                dynamoDb.PutItemAsync(new PutItemRequest
                {
                    TableName = tableName,
                    Item = ToItem(updated),
                    ConditionExpression = "attribute_exists(PK) AND #name = :name AND #start = :start AND #end = :end AND #capacity = :capacity",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#name"] = "Name",
                        ["#start"] = "StartAtUtc",
                        ["#end"] = "EndAtUtc",
                        ["#capacity"] = "Capacity"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":name"] = new AttributeValue(current.Name),
                        [":start"] = new AttributeValue(current.StartAtUtc.ToString("O")),
                        [":end"] = new AttributeValue(current.EndAtUtc.ToString("O")),
                        [":capacity"] = new AttributeValue { N = current.Capacity.ToString() }
                    }
                }).GetAwaiter().GetResult();

                return updated;
            }
            catch (ConditionalCheckFailedException)
            {
                // Concurrent update happened — retry with fresh read.
            }
        }
    }

    public IReadOnlyCollection<EventResponse> GetEndedEvents(DateTimeOffset processedAtUtc)
    {
        var response = dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = tableName,
            FilterExpression = "SK = :eventSk AND EndAtUtc <= :processedAtUtc",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":eventSk"] = new AttributeValue(SkValue),
                [":processedAtUtc"] = new AttributeValue(processedAtUtc.ToString("O"))
            }
        }).GetAwaiter().GetResult();

        return response.Items
            .Select(FromItem)
            .ToArray();
    }

    private static Dictionary<string, AttributeValue> Key(Guid id) => new()
    {
        [Pk] = new AttributeValue(EventPk(id)),
        [Sk] = new AttributeValue(SkValue)
    };

    private static Dictionary<string, AttributeValue> ToItem(EventResponse evt)
    {
        return new Dictionary<string, AttributeValue>
        {
            [Pk] = new AttributeValue(EventPk(evt.Id)),
            [Sk] = new AttributeValue(SkValue),
            ["Id"] = new AttributeValue(evt.Id.ToString("N")),
            ["Name"] = new AttributeValue(evt.Name),
            ["StartAtUtc"] = new AttributeValue(evt.StartAtUtc.ToString("O")),
            ["EndAtUtc"] = new AttributeValue(evt.EndAtUtc.ToString("O")),
            ["Capacity"] = new AttributeValue { N = evt.Capacity.ToString() },
            ["GeoLatitude"] = new AttributeValue { N = evt.Geofence.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["GeoLongitude"] = new AttributeValue { N = evt.Geofence.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["GeoRadiusMeters"] = new AttributeValue { N = evt.Geofence.RadiusMeters.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        };
    }

    private static EventResponse FromItem(Dictionary<string, AttributeValue> item)
    {
        return new EventResponse
        {
            Id = Guid.ParseExact(item["Id"].S, "N"),
            Name = item["Name"].S,
            StartAtUtc = DateTimeOffset.Parse(item["StartAtUtc"].S),
            EndAtUtc = DateTimeOffset.Parse(item["EndAtUtc"].S),
            Capacity = int.Parse(item["Capacity"].N),
            Geofence = new GeofenceResponse
            {
                Latitude = double.Parse(item["GeoLatitude"].N, System.Globalization.CultureInfo.InvariantCulture),
                Longitude = double.Parse(item["GeoLongitude"].N, System.Globalization.CultureInfo.InvariantCulture),
                RadiusMeters = double.Parse(item["GeoRadiusMeters"].N, System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }
}

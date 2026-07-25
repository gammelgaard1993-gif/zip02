using System.Collections.Concurrent;

namespace zip02.Services.Events;

public interface IEventStore
{
    EventResponse Create(CreateEventRequest request);

    EventResponse? Get(Guid id);

    EventResponse? Update(Guid id, UpdateEventRequest request);

    IReadOnlyCollection<EventResponse> GetEndedEvents(DateTimeOffset processedAtUtc);
}

public sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, EventResponse> _events = new();

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

        _events[evt.Id] = evt;
        return evt;
    }

    public EventResponse? Get(Guid id)
    {
        return _events.TryGetValue(id, out var evt) ? evt : null;
    }

    public EventResponse? Update(Guid id, UpdateEventRequest request)
    {
        while (true)
        {
            if (!_events.TryGetValue(id, out var current))
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

            if (_events.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }
    }

    public IReadOnlyCollection<EventResponse> GetEndedEvents(DateTimeOffset processedAtUtc)
    {
        return _events.Values
            .Where(e => e.EndAtUtc <= processedAtUtc)
            .ToArray();
    }
}

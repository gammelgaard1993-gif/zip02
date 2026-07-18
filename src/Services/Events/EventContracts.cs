using System.ComponentModel.DataAnnotations;

namespace zip02.Services.Events;

public sealed class GeofenceRequest
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Range(1, 100000)]
    public double RadiusMeters { get; set; }
}

public sealed class CreateEventRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset StartAtUtc { get; set; }

    public DateTimeOffset EndAtUtc { get; set; }

    [Range(1, int.MaxValue)]
    public int Capacity { get; set; }

    [Required]
    public GeofenceRequest Geofence { get; set; } = new();
}

public sealed class UpdateEventRequest
{
    [MaxLength(200)]
    public string? Name { get; set; }

    public DateTimeOffset? StartAtUtc { get; set; }

    public DateTimeOffset? EndAtUtc { get; set; }

    [Range(1, int.MaxValue)]
    public int? Capacity { get; set; }

    public GeofenceRequest? Geofence { get; set; }
}

public sealed class GeofenceResponse
{
    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public double RadiusMeters { get; init; }
}

public sealed class EventResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset StartAtUtc { get; init; }

    public DateTimeOffset EndAtUtc { get; init; }

    public int Capacity { get; init; }

    public GeofenceResponse Geofence { get; init; } = new();
}

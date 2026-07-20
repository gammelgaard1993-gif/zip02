using System.ComponentModel.DataAnnotations;

namespace zip02.Services.CheckIn.Application;

public enum CheckInFailureReason
{
    None,
    TicketNotFound,
    EventNotFound,
    TicketNotPaid,
    AlreadyCheckedIn,
    InvalidQrToken,
    OutsideGeofence,
    OutsideCheckInWindow
}

public sealed class CheckInRequest
{
    [Required]
    public Guid TicketId { get; set; }

    [Required]
    [MaxLength(200)]
    public string? QrToken { get; set; }

    [Required]
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Required]
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class CheckInResponse
{
    public Guid TicketId { get; init; }

    public Guid EventId { get; init; }

    public bool Success { get; init; }

    public CheckInFailureReason Reason { get; init; }

    public double? DistanceMeters { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public DateTimeOffset? CheckedInAtUtc { get; init; }
}

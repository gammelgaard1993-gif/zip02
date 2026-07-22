using System.ComponentModel.DataAnnotations;

namespace zip02.Services.Ticketing.Contracts;

public enum TicketStatus
{
    Reserved,
    Paid,
    CheckedIn,
    Expired,
    Refunded,
    Cancelled
}

public sealed class ReserveTicketRequest
{
    [Required]
    public Guid EventId { get; set; }

    [Required]
    [MaxLength(100)]
    public string? AttendeeId { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string? AttendeeEmail { get; set; }

    [Required]
    [MaxLength(100)]
    public string? IdempotencyKey { get; set; }
}

public sealed class UpdateTicketRequest
{
    [EmailAddress]
    [MaxLength(320)]
    public string? AttendeeEmail { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public sealed class ExpireReservationsRequest
{
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

public sealed class ExpireReservationsResponse
{
    public DateTimeOffset ProcessedAtUtc { get; init; }

    public int ExpiredCount { get; init; }

    public IReadOnlyCollection<Guid> TicketIds { get; init; } = Array.Empty<Guid>();
}

public sealed class TicketResponse
{
    public Guid Id { get; init; }

    public Guid EventId { get; init; }

    public string AttendeeId { get; init; } = string.Empty;

    public string AttendeeEmail { get; init; } = string.Empty;

    public TicketStatus Status { get; init; }

    public DateTimeOffset ReservedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? PaidAtUtc { get; init; }

    public DateTimeOffset? CheckedInAtUtc { get; init; }

    public DateTimeOffset? ExpiredAtUtc { get; init; }

    public DateTimeOffset? RefundedAtUtc { get; init; }

    public DateTimeOffset? CancelledAtUtc { get; init; }

    public string? QrToken { get; init; }

    public string? QrPayload { get; init; }

    public DateTimeOffset? QrIssuedAtUtc { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Shared copy helper used by all ITicketStore implementations to produce
/// a new TicketResponse with updated fields, preserving all other values from source.
/// </summary>
public static class TicketCopy
{
    public static TicketResponse Copy(
        TicketResponse source,
        TicketStatus status,
        DateTimeOffset? paidAtUtc = null,
        DateTimeOffset? checkedInAtUtc = null,
        DateTimeOffset? expiredAtUtc = null,
        DateTimeOffset? refundedAtUtc = null,
        DateTimeOffset? cancelledAtUtc = null,
        string? qrToken = null,
        string? qrPayload = null,
        DateTimeOffset? qrIssuedAtUtc = null,
        string? attendeeEmail = null,
        string? notes = null)
    {
        return new TicketResponse
        {
            Id = source.Id,
            EventId = source.EventId,
            AttendeeId = source.AttendeeId,
            AttendeeEmail = attendeeEmail ?? source.AttendeeEmail,
            Status = status,
            ReservedAtUtc = source.ReservedAtUtc,
            ExpiresAtUtc = source.ExpiresAtUtc,
            PaidAtUtc = paidAtUtc ?? source.PaidAtUtc,
            CheckedInAtUtc = checkedInAtUtc ?? source.CheckedInAtUtc,
            ExpiredAtUtc = expiredAtUtc ?? source.ExpiredAtUtc,
            RefundedAtUtc = refundedAtUtc ?? source.RefundedAtUtc,
            CancelledAtUtc = cancelledAtUtc ?? source.CancelledAtUtc,
            QrToken = qrToken ?? source.QrToken,
            QrPayload = qrPayload ?? source.QrPayload,
            QrIssuedAtUtc = qrIssuedAtUtc ?? source.QrIssuedAtUtc,
            Notes = notes ?? source.Notes
        };
    }
}

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

    public string? Notes { get; init; }
}

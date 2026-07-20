using System.ComponentModel.DataAnnotations;

namespace zip02.Services.Refunds.Application;

public enum RefundDecisionReason
{
    None,
    EventNotFound,
    EventNotEnded,
    TicketNotFound,
    TicketNotPaid,
    TicketCheckedIn,
    AlreadyRefunded,
    Refunded
}

public sealed class ManualRefundRequest
{
    [Required]
    [MaxLength(200)]
    public string? CorrelationId { get; set; }

    [Required]
    public DateTimeOffset RequestedAtUtc { get; set; }
}

public sealed class RefundTicketResult
{
    public Guid TicketId { get; init; }

    public Guid EventId { get; init; }

    public bool Refunded { get; init; }

    public RefundDecisionReason Reason { get; init; }

    public DateTimeOffset ProcessedAtUtc { get; init; }
}

public sealed class NoShowReconciliationResult
{
    public Guid EventId { get; init; }

    public DateTimeOffset ProcessedAtUtc { get; init; }

    public int RefundedCount { get; init; }

    public int EvaluatedCount { get; init; }

    public IReadOnlyCollection<RefundTicketResult> Tickets { get; init; } = Array.Empty<RefundTicketResult>();
}

using System.ComponentModel.DataAnnotations;

namespace zip02.Services.Payments;

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded
}

public sealed class PaymentWebhookRequest
{
    [Required]
    [MaxLength(50)]
    public string? Provider { get; set; }

    [Required]
    [MaxLength(200)]
    public string? ProviderEventId { get; set; }

    [Required]
    public Guid TicketId { get; set; }

    [Required]
    public Guid EventId { get; set; }

    [Required]
    [Range(1, long.MaxValue)]
    public long? AmountMinorUnits { get; set; }

    [Required]
    [MaxLength(3)]
    public string? Currency { get; set; }

    [Required]
    public PaymentStatus? Status { get; set; }

    [Required]
    public DateTimeOffset? OccurredAtUtc { get; set; }

    [Required]
    [MaxLength(200)]
    public string? CorrelationId { get; set; }
}

public sealed class PaymentRecord
{
    public Guid Id { get; init; }

    public Guid TicketId { get; init; }

    public Guid EventId { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string ProviderEventId { get; init; } = string.Empty;

    public long AmountMinorUnits { get; init; }

    public string Currency { get; init; } = string.Empty;

    public PaymentStatus Status { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public DateTimeOffset ProcessedAtUtc { get; init; }

    public string CorrelationId { get; init; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace zip02.Services.Notifications.Contracts;

public enum NotificationStatus
{
    Pending,
    Succeeded,
    Failed
}

public sealed class QrArtifact
{
    [Required]
    public Guid TicketId { get; set; }

    [Required]
    [MaxLength(200)]
    public string? Token { get; set; }

    [Required]
    [MaxLength(2000)]
    public string? RenderedPayload { get; set; }

    [Required]
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class EmailNotificationRequest
{
    [Required]
    public Guid TicketId { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string? ToEmail { get; set; }

    [Required]
    [MaxLength(200)]
    public string? Subject { get; set; }

    [Required]
    [MaxLength(4000)]
    public string? Body { get; set; }

    [Required]
    [MaxLength(200)]
    public string? CorrelationId { get; set; }
}

public sealed class EmailNotificationRecord
{
    public Guid Id { get; init; }

    public Guid TicketId { get; init; }

    public string ToEmail { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public NotificationStatus Status { get; init; }

    public DateTimeOffset SentAtUtc { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string? FailureReason { get; init; }
}

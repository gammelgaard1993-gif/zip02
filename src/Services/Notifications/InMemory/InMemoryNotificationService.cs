using System.Collections.Concurrent;
using System.Text;
using zip02.Services.Notifications.Contracts;
using zip02.Services.Ticketing.Contracts;

namespace zip02.Services.Notifications.InMemory;

public interface INotificationStore
{
    QrArtifact IssueQr(TicketResponse ticket, DateTimeOffset createdAtUtc);

    EmailNotificationRecord SendEmail(EmailNotificationRequest request, DateTimeOffset sentAtUtc);

    QrArtifact? GetQr(Guid ticketId);

    EmailNotificationRecord? GetEmailForTicket(Guid ticketId);
}

public sealed class InMemoryNotificationStore : INotificationStore
{
    private readonly ConcurrentDictionary<Guid, QrArtifact> _qrByTicketId = new();
    private readonly ConcurrentDictionary<Guid, EmailNotificationRecord> _emailByTicketId = new();

    public QrArtifact IssueQr(TicketResponse ticket, DateTimeOffset createdAtUtc)
    {
        return _qrByTicketId.GetOrAdd(ticket.Id, _ =>
        {
            var token = GenerateToken(ticket.Id);
            return new QrArtifact
            {
                TicketId = ticket.Id,
                Token = token,
                RenderedPayload = RenderPayload(ticket.Id, token),
                CreatedAtUtc = createdAtUtc
            };
        });
    }

    public EmailNotificationRecord SendEmail(EmailNotificationRequest request, DateTimeOffset sentAtUtc)
    {
        return _emailByTicketId.GetOrAdd(request.TicketId, _ => new EmailNotificationRecord
        {
            Id = Guid.NewGuid(),
            TicketId = request.TicketId,
            ToEmail = request.ToEmail!.Trim(),
            Subject = request.Subject!.Trim(),
            Body = request.Body!.Trim(),
            Status = NotificationStatus.Succeeded,
            SentAtUtc = sentAtUtc,
            CorrelationId = request.CorrelationId!.Trim()
        });
    }

    public QrArtifact? GetQr(Guid ticketId)
    {
        return _qrByTicketId.TryGetValue(ticketId, out var qr) ? qr : null;
    }

    public EmailNotificationRecord? GetEmailForTicket(Guid ticketId)
    {
        return _emailByTicketId.TryGetValue(ticketId, out var email) ? email : null;
    }

    private static string GenerateToken(Guid ticketId)
    {
        var bytes = Encoding.UTF8.GetBytes(ticketId.ToString("N") + ":" + Guid.NewGuid().ToString("N"));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string RenderPayload(Guid ticketId, string token)
    {
        return $"zip02://qr/tickets/{ticketId}?token={token}";
    }
}

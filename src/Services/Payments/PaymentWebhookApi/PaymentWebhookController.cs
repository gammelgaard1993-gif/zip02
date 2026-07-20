using Microsoft.AspNetCore.Mvc;
using zip02.Services.Notifications.Contracts;
using zip02.Services.Notifications.InMemory;
using zip02.Services.Payments.Contracts;
using zip02.Services.Payments.InMemory;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Services.Payments.PaymentWebhookApi;

[ApiController]
[Route("payments/stripe")]
public class PaymentWebhookController(IPaymentStore paymentStore, ITicketStore ticketStore, INotificationStore notificationStore) : ControllerBase
{
    [HttpPost("webhook")]
    public ActionResult<PaymentRecord> Webhook([FromBody] PaymentWebhookRequest request)
    {
        var existing = paymentStore.GetByProviderEventId(request.ProviderEventId!);
        if (existing is not null)
        {
            if (existing.Status == PaymentStatus.Succeeded)
            {
                CompleteQrDelivery(existing.TicketId, existing.CorrelationId, existing.OccurredAtUtc);
            }

            return Ok(existing);
        }

        var record = paymentStore.Record(request, DateTimeOffset.UtcNow);
        if (record.Status == PaymentStatus.Succeeded)
        {
            if (!CompleteQrDelivery(record.TicketId, record.CorrelationId, record.OccurredAtUtc))
            {
                return Conflict(record);
            }
        }

        return Ok(record);
    }

    private bool CompleteQrDelivery(Guid ticketId, string correlationId, DateTimeOffset occurredAtUtc)
    {
        var ticket = ticketStore.MarkPaid(ticketId, occurredAtUtc);
        if (ticket is null)
        {
            return false;
        }

        var qr = notificationStore.IssueQr(ticket, occurredAtUtc);
        ticketStore.MarkQrIssued(ticket.Id, qr.Token!, qr.RenderedPayload!, qr.CreatedAtUtc);

        notificationStore.SendEmail(new EmailNotificationRequest
        {
            TicketId = ticket.Id,
            ToEmail = ticket.AttendeeEmail,
            Subject = "Your QR ticket",
            Body = qr.RenderedPayload!,
            CorrelationId = correlationId
        }, occurredAtUtc);

        return true;
    }
}

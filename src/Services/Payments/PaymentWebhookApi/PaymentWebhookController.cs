using Microsoft.AspNetCore.Mvc;
using zip02.Services.Payments.Contracts;
using zip02.Services.Payments.InMemory;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Services.Payments.PaymentWebhookApi;

[ApiController]
[Route("payments/stripe")]
public class PaymentWebhookController(IPaymentStore paymentStore, ITicketStore ticketStore) : ControllerBase
{
    [HttpPost("webhook")]
    public ActionResult<PaymentRecord> Webhook([FromBody] PaymentWebhookRequest request)
    {
        var existing = paymentStore.GetByProviderEventId(request.ProviderEventId!);
        if (existing is not null)
        {
            if (existing.Status == PaymentStatus.Succeeded)
            {
                ticketStore.MarkPaid(existing.TicketId, existing.OccurredAtUtc);
            }

            return Ok(existing);
        }

        var record = paymentStore.Record(request, DateTimeOffset.UtcNow);
        if (record.Status == PaymentStatus.Succeeded)
        {
            var ticket = ticketStore.MarkPaid(record.TicketId, record.OccurredAtUtc);
            if (ticket is null)
            {
                return Conflict(record);
            }
        }

        return Ok(record);
    }
}

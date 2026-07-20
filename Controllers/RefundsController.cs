using Microsoft.AspNetCore.Mvc;
using zip02.Services.Refunds.Application;
using zip02.Services.Refunds.Infrastructure;

namespace zip02.Controllers;

[ApiController]
[Route("refunds")]
public class RefundsController(IRefundProcessor refundProcessor) : ControllerBase
{
    [HttpPost("events/{eventId:guid}/reconcile")]
    public ActionResult<NoShowReconciliationResult> ReconcileNoShows(Guid eventId)
    {
        var result = refundProcessor.ReconcileNoShows(eventId, DateTimeOffset.UtcNow);
        return Ok(result);
    }

    [HttpPost("tickets/{ticketId:guid}")]
    public ActionResult<RefundTicketResult> RefundBeforeActivation(Guid ticketId, [FromBody] ManualRefundRequest request)
    {
        var result = refundProcessor.RefundBeforeActivation(ticketId, request.RequestedAtUtc, request.CorrelationId!);
        return Ok(result);
    }
}

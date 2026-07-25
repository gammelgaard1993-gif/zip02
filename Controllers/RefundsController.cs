using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using zip02.Services.Refunds.Application;
using zip02.Services.Refunds.Infrastructure;

namespace zip02.Controllers;

[ApiController]
[Route("refunds")]
public class RefundsController(IRefundProcessor refundProcessor) : ControllerBase
{
    // SECURITY: Reconciliation can trigger financial actions (refund decisions).
    // Only organizer roles may invoke this endpoint manually.
    [Authorize(Policy = "OrganizerWrite")]
    [HttpPost("events/{eventId:guid}/reconcile")]
    public ActionResult<NoShowReconciliationResult> ReconcileNoShows(Guid eventId)
    {
        var result = refundProcessor.ReconcileNoShows(eventId, DateTimeOffset.UtcNow);
        return Ok(result);
    }

    // SECURITY: Manual refund is a privileged, money-impacting operation.
    // Require authenticated organizer role membership for explicit accountability.
    [Authorize(Policy = "OrganizerWrite")]
    [HttpPost("tickets/{ticketId:guid}")]
    public ActionResult<RefundTicketResult> RefundBeforeActivation(Guid ticketId, [FromBody] ManualRefundRequest request)
    {
        var result = refundProcessor.RefundBeforeActivation(ticketId, request.RequestedAtUtc, request.CorrelationId!);
        return Ok(result);
    }
}

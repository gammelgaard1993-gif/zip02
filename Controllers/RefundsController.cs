using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using zip02.Services.Events;
using zip02.Services.Refunds.Application;
using zip02.Services.Refunds.Infrastructure;

namespace zip02.Controllers;

[ApiController]
[Route("refunds")]
public class RefundsController(IRefundProcessor refundProcessor, IEventStore eventStore, ILogger<RefundsController> logger) : ControllerBase
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

    // SECURITY: Batch reconciliation is designed for scheduler/admin workflows.
    // It processes ALL ended events and can trigger many refund decisions, so it
    // is protected by OrganizerWrite (human organizer JWT OR trusted scheduler
    // machine token path validated in Program.cs).
    [Authorize(Policy = "OrganizerWrite")]
    [HttpPost("reconcile-ended-events")]
    public ActionResult<BatchNoShowReconciliationResult> ReconcileEndedEvents()
    {
        var processedAtUtc = DateTimeOffset.UtcNow;
        var endedEvents = eventStore.GetEndedEvents(processedAtUtc);

        // OPERATIONAL LOGGING: One start log and one completion log make this
        // scheduled financial workflow easy to trace in CloudWatch.
        logger.LogInformation(
            "Batch no-show reconciliation started at {ProcessedAtUtc}; endedEventCount={EndedEventCount}; invocationSource={InvocationSource}",
            processedAtUtc,
            endedEvents.Count,
            Request.Headers["x-invocation-source"].ToString());

        var eventResults = endedEvents
            .Select(e => refundProcessor.ReconcileNoShows(e.Id, processedAtUtc))
            .ToArray();

        var result = new BatchNoShowReconciliationResult
        {
            ProcessedAtUtc = processedAtUtc,
            EventCount = eventResults.Length,
            EvaluatedTicketCount = eventResults.Sum(r => r.EvaluatedCount),
            RefundedTicketCount = eventResults.Sum(r => r.RefundedCount),
            Events = eventResults
        };

        logger.LogInformation(
            "Batch no-show reconciliation completed at {ProcessedAtUtc}; eventCount={EventCount}; evaluatedTicketCount={EvaluatedTicketCount}; refundedTicketCount={RefundedTicketCount}",
            processedAtUtc,
            result.EventCount,
            result.EvaluatedTicketCount,
            result.RefundedTicketCount);

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

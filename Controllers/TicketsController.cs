using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using zip02.Services.Events;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Controllers;

[ApiController]
[Route("tickets")]
public class TicketsController(ITicketStore ticketStore, IEventStore eventStore, ILogger<TicketsController> logger) : ControllerBase
{
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    [HttpPost("reserve")]
    public ActionResult<TicketResponse> Reserve([FromBody] ReserveTicketRequest request)
    {
        var evt = eventStore.Get(request.EventId);
        if (evt is null)
        {
            return NotFound();
        }

        var activeCount = ticketStore.GetByEvent(request.EventId)
            .Count(t => t.Status is TicketStatus.Reserved or TicketStatus.Paid or TicketStatus.CheckedIn);

        if (activeCount >= evt.Capacity)
        {
            return Conflict();
        }

        var reserved = ticketStore.Reserve(request, DateTimeOffset.UtcNow, ReservationTtl);
        return CreatedAtAction(nameof(GetById), new { id = reserved.Id }, reserved);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<TicketResponse> GetById(Guid id)
    {
        var ticket = ticketStore.Get(id);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPatch("{id:guid}")]
    public ActionResult<TicketResponse> Update(Guid id, [FromBody] UpdateTicketRequest request)
    {
        var updated = ticketStore.Update(id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    // SECURITY: Reservation expiry is an operational/admin action that mutates
    // ticket state in bulk. Restrict to organizer principals (or scheduled
    // machine invocation when IAM/authorizer wiring is enabled in API Gateway).
    [Authorize(Policy = "OrganizerWrite")]
    [HttpPost("expire-reservations")]
    public ActionResult<ExpireReservationsResponse> ExpireReservations([FromBody] ExpireReservationsRequest? request)
    {
        var processedAtUtc = request?.ProcessedAtUtc ?? DateTimeOffset.UtcNow;

        // OPERATIONAL LOGGING: Explicitly record scheduler/admin expiry runs so
        // CloudWatch queries can track job cadence, throughput, and anomalies.
        logger.LogInformation(
            "Ticket expiry run started at {ProcessedAtUtc} (invocationSource={InvocationSource})",
            processedAtUtc,
            Request.Headers["x-invocation-source"].ToString());

        var expired = ticketStore.ExpireReservations(processedAtUtc);

        logger.LogInformation(
            "Ticket expiry run completed at {ProcessedAtUtc}; expiredCount={ExpiredCount}",
            processedAtUtc,
            expired.Count);

        return Ok(new ExpireReservationsResponse
        {
            ProcessedAtUtc = processedAtUtc,
            ExpiredCount = expired.Count,
            TicketIds = expired.Select(t => t.Id).ToArray()
        });
    }
}

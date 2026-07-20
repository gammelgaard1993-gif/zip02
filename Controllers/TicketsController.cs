using Microsoft.AspNetCore.Mvc;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Controllers;

[ApiController]
[Route("tickets")]
public class TicketsController(ITicketStore ticketStore) : ControllerBase
{
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    [HttpPost("reserve")]
    public ActionResult<TicketResponse> Reserve([FromBody] ReserveTicketRequest request)
    {
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
}

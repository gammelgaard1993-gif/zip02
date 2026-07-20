using Microsoft.AspNetCore.Mvc;
using zip02.Services.Events;

namespace zip02.Controllers;

[ApiController]
[Route("events")]
public class EventsController(IEventStore eventStore) : ControllerBase
{
    [HttpPost]
    public ActionResult<EventResponse> Create([FromBody] CreateEventRequest request)
    {
        if (!IsTimeRangeValid(request.StartAtUtc!.Value, request.EndAtUtc!.Value))
        {
            ModelState.AddModelError(nameof(request.EndAtUtc), "EndAtUtc must be greater than StartAtUtc.");
            return ValidationProblem(ModelState);
        }

        var created = eventStore.Create(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<EventResponse> GetById(Guid id)
    {
        var evt = eventStore.Get(id);
        return evt is null ? NotFound() : Ok(evt);
    }

    [HttpPatch("{id:guid}")]
    public ActionResult<EventResponse> Update(Guid id, [FromBody] UpdateEventRequest request)
    {
        var existing = eventStore.Get(id);
        if (existing is null)
        {
            return NotFound();
        }

        var start = request.StartAtUtc ?? existing.StartAtUtc;
        var end = request.EndAtUtc ?? existing.EndAtUtc;

        if (!IsTimeRangeValid(start, end))
        {
            ModelState.AddModelError(nameof(request.EndAtUtc), "EndAtUtc must be greater than StartAtUtc.");
            return ValidationProblem(ModelState);
        }

        var updated = eventStore.Update(id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    private static bool IsTimeRangeValid(DateTimeOffset start, DateTimeOffset end)
    {
        return end > start;
    }
}

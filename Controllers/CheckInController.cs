using Microsoft.AspNetCore.Mvc;
using zip02.Services.CheckIn.Application;
using zip02.Services.CheckIn.Infrastructure;
using zip02.Services.Events;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Controllers;

[ApiController]
[Route("checkin")]
public class CheckInController(IEventStore eventStore, ITicketStore ticketStore) : ControllerBase
{
    [HttpPost]
    public ActionResult<CheckInResponse> CheckIn([FromBody] CheckInRequest request)
    {
        var ticket = ticketStore.Get(request.TicketId);
        if (ticket is null)
        {
            return Ok(new CheckInResponse
            {
                TicketId = request.TicketId,
                Success = false,
                Reason = CheckInFailureReason.TicketNotFound,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        var evt = eventStore.Get(ticket.EventId);
        if (evt is null)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = ticket.EventId,
                Success = false,
                Reason = CheckInFailureReason.EventNotFound,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        if (ticket.Status == TicketStatus.CheckedIn)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.AlreadyCheckedIn,
                OccurredAtUtc = request.OccurredAtUtc,
                CheckedInAtUtc = ticket.CheckedInAtUtc
            });
        }

        if (ticket.Status != TicketStatus.Paid)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.TicketNotPaid,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        if (!string.Equals(ticket.QrToken, request.QrToken, StringComparison.Ordinal))
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.InvalidQrToken,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        if (request.OccurredAtUtc < evt.StartAtUtc || request.OccurredAtUtc > evt.EndAtUtc)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.OutsideCheckInWindow,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        var distance = GeofenceMath.DistanceMeters(
            request.Latitude,
            request.Longitude,
            evt.Geofence.Latitude,
            evt.Geofence.Longitude);

        if (distance > evt.Geofence.RadiusMeters)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.OutsideGeofence,
                DistanceMeters = distance,
                OccurredAtUtc = request.OccurredAtUtc
            });
        }

        var checkedIn = ticketStore.MarkCheckedIn(ticket.Id, request.OccurredAtUtc);
        if (checkedIn is null)
        {
            return Ok(new CheckInResponse
            {
                TicketId = ticket.Id,
                EventId = evt.Id,
                Success = false,
                Reason = CheckInFailureReason.AlreadyCheckedIn,
                OccurredAtUtc = request.OccurredAtUtc,
                DistanceMeters = distance
            });
        }

        return Ok(new CheckInResponse
        {
            TicketId = checkedIn.Id,
            EventId = evt.Id,
            Success = true,
            Reason = CheckInFailureReason.None,
            DistanceMeters = distance,
            OccurredAtUtc = request.OccurredAtUtc,
            CheckedInAtUtc = checkedIn.CheckedInAtUtc
        });
    }
}

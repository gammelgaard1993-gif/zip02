using Microsoft.AspNetCore.Mvc;
using zip02.Controllers;
using zip02.Services.CheckIn.Application;
using zip02.Services.Events;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace Zip02.Tests.CheckIn.Unit;

public sealed class CheckInControllerTests
{
    // Fixed event window: 10:00 – 22:00 UTC on 2024-06-01
    private static readonly DateTimeOffset EventStart = new(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventEnd   = new(2024, 6, 1, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DuringEvent = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // Geofence: Copenhagen City Hall, 500 m radius
    private const double CenterLat    = 55.6761;
    private const double CenterLon    = 12.5683;
    private const double RadiusMeters = 500;

    // Inside: ~200 m north of center
    private const double InsideLat = 55.6779;
    private const double InsideLon = CenterLon;

    // Outside: ~2 km north of center
    private const double OutsideLat = 55.6940;
    private const double OutsideLon = CenterLon;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (InMemoryEventStore Events, InMemoryTicketStore Tickets, EventResponse Evt) SetUpEvent()
    {
        var events  = new InMemoryEventStore();
        var tickets = new InMemoryTicketStore();

        var evt = events.Create(new CreateEventRequest
        {
            Name       = "Test Event",
            StartAtUtc = EventStart,
            EndAtUtc   = EventEnd,
            Capacity   = 100,
            Geofence   = new GeofenceRequest
            {
                Latitude      = CenterLat,
                Longitude     = CenterLon,
                RadiusMeters  = RadiusMeters
            }
        });

        return (events, tickets, evt);
    }

    /// <summary>Puts a ticket through Reserve → Paid → QrIssued so it is ready to check in.</summary>
    private static TicketResponse SetUpPaidTicket(InMemoryTicketStore tickets, Guid eventId, string qrToken = "valid-qr-token")
    {
        var setup = EventStart.AddMinutes(-10);

        var reserved = tickets.Reserve(new ReserveTicketRequest
        {
            EventId        = eventId,
            AttendeeId     = "attendee-1",
            AttendeeEmail  = "attendee@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        }, setup, TimeSpan.FromMinutes(30));

        tickets.MarkPaid(reserved.Id, setup.AddMinutes(5));
        tickets.MarkQrIssued(reserved.Id, qrToken, "QR-PAYLOAD", setup.AddMinutes(5));

        return tickets.Get(reserved.Id)!;
    }

    private static CheckInController CreateController(IEventStore events, ITicketStore tickets)
        => new(events, tickets);

    private static CheckInResponse Unwrap(ActionResult<CheckInResponse> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<CheckInResponse>(ok.Value);
    }

    // ── Not-found cases ───────────────────────────────────────────────────────

    [Fact]
    public void CheckIn_UnknownTicket_ReturnsTicketNotFound()
    {
        var (events, tickets, _) = SetUpEvent();
        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = Guid.NewGuid(),
            QrToken       = "tok",
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.TicketNotFound, response.Reason);
    }

    [Fact]
    public void CheckIn_TicketReferencesUnknownEvent_ReturnsEventNotFound()
    {
        var events  = new InMemoryEventStore();
        var tickets = new InMemoryTicketStore();

        // Ticket references an event that was never stored
        var setup = DuringEvent;
        var reserved = tickets.Reserve(new ReserveTicketRequest
        {
            EventId        = Guid.NewGuid(),
            AttendeeId     = "a",
            AttendeeEmail  = "a@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        }, setup, TimeSpan.FromMinutes(30));
        tickets.MarkPaid(reserved.Id, setup);
        tickets.MarkQrIssued(reserved.Id, "tok", "payload", setup);
        var ticket = tickets.Get(reserved.Id)!;

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = "tok",
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.EventNotFound, response.Reason);
    }

    // ── Status guardrails ─────────────────────────────────────────────────────

    [Fact]
    public void CheckIn_AlreadyCheckedInTicket_ReturnsAlreadyCheckedIn()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);
        tickets.MarkCheckedIn(ticket.Id, DuringEvent);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent.AddMinutes(5)
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.AlreadyCheckedIn, response.Reason);
    }

    [Fact]
    public void CheckIn_ReservedTicket_ReturnsTicketNotPaid()
    {
        var (events, tickets, evt) = SetUpEvent();

        var reserved = tickets.Reserve(new ReserveTicketRequest
        {
            EventId        = evt.Id,
            AttendeeId     = "a",
            AttendeeEmail  = "a@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        }, DuringEvent, TimeSpan.FromMinutes(30));

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = reserved.Id,
            QrToken       = "any-token",
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.TicketNotPaid, response.Reason);
    }

    [Fact]
    public void CheckIn_RefundedTicket_ReturnsTicketNotPaid()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);
        tickets.MarkRefunded(ticket.Id, DuringEvent);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.TicketNotPaid, response.Reason);
    }

    // ── QR token validation ───────────────────────────────────────────────────

    [Fact]
    public void CheckIn_WrongQrToken_ReturnsInvalidQrToken()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id, "correct-token");

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = "wrong-token",
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.InvalidQrToken, response.Reason);
    }

    [Fact]
    public void CheckIn_QrTokenIsCaseSensitive()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id, "CaseSensitiveToken");

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = "casesensitivetoken",    // lowercase
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.InvalidQrToken, response.Reason);
    }

    // ── Time window ───────────────────────────────────────────────────────────

    [Fact]
    public void CheckIn_BeforeEventStart_ReturnsOutsideCheckInWindow()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = EventStart.AddMinutes(-1)
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.OutsideCheckInWindow, response.Reason);
    }

    [Fact]
    public void CheckIn_AfterEventEnd_ReturnsOutsideCheckInWindow()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = EventEnd.AddMinutes(1)
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.OutsideCheckInWindow, response.Reason);
    }

    // ── Geofence ──────────────────────────────────────────────────────────────

    [Fact]
    public void CheckIn_OutsideGeofence_ReturnsOutsideGeofence()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = OutsideLat,
            Longitude     = OutsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.OutsideGeofence, response.Reason);
        Assert.NotNull(response.DistanceMeters);
        Assert.True(response.DistanceMeters > RadiusMeters);
    }

    [Fact]
    public void CheckIn_OutsideGeofence_ReportsActualDistance()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = OutsideLat,
            Longitude     = OutsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.NotNull(response.DistanceMeters);
        Assert.InRange(response.DistanceMeters!.Value, 1_900, 2_100);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void CheckIn_ValidRequest_ReturnsSuccess()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        var result = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var response = Unwrap(result);
        Assert.True(response.Success);
        Assert.Equal(CheckInFailureReason.None, response.Reason);
        Assert.Equal(ticket.Id, response.TicketId);
        Assert.Equal(evt.Id, response.EventId);
        Assert.Equal(DuringEvent, response.CheckedInAtUtc);
    }

    [Fact]
    public void CheckIn_ValidRequest_PersistsCheckedInStatus()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);

        var controller = CreateController(events, tickets);

        controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        Assert.Equal(TicketStatus.CheckedIn, tickets.Get(ticket.Id)!.Status);
    }

    [Fact]
    public void CheckIn_SecondAttemptAfterSuccess_ReturnsAlreadyCheckedIn()
    {
        var (events, tickets, evt) = SetUpEvent();
        var ticket = SetUpPaidTicket(tickets, evt.Id);
        var controller = CreateController(events, tickets);

        controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent
        });

        var second = controller.CheckIn(new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = DuringEvent.AddMinutes(1)
        });

        var response = Unwrap(second);
        Assert.False(response.Success);
        Assert.Equal(CheckInFailureReason.AlreadyCheckedIn, response.Reason);
    }
}

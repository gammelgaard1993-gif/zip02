using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace Zip02.Tests.Tickets.Unit;

public sealed class InMemoryTicketStoreTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryTicketStore CreateStore() => new();

    private static ReserveTicketRequest DefaultRequest(string? idempotencyKey = null) => new()
    {
        EventId = EventId,
        AttendeeId = "attendee-1",
        AttendeeEmail = "attendee@example.com",
        IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString("N")
    };

    // ── Reserve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reserve_NewRequest_ReturnsReservedTicket()
    {
        var store = CreateStore();

        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        Assert.NotEqual(Guid.Empty, ticket.Id);
        Assert.Equal(TicketStatus.Reserved, ticket.Status);
        Assert.Equal(EventId, ticket.EventId);
        Assert.Equal(Now.Add(TimeSpan.FromMinutes(15)), ticket.ExpiresAtUtc);
    }

    [Fact]
    public void Reserve_SameIdempotencyKey_ReturnsSameTicket()
    {
        var store = CreateStore();
        var key = Guid.NewGuid().ToString("N");

        var first = store.Reserve(DefaultRequest(key), Now, TimeSpan.FromMinutes(15));
        var second = store.Reserve(DefaultRequest(key), Now.AddSeconds(5), TimeSpan.FromMinutes(15));

        Assert.Equal(first.Id, second.Id);
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Get_KnownId_ReturnsTicket()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        var result = store.Get(ticket.Id);

        Assert.NotNull(result);
        Assert.Equal(ticket.Id, result!.Id);
    }

    // ── MarkPaid ──────────────────────────────────────────────────────────────

    [Fact]
    public void MarkPaid_ReservedTicket_TransitionsToPaid()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        var paid = store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        Assert.NotNull(paid);
        Assert.Equal(TicketStatus.Paid, paid!.Status);
        Assert.Equal(Now.AddMinutes(1), paid.PaidAtUtc);
    }

    [Fact]
    public void MarkPaid_AlreadyPaidTicket_ReturnsExistingIdempotently()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var second = store.MarkPaid(ticket.Id, Now.AddMinutes(2));

        Assert.NotNull(second);
        Assert.Equal(TicketStatus.Paid, second!.Status);
    }

    [Fact]
    public void MarkPaid_ExpiredTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(5));
        store.MarkExpired(ticket.Id, Now.AddMinutes(10));

        var result = store.MarkPaid(ticket.Id, Now.AddMinutes(11));

        Assert.Null(result);
    }

    [Fact]
    public void MarkPaid_UnknownId_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.MarkPaid(Guid.NewGuid(), Now));
    }

    // ── MarkCheckedIn ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkCheckedIn_PaidTicket_TransitionsToCheckedIn()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var checkedIn = store.MarkCheckedIn(ticket.Id, Now.AddMinutes(5));

        Assert.NotNull(checkedIn);
        Assert.Equal(TicketStatus.CheckedIn, checkedIn!.Status);
        Assert.Equal(Now.AddMinutes(5), checkedIn.CheckedInAtUtc);
    }

    [Fact]
    public void MarkCheckedIn_ReservedTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        Assert.Null(store.MarkCheckedIn(ticket.Id, Now.AddMinutes(1)));
    }

    [Fact]
    public void MarkCheckedIn_AlreadyCheckedInTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));
        store.MarkCheckedIn(ticket.Id, Now.AddMinutes(5));

        Assert.Null(store.MarkCheckedIn(ticket.Id, Now.AddMinutes(6)));
    }

    // ── MarkExpired ───────────────────────────────────────────────────────────

    [Fact]
    public void MarkExpired_ReservedTicket_TransitionsToExpired()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        var expired = store.MarkExpired(ticket.Id, Now.AddMinutes(20));

        Assert.NotNull(expired);
        Assert.Equal(TicketStatus.Expired, expired!.Status);
        Assert.Equal(Now.AddMinutes(20), expired.ExpiredAtUtc);
    }

    [Fact]
    public void MarkExpired_PaidTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        Assert.Null(store.MarkExpired(ticket.Id, Now.AddMinutes(20)));
    }

    // ── ExpireReservations ────────────────────────────────────────────────────

    [Fact]
    public void ExpireReservations_OnlyExpiresElapsedReservedTickets()
    {
        var store = CreateStore();
        var shortTtl = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(5));
        var longTtl = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(30));

        var expired = store.ExpireReservations(Now.AddMinutes(10));

        Assert.Single(expired);
        Assert.Equal(shortTtl.Id, expired.First().Id);
        Assert.Equal(TicketStatus.Expired, expired.First().Status);
        Assert.Equal(TicketStatus.Reserved, store.Get(longTtl.Id)!.Status);
    }

    [Fact]
    public void ExpireReservations_PaidTicket_IsNotExpired()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(5));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var expired = store.ExpireReservations(Now.AddMinutes(10));

        Assert.Empty(expired);
        Assert.Equal(TicketStatus.Paid, store.Get(ticket.Id)!.Status);
    }

    [Fact]
    public void ExpireReservations_NoElapsedTickets_ReturnsEmpty()
    {
        var store = CreateStore();
        store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(30));

        var expired = store.ExpireReservations(Now.AddMinutes(10));

        Assert.Empty(expired);
    }

    // ── MarkRefunded ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkRefunded_PaidTicket_TransitionsToRefunded()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var refunded = store.MarkRefunded(ticket.Id, Now.AddMinutes(10));

        Assert.NotNull(refunded);
        Assert.Equal(TicketStatus.Refunded, refunded!.Status);
        Assert.Equal(Now.AddMinutes(10), refunded.RefundedAtUtc);
    }

    [Fact]
    public void MarkRefunded_CheckedInTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));
        store.MarkCheckedIn(ticket.Id, Now.AddMinutes(5));

        Assert.Null(store.MarkRefunded(ticket.Id, Now.AddMinutes(10)));
    }

    [Fact]
    public void MarkRefunded_ReservedTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        Assert.Null(store.MarkRefunded(ticket.Id, Now.AddMinutes(1)));
    }

    // ── MarkCancelled ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkCancelled_ReservedTicket_TransitionsToCancelled()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        var cancelled = store.MarkCancelled(ticket.Id, Now.AddMinutes(1));

        Assert.NotNull(cancelled);
        Assert.Equal(TicketStatus.Cancelled, cancelled!.Status);
        Assert.Equal(Now.AddMinutes(1), cancelled.CancelledAtUtc);
    }

    [Fact]
    public void MarkCancelled_PaidTicket_TransitionsToCancelled()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var cancelled = store.MarkCancelled(ticket.Id, Now.AddMinutes(5));

        Assert.NotNull(cancelled);
        Assert.Equal(TicketStatus.Cancelled, cancelled!.Status);
    }

    [Fact]
    public void MarkCancelled_CheckedInTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));
        store.MarkCheckedIn(ticket.Id, Now.AddMinutes(5));

        Assert.Null(store.MarkCancelled(ticket.Id, Now.AddMinutes(10)));
    }

    // ── MarkQrIssued ──────────────────────────────────────────────────────────

    [Fact]
    public void MarkQrIssued_PaidTicketWithNoQr_SetsQrFields()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));

        var result = store.MarkQrIssued(ticket.Id, "qr-token-123", "QR-PAYLOAD", Now.AddMinutes(1));

        Assert.NotNull(result);
        Assert.Equal("qr-token-123", result!.QrToken);
        Assert.Equal("QR-PAYLOAD", result.QrPayload);
        Assert.Equal(Now.AddMinutes(1), result.QrIssuedAtUtc);
    }

    [Fact]
    public void MarkQrIssued_AlreadyHasQr_ReturnsOriginalTokenIdempotently()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));
        store.MarkPaid(ticket.Id, Now.AddMinutes(1));
        store.MarkQrIssued(ticket.Id, "original-token", "ORIGINAL", Now.AddMinutes(1));

        var result = store.MarkQrIssued(ticket.Id, "new-token", "NEW", Now.AddMinutes(2));

        Assert.NotNull(result);
        Assert.Equal("original-token", result!.QrToken);
    }

    [Fact]
    public void MarkQrIssued_ReservedTicket_ReturnsNull()
    {
        var store = CreateStore();
        var ticket = store.Reserve(DefaultRequest(), Now, TimeSpan.FromMinutes(15));

        Assert.Null(store.MarkQrIssued(ticket.Id, "token", "payload", Now));
    }
}

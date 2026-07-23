using zip02.Services.Notifications.Contracts;
using zip02.Services.Notifications.InMemory;
using zip02.Services.Ticketing.Contracts;

namespace Zip02.Tests.Notifications.Unit;

public sealed class InMemoryNotificationStoreTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketResponse BuildTicket(Guid? id = null, string email = "attendee@example.com")
    {
        var ticketId = id ?? Guid.NewGuid();
        return new TicketResponse
        {
            Id = ticketId,
            EventId = Guid.NewGuid(),
            AttendeeId = "attendee-1",
            AttendeeEmail = email,
            Status = TicketStatus.Paid,
            ReservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(20),
            PaidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
    }

    private static EmailNotificationRequest BuildEmailRequest(Guid ticketId, string correlationId = "corr-1")
        => new()
        {
            TicketId = ticketId,
            ToEmail = "attendee@example.com",
            Subject = "Your QR ticket",
            Body = "zip02://qr/tickets/…",
            CorrelationId = correlationId
        };

    // ── IssueQr ───────────────────────────────────────────────────────────────

    [Fact]
    public void IssueQr_NewTicket_ReturnsArtifactWithNonEmptyToken()
    {
        var store = new InMemoryNotificationStore();
        var ticket = BuildTicket();
        var now = DateTimeOffset.UtcNow;

        var qr = store.IssueQr(ticket, now);

        Assert.Equal(ticket.Id, qr.TicketId);
        Assert.False(string.IsNullOrWhiteSpace(qr.Token));
        Assert.False(string.IsNullOrWhiteSpace(qr.RenderedPayload));
        Assert.Equal(now, qr.CreatedAtUtc);
    }

    [Fact]
    public void IssueQr_NewTicket_RenderedPayloadContainsTicketId()
    {
        var store = new InMemoryNotificationStore();
        var ticket = BuildTicket();

        var qr = store.IssueQr(ticket, DateTimeOffset.UtcNow);

        Assert.Contains(ticket.Id.ToString(), qr.RenderedPayload);
    }

    [Fact]
    public void IssueQr_SameTicketTwice_ReturnsSameTokenIdempotently()
    {
        var store = new InMemoryNotificationStore();
        var ticket = BuildTicket();
        var now = DateTimeOffset.UtcNow;

        var first = store.IssueQr(ticket, now);
        var second = store.IssueQr(ticket, now.AddMinutes(1));

        Assert.Equal(first.Token, second.Token);
        Assert.Equal(first.RenderedPayload, second.RenderedPayload);
    }

    [Fact]
    public void IssueQr_DifferentTickets_ProduceDifferentTokens()
    {
        var store = new InMemoryNotificationStore();
        var ticket1 = BuildTicket();
        var ticket2 = BuildTicket();
        var now = DateTimeOffset.UtcNow;

        var qr1 = store.IssueQr(ticket1, now);
        var qr2 = store.IssueQr(ticket2, now);

        Assert.NotEqual(qr1.Token, qr2.Token);
    }

    // ── SendEmail ─────────────────────────────────────────────────────────────

    [Fact]
    public void SendEmail_NewRequest_ReturnsRecordWithSucceededStatus()
    {
        var store = new InMemoryNotificationStore();
        var ticketId = Guid.NewGuid();
        var request = BuildEmailRequest(ticketId);
        var now = DateTimeOffset.UtcNow;

        var record = store.SendEmail(request, now);

        Assert.Equal(ticketId, record.TicketId);
        Assert.Equal(request.ToEmail, record.ToEmail);
        Assert.Equal(request.Subject, record.Subject);
        Assert.Equal(NotificationStatus.Succeeded, record.Status);
        Assert.Equal(now, record.SentAtUtc);
        Assert.Equal(request.CorrelationId, record.CorrelationId);
    }

    [Fact]
    public void SendEmail_SameTicketTwice_ReturnsOriginalRecordIdempotently()
    {
        var store = new InMemoryNotificationStore();
        var ticketId = Guid.NewGuid();
        var request = BuildEmailRequest(ticketId);
        var now = DateTimeOffset.UtcNow;

        var first = store.SendEmail(request, now);
        var second = store.SendEmail(request, now.AddMinutes(5));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.SentAtUtc, second.SentAtUtc);
    }

    // ── GetQr ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetQr_KnownTicketId_ReturnsIssuedArtifact()
    {
        var store = new InMemoryNotificationStore();
        var ticket = BuildTicket();
        var issued = store.IssueQr(ticket, DateTimeOffset.UtcNow);

        var result = store.GetQr(ticket.Id);

        Assert.NotNull(result);
        Assert.Equal(issued.Token, result!.Token);
    }

    [Fact]
    public void GetQr_UnknownTicketId_ReturnsNull()
    {
        var store = new InMemoryNotificationStore();

        var result = store.GetQr(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── GetEmailForTicket ─────────────────────────────────────────────────────

    [Fact]
    public void GetEmailForTicket_KnownTicketId_ReturnsSentRecord()
    {
        var store = new InMemoryNotificationStore();
        var ticketId = Guid.NewGuid();
        store.SendEmail(BuildEmailRequest(ticketId), DateTimeOffset.UtcNow);

        var result = store.GetEmailForTicket(ticketId);

        Assert.NotNull(result);
        Assert.Equal(ticketId, result!.TicketId);
    }

    [Fact]
    public void GetEmailForTicket_UnknownTicketId_ReturnsNull()
    {
        var store = new InMemoryNotificationStore();

        var result = store.GetEmailForTicket(Guid.NewGuid());

        Assert.Null(result);
    }
}

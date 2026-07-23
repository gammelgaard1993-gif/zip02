using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using zip02.Services.CheckIn.Application;
using zip02.Services.Events;
using zip02.Services.Payments.Contracts;
using zip02.Services.Ticketing.Contracts;

namespace Zip02.Tests.EndToEnd;

public sealed class CheckInEdgeCaseTests
{
    private const string WebhookSecret = "test_stripe_webhook_secret";

    // Copenhagen City Hall — same geofence as MvpFlowTests
    private const double EventLat    = 55.6761;
    private const double EventLon    = 12.5683;
    private const double EventRadius = 500;

    // ~200 m north of center — inside the 500 m geofence
    private const double InsideLat = 55.6779;
    private const double InsideLon = EventLon;

    // ~2 km north of center — outside the 500 m geofence
    private const double OutsideLat = 55.6940;
    private const double OutsideLon = EventLon;

    // ── Check-in rejection tests ──────────────────────────────────────────────

    [Fact]
    public async Task CheckIn_OutsideGeofence_RespondsWithOutsideGeofenceReason()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var evt    = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2));
        var ticket = await PayAndGetQrTicketAsync(client, evt.Id, "a", "a@example.com", now);

        var response = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = OutsideLat,
            Longitude     = OutsideLon,
            OccurredAtUtc = now.AddMinutes(1)
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(CheckInFailureReason.OutsideGeofence, result.Reason);
        Assert.NotNull(result.DistanceMeters);
        Assert.True(result.DistanceMeters > EventRadius);
    }

    [Fact]
    public async Task CheckIn_BeforeEventStart_RespondsWithOutsideCheckInWindowReason()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        // Event that starts 30 minutes from now
        var evt    = await CreateEventAsync(client, now.AddMinutes(30), now.AddHours(3));
        var ticket = await PayAndGetQrTicketAsync(client, evt.Id, "b", "b@example.com", now.AddMinutes(1));

        var response = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = now   // before event start
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(CheckInFailureReason.OutsideCheckInWindow, result.Reason);
    }

    [Fact]
    public async Task CheckIn_AfterEventEnd_RespondsWithOutsideCheckInWindowReason()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        // Event that ended 1 hour ago
        var evt    = await CreateEventAsync(client, now.AddHours(-3), now.AddHours(-1));
        var ticket = await PayAndGetQrTicketAsync(client, evt.Id, "c", "c@example.com", now.AddHours(-2));

        var response = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = ticket.QrToken,
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = now   // after event end
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(CheckInFailureReason.OutsideCheckInWindow, result.Reason);
    }

    [Fact]
    public async Task CheckIn_WithInvalidQrToken_RespondsWithInvalidQrTokenReason()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var evt    = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2));
        var ticket = await PayAndGetQrTicketAsync(client, evt.Id, "d", "d@example.com", now);

        var response = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId      = ticket.Id,
            QrToken       = "totally-wrong-token",
            Latitude      = InsideLat,
            Longitude     = InsideLon,
            OccurredAtUtc = now.AddMinutes(1)
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Equal(CheckInFailureReason.InvalidQrToken, result.Reason);
    }

    // ── Capacity enforcement tests ────────────────────────────────────────────

    [Fact]
    public async Task Reserve_WhenEventAtCapacity_Returns409Conflict()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var evt = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2), capacity: 1);

        // First reservation fills the event
        await ReserveTicketAsync(client, evt.Id, "e", "e@example.com", Guid.NewGuid().ToString("N"));

        // Second reservation from a different attendee should be rejected
        var overCapacity = await client.PostAsJsonAsync("/tickets/reserve", new ReserveTicketRequest
        {
            EventId        = evt.Id,
            AttendeeId     = "f",
            AttendeeEmail  = "f@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Conflict, overCapacity.StatusCode);
    }

    [Fact]
    public async Task Reserve_WhenCapacityFreedByExpiry_Succeeds()
    {
        using var factory = new TestApiFactory();
        using var client  = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var evt = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2), capacity: 1);

        // Reserve fills the single slot
        await ReserveTicketAsync(client, evt.Id, "g", "g@example.com", Guid.NewGuid().ToString("N"));

        // Advance the clock past the 15-min TTL to expire the reservation
        var expireResponse = await client.PostAsJsonAsync("/tickets/expire-reservations",
            new ExpireReservationsRequest { ProcessedAtUtc = now.AddMinutes(20) });
        expireResponse.EnsureSuccessStatusCode();

        var expireResult = await expireResponse.Content.ReadFromJsonAsync<ExpireReservationsResponse>();
        Assert.NotNull(expireResult);
        Assert.Equal(1, expireResult!.ExpiredCount);

        // Slot is now free — new reservation should succeed
        var second = await ReserveTicketAsync(client, evt.Id, "h", "h@example.com", Guid.NewGuid().ToString("N"));
        Assert.Equal(TicketStatus.Reserved, second.Status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<EventResponse> CreateEventAsync(
        HttpClient client,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        int capacity = 100)
    {
        var response = await client.PostAsJsonAsync("/events", new CreateEventRequest
        {
            Name       = "Edge Case Test Event",
            StartAtUtc = startAtUtc,
            EndAtUtc   = endAtUtc,
            Capacity   = capacity,
            Geofence   = new GeofenceRequest
            {
                Latitude     = EventLat,
                Longitude    = EventLon,
                RadiusMeters = EventRadius
            }
        });

        response.EnsureSuccessStatusCode();
        var evt = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(evt);
        return evt!;
    }

    private static async Task<TicketResponse> ReserveTicketAsync(
        HttpClient client, Guid eventId, string attendeeId, string email, string idempotencyKey)
    {
        var response = await client.PostAsJsonAsync("/tickets/reserve", new ReserveTicketRequest
        {
            EventId        = eventId,
            AttendeeId     = attendeeId,
            AttendeeEmail  = email,
            IdempotencyKey = idempotencyKey
        });
        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(ticket);
        return ticket!;
    }

    /// <summary>
    /// Reserves a ticket, confirms payment via Stripe webhook, and returns the ticket
    /// with its QR token populated.
    /// </summary>
    private static async Task<TicketResponse> PayAndGetQrTicketAsync(
        HttpClient client, Guid eventId, string attendeeId, string email, DateTimeOffset paymentTime)
    {
        var reserved = await ReserveTicketAsync(client, eventId, attendeeId, email, Guid.NewGuid().ToString("N"));

        var payload = JsonSerializer.Serialize(new PaymentWebhookRequest
        {
            Provider         = "stripe",
            ProviderEventId  = $"evt_{Guid.NewGuid():N}",
            TicketId         = reserved.Id,
            EventId          = eventId,
            AmountMinorUnits = 2500,
            Currency         = "usd",
            Status           = PaymentStatus.Succeeded,
            OccurredAtUtc    = paymentTime,
            CorrelationId    = $"corr-{Guid.NewGuid():N}"
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig       = BuildStripeSignature(payload, WebhookSecret, timestamp);

        using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/payments/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        webhookRequest.Headers.Add("Stripe-Signature", $"t={timestamp},v1={sig}");

        var webhookResponse = await client.SendAsync(webhookRequest);
        webhookResponse.EnsureSuccessStatusCode();

        var ticket = await client.GetFromJsonAsync<TicketResponse>($"/tickets/{reserved.Id}");
        Assert.NotNull(ticket);
        Assert.Equal(TicketStatus.Paid, ticket!.Status);
        Assert.False(string.IsNullOrWhiteSpace(ticket.QrToken));
        return ticket;
    }

    private static string BuildStripeSignature(string payload, string secret, long unixTimestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{unixTimestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:Stripe:WebhookSecret"] = WebhookSecret
                });
            });
        }
    }
}

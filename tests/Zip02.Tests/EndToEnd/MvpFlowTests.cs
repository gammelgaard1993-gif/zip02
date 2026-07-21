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
using zip02.Services.Refunds.Application;
using zip02.Services.Ticketing.Contracts;

namespace Zip02.Tests.EndToEnd;

public sealed class MvpFlowTests
{
    private const string WebhookSecret = "test_stripe_webhook_secret";

    [Fact]
    public async Task Purchase_CheckIn_And_PreActivationRefundGuardrail_Work_EndToEnd()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var evt = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2));
        var reserved = await ReserveTicketAsync(client, evt.Id, "attendee-a", "attendee-a@example.com", Guid.NewGuid().ToString("N"));

        var payment = await SendSucceededPaymentWebhookAsync(client, reserved.Id, evt.Id, now, "corr-active");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        var paidTicket = await client.GetFromJsonAsync<TicketResponse>($"/tickets/{reserved.Id}");
        Assert.NotNull(paidTicket);
        Assert.Equal(TicketStatus.Paid, paidTicket!.Status);
        Assert.False(string.IsNullOrWhiteSpace(paidTicket.QrToken));

        var checkInResponse = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId = paidTicket.Id,
            QrToken = paidTicket.QrToken,
            Latitude = 55.6761,
            Longitude = 12.5683,
            OccurredAtUtc = now.AddMinutes(1)
        });
        checkInResponse.EnsureSuccessStatusCode();

        var checkIn = await checkInResponse.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(checkIn);
        Assert.True(checkIn!.Success);
        Assert.Equal(CheckInFailureReason.None, checkIn.Reason);

        var secondCheckInResponse = await client.PostAsJsonAsync("/checkin", new CheckInRequest
        {
            TicketId = paidTicket.Id,
            QrToken = paidTicket.QrToken,
            Latitude = 55.6761,
            Longitude = 12.5683,
            OccurredAtUtc = now.AddMinutes(2)
        });
        secondCheckInResponse.EnsureSuccessStatusCode();

        var secondCheckIn = await secondCheckInResponse.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.NotNull(secondCheckIn);
        Assert.False(secondCheckIn!.Success);
        Assert.Equal(CheckInFailureReason.AlreadyCheckedIn, secondCheckIn.Reason);

        var refundAttemptResponse = await client.PostAsJsonAsync($"/refunds/tickets/{paidTicket.Id}", new ManualRefundRequest
        {
            CorrelationId = "corr-refund-checkedin",
            RequestedAtUtc = now.AddMinutes(3)
        });
        refundAttemptResponse.EnsureSuccessStatusCode();

        var refundAttempt = await refundAttemptResponse.Content.ReadFromJsonAsync<RefundTicketResult>();
        Assert.NotNull(refundAttempt);
        Assert.False(refundAttempt!.Refunded);
        Assert.Equal(RefundDecisionReason.TicketCheckedIn, refundAttempt.Reason);
    }

    [Fact]
    public async Task EndedEvent_NoShowPaidTicket_IsRefundedByReconciliation()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var endedEvent = await CreateEventAsync(client, now.AddHours(-3), now.AddHours(-1));
        var reserved = await ReserveTicketAsync(client, endedEvent.Id, "attendee-b", "attendee-b@example.com", Guid.NewGuid().ToString("N"));

        var payment = await SendSucceededPaymentWebhookAsync(client, reserved.Id, endedEvent.Id, now.AddHours(-2), "corr-ended");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        var reconcileResponse = await client.PostAsync($"/refunds/events/{endedEvent.Id}/reconcile", content: null);
        reconcileResponse.EnsureSuccessStatusCode();

        var reconcile = await reconcileResponse.Content.ReadFromJsonAsync<NoShowReconciliationResult>();
        Assert.NotNull(reconcile);
        Assert.Equal(1, reconcile!.RefundedCount);
        Assert.Equal(1, reconcile.EvaluatedCount);

        var ticketResult = reconcile.Tickets.Single(t => t.TicketId == reserved.Id);
        Assert.True(ticketResult.Refunded);
        Assert.Equal(RefundDecisionReason.Refunded, ticketResult.Reason);

        var refundedTicket = await client.GetFromJsonAsync<TicketResponse>($"/tickets/{reserved.Id}");
        Assert.NotNull(refundedTicket);
        Assert.Equal(TicketStatus.Refunded, refundedTicket!.Status);
    }

    private static async Task<EventResponse> CreateEventAsync(HttpClient client, DateTimeOffset startAtUtc, DateTimeOffset endAtUtc)
    {
        var response = await client.PostAsJsonAsync("/events", new CreateEventRequest
        {
            Name = "MVP Test Event",
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            Capacity = 100,
            Geofence = new GeofenceRequest
            {
                Latitude = 55.6761,
                Longitude = 12.5683,
                RadiusMeters = 500
            }
        });

        response.EnsureSuccessStatusCode();
        var evt = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(evt);
        return evt!;
    }

    private static async Task<TicketResponse> ReserveTicketAsync(HttpClient client, Guid eventId, string attendeeId, string attendeeEmail, string idempotencyKey)
    {
        var response = await client.PostAsJsonAsync("/tickets/reserve", new ReserveTicketRequest
        {
            EventId = eventId,
            AttendeeId = attendeeId,
            AttendeeEmail = attendeeEmail,
            IdempotencyKey = idempotencyKey
        });

        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(ticket);
        Assert.Equal(TicketStatus.Reserved, ticket!.Status);
        return ticket;
    }

    private static async Task<PaymentRecord> SendSucceededPaymentWebhookAsync(HttpClient client, Guid ticketId, Guid eventId, DateTimeOffset occurredAtUtc, string correlationId)
    {
        var payloadObject = new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            TicketId = ticketId,
            EventId = eventId,
            AmountMinorUnits = 2500,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId
        };

        var payload = JsonSerializer.Serialize(payloadObject);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = CreateStripeSignature(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payment = await response.Content.ReadFromJsonAsync<PaymentRecord>();
        Assert.NotNull(payment);
        return payment!;
    }

    private static string CreateStripeSignature(string payload, string secret, long unixTimestamp)
    {
        var signedPayload = $"{unixTimestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
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

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using zip02.Services.Events;
using zip02.Services.Payments.Contracts;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.DynamoDB;
using zip02.Services.Ticketing.InMemory;
using Zip02.Tests.Infrastructure;

namespace Zip02.Tests.Payments.Integration;

/// <summary>
/// Integration tests for the Stripe webhook -> DynamoDB ticket state flow.
/// Requires a running LocalStack container (Docker). Skips gracefully otherwise.
/// Category=Integration isolates this suite in CI.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PaymentWebhookDynamoDbTests : IClassFixture<LocalStackFixture>
{
    private const string WebhookSecret = "integration_stripe_secret";
    private readonly LocalStackFixture _localStack;

    public PaymentWebhookDynamoDbTests(LocalStackFixture localStack)
    {
        _localStack = localStack;
    }

    [Fact]
    public async Task Webhook_Succeeds_And_Writes_Paid_Status_To_DynamoDB()
    {
        if (!_localStack.CheckAvailable()) return;

        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var evt = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2));

        var reserved = await client.PostAsJsonAsync("/tickets/reserve", new ReserveTicketRequest
        {
            EventId = evt.Id,
            AttendeeId = "integration-attendee",
            AttendeeEmail = "integration@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });
        reserved.EnsureSuccessStatusCode();
        var ticket = await reserved.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(ticket);
        Assert.Equal(TicketStatus.Reserved, ticket!.Status);

        var payment = await SendWebhookAsync(client, ticket.Id, evt.Id, now, PaymentStatus.Succeeded, "corr-dynamo-1");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        // Verify DynamoDB record directly
        var dbItem = await GetTicketFromDynamoAsync(ticket.Id);
        Assert.Equal("Paid", dbItem["Status"].S);
        Assert.True(dbItem.ContainsKey("PaidAtUtc"));
        Assert.True(dbItem.ContainsKey("QrToken"));
    }

    [Fact]
    public async Task Duplicate_Webhook_Is_Idempotent_And_Does_Not_Double_Transition()
    {
        if (!_localStack.CheckAvailable()) return;

        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var evt = await CreateEventAsync(client, now.AddMinutes(-5), now.AddHours(2));

        var reserved = await client.PostAsJsonAsync("/tickets/reserve", new ReserveTicketRequest
        {
            EventId = evt.Id,
            AttendeeId = "integration-attendee-dup",
            AttendeeEmail = "dup@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });
        reserved.EnsureSuccessStatusCode();
        var ticket = (await reserved.Content.ReadFromJsonAsync<TicketResponse>())!;

        var providerEventId = $"evt_{Guid.NewGuid():N}";
        await SendWebhookAsync(client, ticket.Id, evt.Id, now, PaymentStatus.Succeeded, "corr-dup-1", providerEventId);

        // Second delivery of the same Stripe event
        var secondResponse = await SendRawWebhookAsync(client, ticket.Id, evt.Id, now, PaymentStatus.Succeeded, "corr-dup-1", providerEventId);
        Assert.True(secondResponse.IsSuccessStatusCode, $"Expected 200, got {secondResponse.StatusCode}");

        // DynamoDB must still show Paid (not a double-write or error)
        var dbItem = await GetTicketFromDynamoAsync(ticket.Id);
        Assert.Equal("Paid", dbItem["Status"].S);
    }

    [Fact]
    public async Task Webhook_With_Invalid_Signature_Returns_Unauthorized()
    {
        if (!_localStack.CheckAvailable()) return;

        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var payload = JsonSerializer.Serialize(new { anything = "value" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", "t=1234567890,v1=invalidsignature");

        var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> BuildFactory()
    {
        return new LocalStackApiFactory(_localStack.ServiceUrl, _localStack.TableName);
    }

    private async Task<Dictionary<string, AttributeValue>> GetTicketFromDynamoAsync(Guid ticketId)
    {
        var result = await _localStack.DynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _localStack.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"TICKET#{ticketId:N}"),
                ["SK"] = new("TICKET")
            }
        });

        Assert.True(result.IsItemSet, $"Ticket {ticketId} not found in DynamoDB.");
        return result.Item;
    }

    private static async Task<EventResponse> CreateEventAsync(HttpClient client, DateTimeOffset start, DateTimeOffset end)
    {
        var response = await client.PostAsJsonAsync("/events", new CreateEventRequest
        {
            Name = "Integration Test Event",
            StartAtUtc = start,
            EndAtUtc = end,
            Capacity = 50,
            Geofence = new GeofenceRequest { Latitude = 55.6761, Longitude = 12.5683, RadiusMeters = 300 }
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    private static async Task<PaymentRecord> SendWebhookAsync(
        HttpClient client, Guid ticketId, Guid eventId,
        DateTimeOffset occurredAtUtc, PaymentStatus status,
        string correlationId, string? providerEventId = null)
    {
        var response = await SendRawWebhookAsync(client, ticketId, eventId, occurredAtUtc, status, correlationId, providerEventId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentRecord>())!;
    }

    private static async Task<HttpResponseMessage> SendRawWebhookAsync(
        HttpClient client, Guid ticketId, Guid eventId,
        DateTimeOffset occurredAtUtc, PaymentStatus status,
        string correlationId, string? providerEventId = null)
    {
        var payload = JsonSerializer.Serialize(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = providerEventId ?? $"evt_{Guid.NewGuid():N}",
            TicketId = ticketId,
            EventId = eventId,
            AmountMinorUnits = 2500,
            Currency = "usd",
            Status = status,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={sig}");
        return await client.SendAsync(request);
    }

    private sealed class LocalStackApiFactory(string localStackUrl, string tableName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:Stripe:WebhookSecret"] = WebhookSecret,
                    ["AWS:DynamoDB:ServiceURL"] = localStackUrl,
                    ["AWS:DynamoDB:TableName"] = tableName,
                    ["AWS:Region"] = "eu-west-1"
                });
            });

            builder.ConfigureServices((ctx, services) =>
            {
                // Swap in-memory ticket store for DynamoDB-backed store
                var descriptor = services.Single(d => d.ServiceType == typeof(ITicketStore));
                services.Remove(descriptor);

                var serviceUrl = ctx.Configuration["AWS:DynamoDB:ServiceURL"]!;
                var table = ctx.Configuration["AWS:DynamoDB:TableName"]!;

                services.AddSingleton<ITicketStore>(_ =>
                    new DynamoDbTicketStore(LocalStackFixture.BuildClient(serviceUrl), table));
            });
        }
    }
}

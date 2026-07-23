using zip02.Services.Payments.Contracts;
using zip02.Services.Payments.DynamoDB;
using Zip02.Tests.Infrastructure;

namespace Zip02.Tests.Payments.Integration;

[Trait("Category", "Integration")]
public sealed class DynamoDbPaymentStoreTests : IClassFixture<LocalStackFixture>
{
    private readonly LocalStackFixture _localStack;

    public DynamoDbPaymentStoreTests(LocalStackFixture localStack)
    {
        _localStack = localStack;
    }

    [Fact]
    public void Record_ThenGetByProviderEventId_RoundTripsPayment()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbPaymentStore(_localStack.DynamoDb, _localStack.TableName);

        var ticketId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var providerEventId = $"evt_{Guid.NewGuid():N}";

        var recorded = store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = providerEventId,
            TicketId = ticketId,
            EventId = eventId,
            AmountMinorUnits = 2500,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "corr-pay-1"
        }, DateTimeOffset.UtcNow);

        var loaded = store.GetByProviderEventId(providerEventId);

        Assert.NotNull(loaded);
        Assert.Equal(recorded.Id, loaded!.Id);
        Assert.Equal(ticketId, loaded.TicketId);
        Assert.Equal(eventId, loaded.EventId);
        Assert.Equal("stripe", loaded.Provider);
        Assert.Equal(providerEventId, loaded.ProviderEventId);
        Assert.Equal("USD", loaded.Currency);
        Assert.Equal(PaymentStatus.Succeeded, loaded.Status);
    }

    [Fact]
    public void Record_SameProviderEventId_IsIdempotent()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbPaymentStore(_localStack.DynamoDb, _localStack.TableName);

        var providerEventId = $"evt_{Guid.NewGuid():N}";
        var ticketId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var first = store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = providerEventId,
            TicketId = ticketId,
            EventId = eventId,
            AmountMinorUnits = 1500,
            Currency = "eur",
            Status = PaymentStatus.Succeeded,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "corr-idem-a"
        }, DateTimeOffset.UtcNow);

        var second = store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = providerEventId,
            TicketId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            AmountMinorUnits = 9999,
            Currency = "usd",
            Status = PaymentStatus.Failed,
            OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            CorrelationId = "corr-idem-b"
        }, DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.ProviderEventId, second.ProviderEventId);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.AmountMinorUnits, second.AmountMinorUnits);
    }

    [Fact]
    public void GetForTicket_ReturnsOnlyThatTicketRecords()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbPaymentStore(_localStack.DynamoDb, _localStack.TableName);

        var ticketA = Guid.NewGuid();
        var ticketB = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            TicketId = ticketA,
            EventId = eventId,
            AmountMinorUnits = 1000,
            Currency = "usd",
            Status = PaymentStatus.Pending,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "corr-a"
        }, DateTimeOffset.UtcNow);

        store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            TicketId = ticketA,
            EventId = eventId,
            AmountMinorUnits = 1000,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "corr-a2"
        }, DateTimeOffset.UtcNow);

        store.Record(new PaymentWebhookRequest
        {
            Provider = "stripe",
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            TicketId = ticketB,
            EventId = eventId,
            AmountMinorUnits = 2000,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = "corr-b"
        }, DateTimeOffset.UtcNow);

        var recordsForA = store.GetForTicket(ticketA);

        Assert.Equal(2, recordsForA.Count);
        Assert.All(recordsForA, r => Assert.Equal(ticketA, r.TicketId));
    }

    [Fact]
    public void GetByProviderEventId_Unknown_ReturnsNull()
    {
        if (!_localStack.CheckAvailable()) return;

        var store = new DynamoDbPaymentStore(_localStack.DynamoDb, _localStack.TableName);

        var loaded = store.GetByProviderEventId($"evt_{Guid.NewGuid():N}");

        Assert.Null(loaded);
    }
}

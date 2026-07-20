using System.Collections.Concurrent;

namespace zip02.Services.Payments;

public interface IPaymentStore
{
    PaymentRecord Record(PaymentWebhookRequest request, DateTimeOffset processedAtUtc);

    PaymentRecord? GetByProviderEventId(string providerEventId);

    IReadOnlyCollection<PaymentRecord> GetForTicket(Guid ticketId);
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<string, PaymentRecord> _recordsByProviderEventId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<PaymentRecord>> _recordsByTicketId = new();

    public PaymentRecord Record(PaymentWebhookRequest request, DateTimeOffset processedAtUtc)
    {
        if (_recordsByProviderEventId.TryGetValue(request.ProviderEventId!, out var existing))
        {
            return existing;
        }

        var record = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            TicketId = request.TicketId,
            EventId = request.EventId,
            Provider = request.Provider!.Trim(),
            ProviderEventId = request.ProviderEventId!.Trim(),
            AmountMinorUnits = request.AmountMinorUnits!.Value,
            Currency = request.Currency!.Trim().ToUpperInvariant(),
            Status = request.Status!.Value,
            OccurredAtUtc = request.OccurredAtUtc!.Value,
            ProcessedAtUtc = processedAtUtc,
            CorrelationId = request.CorrelationId!.Trim()
        };

        if (_recordsByProviderEventId.TryAdd(record.ProviderEventId, record))
        {
            var bag = _recordsByTicketId.GetOrAdd(record.TicketId, _ => new ConcurrentBag<PaymentRecord>());
            bag.Add(record);
            return record;
        }

        return _recordsByProviderEventId[record.ProviderEventId];
    }

    public PaymentRecord? GetByProviderEventId(string providerEventId)
    {
        return _recordsByProviderEventId.TryGetValue(providerEventId, out var record) ? record : null;
    }

    public IReadOnlyCollection<PaymentRecord> GetForTicket(Guid ticketId)
    {
        return _recordsByTicketId.TryGetValue(ticketId, out var records)
            ? records.ToArray()
            : Array.Empty<PaymentRecord>();
    }
}

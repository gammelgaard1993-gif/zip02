using System.Collections.Concurrent;
using zip02.Services.Ticketing.Contracts;

namespace zip02.Services.Ticketing.InMemory;

public interface ITicketStore
{
    TicketResponse Reserve(ReserveTicketRequest request, DateTimeOffset nowUtc, TimeSpan reservationTtl);

    TicketResponse? Get(Guid id);

    TicketResponse? Update(Guid id, UpdateTicketRequest request);

    TicketResponse? MarkPaid(Guid id, DateTimeOffset paidAtUtc);

    TicketResponse? MarkCheckedIn(Guid id, DateTimeOffset checkedInAtUtc);

    TicketResponse? MarkExpired(Guid id, DateTimeOffset expiredAtUtc);

    TicketResponse? MarkRefunded(Guid id, DateTimeOffset refundedAtUtc);

    TicketResponse? MarkCancelled(Guid id, DateTimeOffset cancelledAtUtc);

    TicketResponse? MarkQrIssued(Guid id, string token, string renderedPayload, DateTimeOffset issuedAtUtc);
}

public sealed class InMemoryTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<Guid, TicketResponse> _tickets = new();
    private readonly ConcurrentDictionary<string, Guid> _reservationKeys = new(StringComparer.OrdinalIgnoreCase);

    public TicketResponse Reserve(ReserveTicketRequest request, DateTimeOffset nowUtc, TimeSpan reservationTtl)
    {
        if (_reservationKeys.TryGetValue(request.IdempotencyKey!, out var existingId) && _tickets.TryGetValue(existingId, out var existing))
        {
            return existing;
        }

        var ticket = new TicketResponse
        {
            Id = Guid.NewGuid(),
            EventId = request.EventId,
            AttendeeId = request.AttendeeId!.Trim(),
            AttendeeEmail = request.AttendeeEmail!.Trim(),
            Status = TicketStatus.Reserved,
            ReservedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(reservationTtl)
        };

        _tickets[ticket.Id] = ticket;
        _reservationKeys[request.IdempotencyKey!] = ticket.Id;
        return ticket;
    }

    public TicketResponse? Get(Guid id)
    {
        return _tickets.TryGetValue(id, out var ticket) ? ticket : null;
    }

    public TicketResponse? Update(Guid id, UpdateTicketRequest request)
    {
        while (true)
        {
            if (!_tickets.TryGetValue(id, out var current))
            {
                return null;
            }

            if (current.Status is TicketStatus.CheckedIn or TicketStatus.Refunded or TicketStatus.Cancelled or TicketStatus.Expired)
            {
                return current;
            }

            var updated = new TicketResponse
            {
                Id = current.Id,
                EventId = current.EventId,
                AttendeeId = current.AttendeeId,
                AttendeeEmail = string.IsNullOrWhiteSpace(request.AttendeeEmail) ? current.AttendeeEmail : request.AttendeeEmail.Trim(),
                Status = current.Status,
                ReservedAtUtc = current.ReservedAtUtc,
                ExpiresAtUtc = current.ExpiresAtUtc,
                PaidAtUtc = current.PaidAtUtc,
                CheckedInAtUtc = current.CheckedInAtUtc,
                ExpiredAtUtc = current.ExpiredAtUtc,
                RefundedAtUtc = current.RefundedAtUtc,
                CancelledAtUtc = current.CancelledAtUtc,
                QrToken = current.QrToken,
                QrPayload = current.QrPayload,
                QrIssuedAtUtc = current.QrIssuedAtUtc,
                Notes = request.Notes ?? current.Notes
            };

            if (_tickets.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }
    }

    public TicketResponse? MarkPaid(Guid id, DateTimeOffset paidAtUtc)
    {
        return Transition(id, current => current.Status switch
        {
            TicketStatus.Reserved => Copy(current, TicketStatus.Paid, paidAtUtc: paidAtUtc),
            TicketStatus.Paid => current,
            _ => null
        });
    }

    public TicketResponse? MarkCheckedIn(Guid id, DateTimeOffset checkedInAtUtc)
    {
        return Transition(id, current => current.Status == TicketStatus.Paid
            ? Copy(current, TicketStatus.CheckedIn, checkedInAtUtc: checkedInAtUtc)
            : null);
    }

    public TicketResponse? MarkExpired(Guid id, DateTimeOffset expiredAtUtc)
    {
        return Transition(id, current => current.Status == TicketStatus.Reserved
            ? Copy(current, TicketStatus.Expired, expiredAtUtc: expiredAtUtc)
            : null);
    }

    public TicketResponse? MarkRefunded(Guid id, DateTimeOffset refundedAtUtc)
    {
        return Transition(id, current => current.Status == TicketStatus.Paid
            ? Copy(current, TicketStatus.Refunded, refundedAtUtc: refundedAtUtc)
            : null);
    }

    public TicketResponse? MarkCancelled(Guid id, DateTimeOffset cancelledAtUtc)
    {
        return Transition(id, current => current.Status is TicketStatus.Reserved or TicketStatus.Paid
            ? Copy(current, TicketStatus.Cancelled, cancelledAtUtc: cancelledAtUtc)
            : null);
    }

    public TicketResponse? MarkQrIssued(Guid id, string token, string renderedPayload, DateTimeOffset issuedAtUtc)
    {
        return Transition(id, current => current.Status == TicketStatus.Paid
            ? current.QrToken is null
                ? Copy(current, current.Status, qrToken: token, qrPayload: renderedPayload, qrIssuedAtUtc: issuedAtUtc)
                : current
            : null);
    }

    private static TicketResponse Copy(
        TicketResponse source,
        TicketStatus status,
        DateTimeOffset? paidAtUtc = null,
        DateTimeOffset? checkedInAtUtc = null,
        DateTimeOffset? expiredAtUtc = null,
        DateTimeOffset? refundedAtUtc = null,
        DateTimeOffset? cancelledAtUtc = null,
        string? qrToken = null,
        string? qrPayload = null,
        DateTimeOffset? qrIssuedAtUtc = null)
    {
        return new TicketResponse
        {
            Id = source.Id,
            EventId = source.EventId,
            AttendeeId = source.AttendeeId,
            AttendeeEmail = source.AttendeeEmail,
            Status = status,
            ReservedAtUtc = source.ReservedAtUtc,
            ExpiresAtUtc = source.ExpiresAtUtc,
            PaidAtUtc = paidAtUtc ?? source.PaidAtUtc,
            CheckedInAtUtc = checkedInAtUtc ?? source.CheckedInAtUtc,
            ExpiredAtUtc = expiredAtUtc ?? source.ExpiredAtUtc,
            RefundedAtUtc = refundedAtUtc ?? source.RefundedAtUtc,
            CancelledAtUtc = cancelledAtUtc ?? source.CancelledAtUtc,
            QrToken = qrToken ?? source.QrToken,
            QrPayload = qrPayload ?? source.QrPayload,
            QrIssuedAtUtc = qrIssuedAtUtc ?? source.QrIssuedAtUtc,
            Notes = source.Notes
        };
    }

    private TicketResponse? Transition(Guid id, Func<TicketResponse, TicketResponse?> transition)
    {
        while (true)
        {
            if (!_tickets.TryGetValue(id, out var current))
            {
                return null;
            }

            var updated = transition(current);
            if (updated is null)
            {
                return null;
            }

            if (_tickets.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }
    }
}

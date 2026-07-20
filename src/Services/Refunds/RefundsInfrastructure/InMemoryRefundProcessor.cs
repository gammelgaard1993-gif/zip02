using zip02.Services.Events;
using zip02.Services.Refunds.Application;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Services.Refunds.Infrastructure;

public interface IRefundProcessor
{
    NoShowReconciliationResult ReconcileNoShows(Guid eventId, DateTimeOffset processedAtUtc);

    RefundTicketResult RefundBeforeActivation(Guid ticketId, DateTimeOffset processedAtUtc, string correlationId);
}

public sealed class InMemoryRefundProcessor(IEventStore eventStore, ITicketStore ticketStore) : IRefundProcessor
{
    public NoShowReconciliationResult ReconcileNoShows(Guid eventId, DateTimeOffset processedAtUtc)
    {
        var evt = eventStore.Get(eventId);
        if (evt is null)
        {
            return new NoShowReconciliationResult
            {
                EventId = eventId,
                ProcessedAtUtc = processedAtUtc,
                EvaluatedCount = 0,
                RefundedCount = 0,
                Tickets = new[]
                {
                    new RefundTicketResult
                    {
                        TicketId = Guid.Empty,
                        EventId = eventId,
                        Refunded = false,
                        Reason = RefundDecisionReason.EventNotFound,
                        ProcessedAtUtc = processedAtUtc
                    }
                }
            };
        }

        if (processedAtUtc < evt.EndAtUtc)
        {
            return new NoShowReconciliationResult
            {
                EventId = eventId,
                ProcessedAtUtc = processedAtUtc,
                EvaluatedCount = 0,
                RefundedCount = 0,
                Tickets = new[]
                {
                    new RefundTicketResult
                    {
                        TicketId = Guid.Empty,
                        EventId = eventId,
                        Refunded = false,
                        Reason = RefundDecisionReason.EventNotEnded,
                        ProcessedAtUtc = processedAtUtc
                    }
                }
            };
        }

        var tickets = ticketStore.GetByEvent(eventId);
        var results = new List<RefundTicketResult>(tickets.Count);
        var refundedCount = 0;

        foreach (var ticket in tickets)
        {
            if (ticket.Status == TicketStatus.CheckedIn)
            {
                results.Add(Result(ticket, false, RefundDecisionReason.TicketCheckedIn, processedAtUtc));
                continue;
            }

            if (ticket.Status == TicketStatus.Refunded)
            {
                results.Add(Result(ticket, false, RefundDecisionReason.AlreadyRefunded, processedAtUtc));
                continue;
            }

            if (ticket.Status != TicketStatus.Paid)
            {
                results.Add(Result(ticket, false, RefundDecisionReason.TicketNotPaid, processedAtUtc));
                continue;
            }

            var refunded = ticketStore.MarkRefunded(ticket.Id, processedAtUtc);
            if (refunded is null)
            {
                results.Add(Result(ticket, false, RefundDecisionReason.TicketNotPaid, processedAtUtc));
                continue;
            }

            refundedCount++;
            results.Add(Result(refunded, true, RefundDecisionReason.Refunded, processedAtUtc));
        }

        return new NoShowReconciliationResult
        {
            EventId = eventId,
            ProcessedAtUtc = processedAtUtc,
            EvaluatedCount = tickets.Count,
            RefundedCount = refundedCount,
            Tickets = results
        };
    }

    public RefundTicketResult RefundBeforeActivation(Guid ticketId, DateTimeOffset processedAtUtc, string correlationId)
    {
        _ = correlationId;
        var ticket = ticketStore.Get(ticketId);
        if (ticket is null)
        {
            return new RefundTicketResult
            {
                TicketId = ticketId,
                EventId = Guid.Empty,
                Refunded = false,
                Reason = RefundDecisionReason.TicketNotFound,
                ProcessedAtUtc = processedAtUtc
            };
        }

        if (ticket.Status == TicketStatus.CheckedIn)
        {
            return Result(ticket, false, RefundDecisionReason.TicketCheckedIn, processedAtUtc);
        }

        if (ticket.Status == TicketStatus.Refunded)
        {
            return Result(ticket, false, RefundDecisionReason.AlreadyRefunded, processedAtUtc);
        }

        if (ticket.Status != TicketStatus.Paid)
        {
            return Result(ticket, false, RefundDecisionReason.TicketNotPaid, processedAtUtc);
        }

        var refunded = ticketStore.MarkRefunded(ticket.Id, processedAtUtc);
        return refunded is null
            ? Result(ticket, false, RefundDecisionReason.TicketNotPaid, processedAtUtc)
            : Result(refunded, true, RefundDecisionReason.Refunded, processedAtUtc);
    }

    private static RefundTicketResult Result(TicketResponse ticket, bool refunded, RefundDecisionReason reason, DateTimeOffset processedAtUtc)
    {
        return new RefundTicketResult
        {
            TicketId = ticket.Id,
            EventId = ticket.EventId,
            Refunded = refunded,
            Reason = reason,
            ProcessedAtUtc = processedAtUtc
        };
    }
}

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using zip02.Services.Ticketing.Contracts;
using zip02.Services.Ticketing.InMemory;
using static zip02.Services.Ticketing.Contracts.TicketCopy;

namespace zip02.Services.Ticketing.DynamoDB;

/// <summary>
/// DynamoDB-backed implementation of <see cref="ITicketStore"/>.
/// Uses single-table design (ADR-001):
///   PK = TICKET#&lt;ticketId&gt;
///   SK = TICKET
/// All state transitions use conditional writes to enforce state-machine integrity.
/// </summary>
public sealed class DynamoDbTicketStore(IAmazonDynamoDB dynamoDb, string tableName) : ITicketStore
{
    private const string Pk = "PK";
    private const string Sk = "SK";
    private const string SkValue = "TICKET";

    private static string TicketPk(Guid id) => $"TICKET#{id:N}";

    public TicketResponse Reserve(ReserveTicketRequest request, DateTimeOffset nowUtc, TimeSpan reservationTtl)
    {
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

        var item = ToItem(ticket);

        // Idempotency: if idempotency key item already exists return it
        var idempotencyPk = $"IDEM#{request.IdempotencyKey!.Trim()}";
        try
        {
            dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    [Pk] = new(idempotencyPk),
                    [Sk] = new("IDEM"),
                    ["TicketId"] = new(ticket.Id.ToString("N"))
                },
                ConditionExpression = "attribute_not_exists(PK)"
            }).GetAwaiter().GetResult();
        }
        catch (ConditionalCheckFailedException)
        {
            // Already reserved with this key – look up and return existing ticket
            var existing = GetByIdempotencyKey(idempotencyPk);
            if (existing is not null)
            {
                return existing;
            }
        }

        dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = tableName,
            Item = item,
            ConditionExpression = "attribute_not_exists(PK)"
        }).GetAwaiter().GetResult();

        return ticket;
    }

    public TicketResponse? Get(Guid id)
    {
        var result = dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = Key(id)
        }).GetAwaiter().GetResult();

        return result.IsItemSet ? FromItem(result.Item) : null;
    }

    public IReadOnlyCollection<TicketResponse> GetByEvent(Guid eventId)
    {
        // In a real implementation this uses a GSI; for integration test purposes a scan is acceptable.
        var result = dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = tableName,
            FilterExpression = "EventId = :eid AND SK = :sk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":eid"] = new(eventId.ToString("N")),
                [":sk"] = new(SkValue)
            }
        }).GetAwaiter().GetResult();

        return result.Items.Select(FromItem).ToArray();
    }

    public TicketResponse? Update(Guid id, UpdateTicketRequest request)
    {
        var current = Get(id);
        if (current is null)
        {
            return null;
        }

        if (current.Status is TicketStatus.CheckedIn or TicketStatus.Refunded or TicketStatus.Cancelled or TicketStatus.Expired)
        {
            return current;
        }

        var updated = Copy(
            current,
            current.Status,
            attendeeEmail: string.IsNullOrWhiteSpace(request.AttendeeEmail) ? null : request.AttendeeEmail.Trim(),
            notes: request.Notes);

        PutConditionally(updated, current.Status.ToString());
        return updated;
    }

    public TicketResponse? MarkPaid(Guid id, DateTimeOffset paidAtUtc)
    {
        var current = Get(id);
        if (current is null)
        {
            return null;
        }

        if (current.Status == TicketStatus.Paid)
        {
            return current;
        }

        if (current.Status != TicketStatus.Reserved)
        {
            return null;
        }

        var updated = Copy(current, TicketStatus.Paid, paidAtUtc: paidAtUtc);
        return PutConditionally(updated, TicketStatus.Reserved.ToString()) ? updated : null;
    }

    public TicketResponse? MarkCheckedIn(Guid id, DateTimeOffset checkedInAtUtc)
    {
        var current = Get(id);
        if (current is null || current.Status != TicketStatus.Paid)
        {
            return null;
        }

        var updated = Copy(current, TicketStatus.CheckedIn, checkedInAtUtc: checkedInAtUtc);
        return PutConditionally(updated, TicketStatus.Paid.ToString()) ? updated : null;
    }

    public TicketResponse? MarkExpired(Guid id, DateTimeOffset expiredAtUtc)
    {
        var current = Get(id);
        if (current is null || current.Status != TicketStatus.Reserved)
        {
            return null;
        }

        var updated = Copy(current, TicketStatus.Expired, expiredAtUtc: expiredAtUtc);
        return PutConditionally(updated, TicketStatus.Reserved.ToString()) ? updated : null;
    }

    public IReadOnlyCollection<TicketResponse> ExpireReservations(DateTimeOffset processedAtUtc)
    {
        var candidates = dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = tableName,
            FilterExpression = "#st = :reserved AND SK = :sk AND ExpiresAtUtc <= :now",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#st"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":reserved"] = new(TicketStatus.Reserved.ToString()),
                [":sk"] = new(SkValue),
                [":now"] = new(processedAtUtc.ToString("O"))
            }
        }).GetAwaiter().GetResult();

        var expired = new List<TicketResponse>();
        foreach (var item in candidates.Items)
        {
            var ticket = FromItem(item);
            var result = MarkExpired(ticket.Id, processedAtUtc);
            if (result is not null)
            {
                expired.Add(result);
            }
        }

        return expired;
    }

    public TicketResponse? MarkRefunded(Guid id, DateTimeOffset refundedAtUtc)
    {
        var current = Get(id);
        if (current is null || current.Status != TicketStatus.Paid)
        {
            return null;
        }

        var updated = Copy(current, TicketStatus.Refunded, refundedAtUtc: refundedAtUtc);
        return PutConditionally(updated, TicketStatus.Paid.ToString()) ? updated : null;
    }

    public TicketResponse? MarkCancelled(Guid id, DateTimeOffset cancelledAtUtc)
    {
        var current = Get(id);
        if (current is null || current.Status is not (TicketStatus.Reserved or TicketStatus.Paid))
        {
            return null;
        }

        var expectedStatus = current.Status.ToString();
        var updated = Copy(current, TicketStatus.Cancelled, cancelledAtUtc: cancelledAtUtc);
        return PutConditionally(updated, expectedStatus) ? updated : null;
    }

    public TicketResponse? MarkQrIssued(Guid id, string token, string renderedPayload, DateTimeOffset issuedAtUtc)
    {
        var current = Get(id);
        if (current is null || current.Status != TicketStatus.Paid)
        {
            return null;
        }

        if (current.QrToken is not null)
        {
            return current; // Already issued – idempotent
        }

        var updated = Copy(current, current.Status, qrToken: token, qrPayload: renderedPayload, qrIssuedAtUtc: issuedAtUtc);
        return PutConditionally(updated, TicketStatus.Paid.ToString()) ? updated : null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private TicketResponse? GetByIdempotencyKey(string idempotencyPk)
    {
        var result = dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [Pk] = new(idempotencyPk),
                [Sk] = new("IDEM")
            }
        }).GetAwaiter().GetResult();

        if (!result.IsItemSet)
        {
            return null;
        }

        var ticketId = Guid.Parse(result.Item["TicketId"].S);
        return Get(ticketId);
    }

    private bool PutConditionally(TicketResponse updated, string expectedStatus)
    {
        try
        {
            dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = tableName,
                Item = ToItem(updated),
                ConditionExpression = "#st = :expected",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#st"] = "Status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expected"] = new(expectedStatus)
                }
            }).GetAwaiter().GetResult();

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid id) => new()
    {
        [Pk] = new(TicketPk(id)),
        [Sk] = new(SkValue)
    };

    private static Dictionary<string, AttributeValue> ToItem(TicketResponse t)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [Pk] = new(TicketPk(t.Id)),
            [Sk] = new(SkValue),
            ["Id"] = new(t.Id.ToString("N")),
            ["EventId"] = new(t.EventId.ToString("N")),
            ["AttendeeId"] = new(t.AttendeeId),
            ["AttendeeEmail"] = new(t.AttendeeEmail),
            ["Status"] = new(t.Status.ToString()),
            ["ReservedAtUtc"] = new(t.ReservedAtUtc.ToString("O")),
            ["ExpiresAtUtc"] = new(t.ExpiresAtUtc.ToString("O"))
        };

        if (t.PaidAtUtc.HasValue) item["PaidAtUtc"] = new(t.PaidAtUtc.Value.ToString("O"));
        if (t.CheckedInAtUtc.HasValue) item["CheckedInAtUtc"] = new(t.CheckedInAtUtc.Value.ToString("O"));
        if (t.ExpiredAtUtc.HasValue) item["ExpiredAtUtc"] = new(t.ExpiredAtUtc.Value.ToString("O"));
        if (t.RefundedAtUtc.HasValue) item["RefundedAtUtc"] = new(t.RefundedAtUtc.Value.ToString("O"));
        if (t.CancelledAtUtc.HasValue) item["CancelledAtUtc"] = new(t.CancelledAtUtc.Value.ToString("O"));
        if (t.QrToken is not null) item["QrToken"] = new(t.QrToken);
        if (t.QrPayload is not null) item["QrPayload"] = new(t.QrPayload);
        if (t.QrIssuedAtUtc.HasValue) item["QrIssuedAtUtc"] = new(t.QrIssuedAtUtc.Value.ToString("O"));
        if (t.Notes is not null) item["Notes"] = new(t.Notes);

        return item;
    }

    private static TicketResponse FromItem(Dictionary<string, AttributeValue> item) => new()
    {
        Id = Guid.Parse(item["Id"].S),
        EventId = Guid.Parse(item["EventId"].S),
        AttendeeId = item["AttendeeId"].S,
        AttendeeEmail = item["AttendeeEmail"].S,
        Status = Enum.Parse<TicketStatus>(item["Status"].S),
        ReservedAtUtc = DateTimeOffset.Parse(item["ReservedAtUtc"].S),
        ExpiresAtUtc = DateTimeOffset.Parse(item["ExpiresAtUtc"].S),
        PaidAtUtc = item.TryGetValue("PaidAtUtc", out var p) ? DateTimeOffset.Parse(p.S) : null,
        CheckedInAtUtc = item.TryGetValue("CheckedInAtUtc", out var c) ? DateTimeOffset.Parse(c.S) : null,
        ExpiredAtUtc = item.TryGetValue("ExpiredAtUtc", out var e) ? DateTimeOffset.Parse(e.S) : null,
        RefundedAtUtc = item.TryGetValue("RefundedAtUtc", out var r) ? DateTimeOffset.Parse(r.S) : null,
        CancelledAtUtc = item.TryGetValue("CancelledAtUtc", out var ca) ? DateTimeOffset.Parse(ca.S) : null,
        QrToken = item.TryGetValue("QrToken", out var qt) ? qt.S : null,
        QrPayload = item.TryGetValue("QrPayload", out var qp) ? qp.S : null,
        QrIssuedAtUtc = item.TryGetValue("QrIssuedAtUtc", out var qi) ? DateTimeOffset.Parse(qi.S) : null,
        Notes = item.TryGetValue("Notes", out var n) ? n.S : null
    };
}

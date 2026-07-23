using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using zip02.Services.Payments.Contracts;
using zip02.Services.Payments.InMemory;

namespace zip02.Services.Payments.DynamoDB;

/// <summary>
/// DynamoDB-backed implementation of <see cref="IPaymentStore"/>.
/// Single-table item shapes:
///   Payment record:
///     PK = PAYMENT#&lt;paymentId:N&gt;
///     SK = PAYMENT
///   Idempotency index:
///     PK = PAYEVT#&lt;providerEventId&gt;
///     SK = PAYEVT
///     PaymentId = &lt;paymentId:N&gt;
/// </summary>
public sealed class DynamoDbPaymentStore(IAmazonDynamoDB dynamoDb, string tableName) : IPaymentStore
{
    private const string Pk = "PK";
    private const string Sk = "SK";
    private const string PaymentSk = "PAYMENT";
    private const string ProviderEventSk = "PAYEVT";

    private static string PaymentPk(Guid id) => $"PAYMENT#{id:N}";
    private static string ProviderEventPk(string providerEventId) => $"PAYEVT#{providerEventId.Trim()}";

    public PaymentRecord Record(PaymentWebhookRequest request, DateTimeOffset processedAtUtc)
    {
        var existing = GetByProviderEventId(request.ProviderEventId!);
        if (existing is not null)
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

        var idemPk = ProviderEventPk(record.ProviderEventId);

        try
        {
            dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    [Pk] = new AttributeValue(idemPk),
                    [Sk] = new AttributeValue(ProviderEventSk),
                    ["PaymentId"] = new AttributeValue(record.Id.ToString("N"))
                },
                ConditionExpression = "attribute_not_exists(PK)"
            }).GetAwaiter().GetResult();
        }
        catch (ConditionalCheckFailedException)
        {
            var raced = GetByProviderEventId(record.ProviderEventId);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }

        dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(record),
            ConditionExpression = "attribute_not_exists(PK)"
        }).GetAwaiter().GetResult();

        return record;
    }

    public PaymentRecord? GetByProviderEventId(string providerEventId)
    {
        var idx = dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [Pk] = new AttributeValue(ProviderEventPk(providerEventId)),
                [Sk] = new AttributeValue(ProviderEventSk)
            }
        }).GetAwaiter().GetResult();

        if (!idx.IsItemSet || !idx.Item.TryGetValue("PaymentId", out var paymentIdAttr) || string.IsNullOrWhiteSpace(paymentIdAttr.S))
        {
            return null;
        }

        var paymentId = Guid.ParseExact(paymentIdAttr.S, "N");

        var payment = dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [Pk] = new AttributeValue(PaymentPk(paymentId)),
                [Sk] = new AttributeValue(PaymentSk)
            }
        }).GetAwaiter().GetResult();

        return payment.IsItemSet ? FromItem(payment.Item) : null;
    }

    public IReadOnlyCollection<PaymentRecord> GetForTicket(Guid ticketId)
    {
        var result = dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = tableName,
            FilterExpression = "TicketId = :tid AND SK = :sk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":tid"] = new AttributeValue(ticketId.ToString("N")),
                [":sk"] = new AttributeValue(PaymentSk)
            }
        }).GetAwaiter().GetResult();

        return result.Items.Select(FromItem).ToArray();
    }

    private static Dictionary<string, AttributeValue> ToItem(PaymentRecord record)
    {
        return new Dictionary<string, AttributeValue>
        {
            [Pk] = new AttributeValue(PaymentPk(record.Id)),
            [Sk] = new AttributeValue(PaymentSk),
            ["Id"] = new AttributeValue(record.Id.ToString("N")),
            ["TicketId"] = new AttributeValue(record.TicketId.ToString("N")),
            ["EventId"] = new AttributeValue(record.EventId.ToString("N")),
            ["Provider"] = new AttributeValue(record.Provider),
            ["ProviderEventId"] = new AttributeValue(record.ProviderEventId),
            ["AmountMinorUnits"] = new AttributeValue { N = record.AmountMinorUnits.ToString() },
            ["Currency"] = new AttributeValue(record.Currency),
            ["Status"] = new AttributeValue(record.Status.ToString()),
            ["OccurredAtUtc"] = new AttributeValue(record.OccurredAtUtc.ToString("O")),
            ["ProcessedAtUtc"] = new AttributeValue(record.ProcessedAtUtc.ToString("O")),
            ["CorrelationId"] = new AttributeValue(record.CorrelationId)
        };
    }

    private static PaymentRecord FromItem(Dictionary<string, AttributeValue> item)
    {
        return new PaymentRecord
        {
            Id = Guid.ParseExact(item["Id"].S, "N"),
            TicketId = Guid.ParseExact(item["TicketId"].S, "N"),
            EventId = Guid.ParseExact(item["EventId"].S, "N"),
            Provider = item["Provider"].S,
            ProviderEventId = item["ProviderEventId"].S,
            AmountMinorUnits = long.Parse(item["AmountMinorUnits"].N),
            Currency = item["Currency"].S,
            Status = Enum.Parse<PaymentStatus>(item["Status"].S, ignoreCase: false),
            OccurredAtUtc = DateTimeOffset.Parse(item["OccurredAtUtc"].S),
            ProcessedAtUtc = DateTimeOffset.Parse(item["ProcessedAtUtc"].S),
            CorrelationId = item["CorrelationId"].S
        };
    }
}

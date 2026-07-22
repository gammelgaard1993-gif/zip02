using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Zip02.Tests.Infrastructure;

/// <summary>
/// Provisions the single-table DynamoDB schema used across all integration tests.
/// Schema mirrors the production ADR-001 design:
///   PK  = TICKET#&lt;ticketId&gt;   (string, hash key)
///   SK  = TICKET               (string, range key, reserved for future item types)
/// </summary>
public static class DynamoDbTableProvisioner
{
    public static async Task CreateTableAsync(IAmazonDynamoDB client, string tableName)
    {
        try
        {
            await client.DescribeTableAsync(tableName);
            return; // Already exists
        }
        catch (ResourceNotFoundException)
        {
            // Expected - fall through to create
        }

        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement { AttributeName = "PK", KeyType = KeyType.HASH },
                new KeySchemaElement { AttributeName = "SK", KeyType = KeyType.RANGE }
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition { AttributeName = "PK", AttributeType = ScalarAttributeType.S },
                new AttributeDefinition { AttributeName = "SK", AttributeType = ScalarAttributeType.S }
            ]
        });

        // Wait until ACTIVE
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var desc = await client.DescribeTableAsync(tableName);
            if (desc.Table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"DynamoDB table '{tableName}' did not become ACTIVE within 30 seconds.");
    }
}

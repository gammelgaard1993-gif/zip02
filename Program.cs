using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using zip02.Services.Events;
using zip02.Services.Events.DynamoDB;
using zip02.Services.Notifications.InMemory;
using zip02.Services.Payments.DynamoDB;
using zip02.Services.Payments.InMemory;
using zip02.Services.Refunds.Infrastructure;
using zip02.Services.Ticketing.DynamoDB;
using zip02.Services.Ticketing.InMemory;

var builder = WebApplication.CreateBuilder(args);

// ── Persistence: Ticketing ────────────────────────────────────────────────────
// Behaviour is driven by DynamoDB:TableName in configuration:
//   · Empty (default) → InMemoryTicketStore.  No AWS credentials required.
//                        Suitable for local development and all unit/E2E tests.
//   · Set             → DynamoDbTicketStore backed by the named table.
//                        Uses the standard AWS SDK credential chain (IAM role,
//                        instance profile, environment variables).
//                        Override the endpoint for LocalStack via AWS:ServiceURL.

var dynamoTableName = builder.Configuration["DynamoDB:TableName"];

if (!string.IsNullOrWhiteSpace(dynamoTableName))
{
    // AWSOptions reads AWS:Region and AWS:ServiceURL from IConfiguration.
    // AmazonDynamoDBClient is thread-safe — register as Singleton to reuse the
    // underlying HTTP connection pool across requests.
    var awsOptions = builder.Configuration.GetAWSOptions();
    builder.Services.AddSingleton<IAmazonDynamoDB>(_ => awsOptions.CreateServiceClient<IAmazonDynamoDB>());

    // DynamoDB-backed stores for persistent event, ticket, and payment data.
    builder.Services.AddSingleton<IEventStore>(sp =>
        new DynamoDbEventStore(sp.GetRequiredService<IAmazonDynamoDB>(), dynamoTableName));
    builder.Services.AddSingleton<ITicketStore>(sp =>
        new DynamoDbTicketStore(sp.GetRequiredService<IAmazonDynamoDB>(), dynamoTableName));
    builder.Services.AddSingleton<IPaymentStore>(sp =>
        new DynamoDbPaymentStore(sp.GetRequiredService<IAmazonDynamoDB>(), dynamoTableName));
}
else
{
    // In-memory stores: zero external dependencies, data lives only for the
    // lifetime of the process.
    builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
    builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();
    builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
}

// ── Persistence: Notifications, Refunds ──────────────────────────────────────
// These services remain in-memory for now and will move to DynamoDB later.
builder.Services.AddSingleton<INotificationStore, InMemoryNotificationStore>();
builder.Services.AddSingleton<IRefundProcessor, InMemoryRefundProcessor>();

// ── API layer ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// AddAWSLambdaHosting is a no-op when the app runs outside Lambda (local dev,
// unit tests) so it is safe to call unconditionally. Inside Lambda it swaps
// the Kestrel server for the Lambda ASP.NET Core server adapter, translating
// HTTP API Gateway v2 proxy events into HttpContext objects.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    // Expose the OpenAPI document only in development — not in production.
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;


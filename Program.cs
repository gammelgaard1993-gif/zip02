using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// ── Security: Organizer authentication + authorization (Cognito JWT) ─────────
// WHY:
//   Organizer routes mutate business-critical state (event configuration,
//   refunds, and operational reconciliation). These routes must be protected by
//   strong identity, not by anonymous/public access.
//
// HOW:
//   1) If Cognito settings are configured, enable JWT bearer validation.
//      - validates token issuer/audience/lifetime/signature via Cognito JWKS.
//   2) Enforce OrganizerWrite policy that requires authenticated users in one
//      of the approved Cognito groups (default: organizer-admin/organizer-operator).
//
// SAFE LOCAL DEV:
//   If Cognito settings are absent, OrganizerWrite becomes pass-through so
//   existing local tests and dev workflows continue to run unchanged.
var cognitoRegion = builder.Configuration["Security:Cognito:Region"];
var cognitoUserPoolId = builder.Configuration["Security:Cognito:UserPoolId"];
var cognitoClientId = builder.Configuration["Security:Cognito:ClientId"];
var organizerGroupsRaw = builder.Configuration["Security:Cognito:OrganizerGroups"]
    ?? "organizer-admin,organizer-operator";
var organizerGroups = organizerGroupsRaw
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

// SECURITY: Shared secret used only for machine-to-machine EventBridge triggers
// (expiry + reconciliation). This allows scheduled operational workflows to run
// without a human JWT while still preventing anonymous callers from invoking
// privileged endpoints by spoofing only the source header.
var internalSchedulerToken = builder.Configuration["Security:InternalSchedulerToken"];

var cognitoAuthEnabled =
    !string.IsNullOrWhiteSpace(cognitoRegion) &&
    !string.IsNullOrWhiteSpace(cognitoUserPoolId) &&
    !string.IsNullOrWhiteSpace(cognitoClientId);

if (cognitoAuthEnabled)
{
    var authority = $"https://cognito-idp.{cognitoRegion}.amazonaws.com/{cognitoUserPoolId}";

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = cognitoClientId;
            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudience = cognitoClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = "cognito:username"
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizerWrite", policy =>
    {
        if (!cognitoAuthEnabled)
        {
            // Local/dev fallback: no Cognito settings configured, so do not block
            // existing integration tests and local workflows.
            policy.RequireAssertion(_ => true);
            return;
        }

        policy.RequireAssertion(ctx =>
        {
            // SECURITY: Allow trusted machine invocations from EventBridge only
            // when BOTH the invocation-source marker and a secret token match.
            // This protects scheduled admin workflows (expiry/reconcile) without
            // weakening human organizer RBAC.
            if (IsTrustedSchedulerCall(ctx, internalSchedulerToken))
            {
                return true;
            }

            if (!(ctx.User?.Identity?.IsAuthenticated ?? false))
            {
                return false;
            }

            var groups = ctx.User.FindAll("cognito:groups").Select(c => c.Value);
            return groups.Any(group => organizerGroups.Contains(group));
        });
    });
});

static bool IsTrustedSchedulerCall(Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext ctx, string? expectedToken)
{
    if (string.IsNullOrWhiteSpace(expectedToken))
    {
        return false;
    }

    var httpContext = ctx.Resource switch
    {
        HttpContext direct => direct,
        Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext mvc => mvc.HttpContext,
        _ => null
    };

    if (httpContext is null)
    {
        return false;
    }

    var sourceHeader = httpContext.Request.Headers["x-invocation-source"].ToString();
    var tokenHeader = httpContext.Request.Headers["x-internal-auth"].ToString();

    return string.Equals(sourceHeader, "eventbridge-schedule", StringComparison.Ordinal) &&
           string.Equals(tokenHeader, expectedToken, StringComparison.Ordinal);
}

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

if (cognitoAuthEnabled)
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;


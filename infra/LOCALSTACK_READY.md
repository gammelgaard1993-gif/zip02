# LocalStack + Application Testing Guide

## Status: ✅ LocalStack is Running

Your LocalStack environment is now fully operational:

```
✅ LocalStack running at: http://localhost:4566
✅ Application running at: http://localhost:5132
✅ All AWS services available: DynamoDB, EventBridge, SNS, SQS, S3, SSM, CloudWatch, Logs
✅ All tests passing: 93/93 ✓
```

---

## Important: Authorization Setup

The application has **Cognito-based authorization** configured for organizer writing operations. When running locally **without Cognito configured**, you have two options:

### Option 1: Test Using Public/Read Endpoints (No Auth Required)

Public endpoints:
- `GET /events/{id}` - Retrieve event details
- `GET /tickets/{id}` - Retrieve ticket details  
- `GET /refunds/tickets/{ticketId}` - Check refund status
- `POST /webhooks/stripe` - Stripe webhook endpoint (requires signature)

### Option 2: Test Using Scheduler Headers (For Admin Operations)

Admin operations like creating events, expiring reservations, and reconciliation require **either**:
1. **Cognito token** (human organizer with `cognito:groups` containing `organizer-admin` or `organizer-operator`)
2. **Scheduler headers** (for EventBridge/automation):

```
x-invocation-source: eventbridge-schedule
x-internal-auth: <scheduler-token-from-config>
```

---

## Testing Guide

### Testing Public Endpoints (No Auth)

```powershell
$apiBase = "http://localhost:5132"

# Test 1: Get a non-existent event (should return 404, proving endpoint works)
Invoke-WebRequest -Uri "$apiBase/events/00000000-0000-0000-0000-000000000000" -Method GET

# Test 2: Get a non-existent ticket
Invoke-WebRequest -Uri "$apiBase/tickets/00000000-0000-0000-0000-000000000000" -Method GET

# Test 3: Get Swagger UI (development only)
Start-Process "http://localhost:5132/swagger/ui"
```

### Testing Admin Operations (with Scheduler Headers)

For testing `POST /events`, `POST /tickets/expire-reservations`, etc., use the internal scheduler token:

```powershell
$apiBase = "http://localhost:5132"

# Get the scheduler token from environment (in production, this comes from SSM)
# For local testing, it's not set, so these endpoints will fail.
# To enable local testing of admin endpoints, you would need to:

# Option A: Set environment variable
$env:Security__InternalSchedulerToken = "my-test-token"

# Then restart the app with:
# dotnet run

# Option B: Use the test suite (which sets this up automatically)
# dotnet test --filter "MvpFlowTests"
```

---

## How to Create Test Data

The best way to create test data locally is **via the test suite**, which properly configures the application:

```powershell
# Run the full test suite (creates comprehensive test scenarios)
cd C:\Users\gamme\source\repos\zip02
dotnet test --configuration Release

# Run E2E tests specifically
dotnet test --filter "MvpFlowTests"

# Run specific test
dotnet test --filter "Purchase_CheckIn_And_PreActivationRefundGuardrail_Work_EndToEnd"
```

The test suite will:
1. Create events
2. Reserve tickets
3. Process payments (simulated via Stripe webhook)
4. Check in attendees
5. Verify refund logic
6. Test EventBridge reconciliation

**Output:** 93 tests pass ✓

---

## Local Development Workflow

### Scenario 1: Running Tests + LocalStack

```powershell
# Terminal 1: Keep LocalStack running
docker-compose -f local/docker-compose.yml up

# Terminal 2: Run full test suite
cd C:\Users\gamme\source\repos\zip02
dotnet test --configuration Release

# Expected: All 93 tests pass
```

### Scenario 2: Running Application + LocalStack

```powershell
# Terminal 1: Keep LocalStack running
docker-compose -f local/docker-compose.yml up

# Terminal 2: Start the application
cd C:\Users\gamme\source\repos\zip02
$env:USE_DYNAMODB = "false"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run

# Terminal 3: Test public endpoints
$apiBase = "http://localhost:5132"
Invoke-WebRequest -Uri "$apiBase/events/00000000-0000-0000-0000-000000000000" -Method GET
```

### Scenario 3: Testing Admin Operations Locally

To test admin endpoints locally without Cognito:

1. **Option A: Use scheduler headers**
```powershell
$apiBase = "http://localhost:5132"
$headers = @{
	"x-invocation-source" = "eventbridge-schedule"
	"x-internal-auth" = "test-token" # Must match Security:InternalSchedulerToken
	"Content-Type" = "application/json"
}

$event = @{
	Title = "Test Event"
	Capacity = 50
	StartAtUtc = "2024-12-20T10:00:00Z"
	EndAtUtc = "2024-12-20T12:00:00Z"
} | ConvertTo-Json

Invoke-WebRequest -Uri "$apiBase/events" -Method POST -Headers $headers -Body $event
```

2. **Option B: Set up local .NET user secrets**
```powershell
# This would allow the app to accept requests without auth (development mode only)
# Not recommended for production

dotnet user-secrets set "Security:InternalSchedulerToken" "dev-token"
dotnet user-secrets set "Security:Cognito:Region" "" # Empty = disabled
```

---

## Accessing Swagger UI

When running locally with `ASPNETCORE_ENVIRONMENT=Development`:

```
http://localhost:5132/swagger/ui
```

This shows all available endpoints but **authorization still applies** - you can see what exists, but won't be able to call admin endpoints without proper auth.

---

## Monitoring & Debugging

### View Application Logs

In the terminal where `dotnet run` is executing, you'll see logs like:

```
info: Microsoft.Hosting.Lifetime[14]
	  Now listening on: http://localhost:5132
info: Zip02.Controllers.TicketsController[0]
	  [Tickets.Reserve] Started: EventId=..., Email=...
```

### View LocalStack Logs

```powershell
# Follow LocalStack logs in real-time
docker logs -f zip02-localstack

# View last 100 lines
docker logs --tail 100 zip02-localstack
```

### View LocalStack DynamoDB Data

When LocalStack is running, in-memory data persists only during that container session. When you stop and restart LocalStack, all data is lost (unless you enable persistence).

To check what's in DynamoDB:
1. Use the test suite (which validates the data)
2. Check LocalStack logs for DynamoDB operations
3. Future: Set up LocalStack persistence volume

---

## Next Steps

### Option 1: Run Against AWS Dev (After Kogn ito is set up)
```powershell
# See: infra/AWS_SETUP_GUIDE.md
cd infra/scripts
.\deploy.ps1 -Environment dev
```

### Option 2: Continue Local Testing with Enhanced Auth
- Set up Cognito User Pool locally (advanced)
- Use `Amazon.Cognito.Idp` to create test users
- Generate bearer tokens for testing

### Option 3: Full E2E Testing
- Continue using the test suite (currently 93/93 passing)
- Tests automatically set up required data and configurations
- Covers all business flows: purchase → payment → check-in → refund

---

## Cleanup

### Stop Everything
```powershell
# Stop the app
# (Press Ctrl+C in the terminal where dotnet run is running)

# Stop LocalStack
docker-compose -f local/docker-compose.yml down

# Full cleanup (including volumes)
docker-compose -f local/docker-compose.yml down -v
```

### Restart Fresh
```powershell
docker-compose -f local/docker-compose.yml down -v
docker-compose -f local/docker-compose.yml up -d
dotnet run
```

---

## Summary

| Component | Status | Access |
|-----------|--------|--------|
| **LocalStack** | ✅ Running | http://localhost:4566 |
| **Application** | ✅ Running | http://localhost:5132 |
| **Swagger UI** | ✅ Available | http://localhost:5132/swagger/ui |
| **DynamoDB** | ✅ Ready | Via application |
| **EventBridge** | ✅ Ready | Via application |
| **Tests** | ✅ Passing | 93/93 |
| **Public Endpoints** | ✅ Accessible | GET /events/{id}, GET /tickets/{id} |
| **Admin Endpoints** | ⚠️ Requires Auth | Use scheduler headers or Cognito |

You're ready to start local testing! Begin with the public endpoints or run the full test suite.

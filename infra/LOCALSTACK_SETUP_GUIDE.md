# LocalStack Testing Setup Guide

This guide walks you through setting up and running the `zip02` application with LocalStack for local AWS testing.

---

## Prerequisites

- **Docker Desktop** installed and running (Windows, Mac, or Linux)
- **PowerShell** (for running deploy scripts)
- **.NET 10 SDK** (for building the app)
- **Git** (for cloning the repo)

### Check Prerequisites

```powershell
# Check Docker
docker --version
docker ps

# Check .NET
dotnet --version

# Check PowerShell
$PSVersionTable.PSVersion
```

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Your Local Machine                        │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Docker Container (LocalStack)                      │    │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐          │    │
│  │  │ DynamoDB │  │ EventBridge  │SNS/SQS │ SSM   │    │    │
│  │  └──────────┘  └──────────┘  └──────────┘          │    │
│  │  ↑                                                   │    │
│  │  Port 4566 (LocalStack Edge Service)                │    │
│  └──────────────────────────────────────────────────────┘    │
│           ↑                                                    │
│           │ HTTP requests (http://localhost:4566)            │
│           │                                                    │
│  ┌────────────────────────────────┐                          │
│  │  .NET 10 Application            │                          │
│  │  ┌──────────────────────────┐  │                          │
│  │  │ ASP.NET Core Web API     │  │                          │
│  │  │ (Running locally or in   │  │                          │
│  │  │  xUnit test harness)     │  │                          │
│  │  └──────────────────────────┘  │                          │
│  └────────────────────────────────┘                          │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

---

## Step 1: Start LocalStack

Navigate to the repository root and start LocalStack:

```powershell
cd C:\Users\gamme\source\repos\zip02

# Start LocalStack container with docker-compose
docker-compose -f local/docker-compose.yml up -d
```

**Expected output:**
```
Creating zip02-localstack ... done
```

### Verify LocalStack is Running

```powershell
# Check container status
docker ps | Select-String "localstack"

# Should output something like:
# zip02-localstack   localstack/localstack:3.8   Up 2 minutes   0.0.0.0:4566->4566/tcp
```

### Check LocalStack Health

```powershell
# Health check endpoint
curl -s http://localhost:4566/_localstack/health | ConvertFrom-Json | Format-Table

# Should show services: dynamodb, events, sns, sqs, s3, ssm, cloudwatch, logs
```

---

## Step 2: Verify AWS Credentials for LocalStack

LocalStack accepts any AWS credentials (dummy values work fine):

```powershell
# Set local AWS credentials (dummy values - LocalStack accepts anything)
$env:AWS_ACCESS_KEY_ID = "test"
$env:AWS_SECRET_ACCESS_KEY = "test"
$env:AWS_DEFAULT_REGION = "eu-west-1"
$env:LOCALSTACK_ENDPOINT = "http://localhost:4566"

# Verify connection
aws dynamodb list-tables --endpoint-url http://localhost:4566 --region eu-west-1
```

**Expected output:**
```
{
	"TableNames": []
}
```

---

## Step 3: Create LocalStack Resources Manually (Optional)

You can pre-create DynamoDB tables and SSM parameters, or let the deploy script create them:

```powershell
# Create DynamoDB table for local testing
aws dynamodb create-table `
  --table-name Tickets-LocalStack `
  --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S `
  --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE `
  --billing-mode PAY_PER_REQUEST `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1

# Create SSM parameter for internal scheduler token
aws ssm put-parameter `
  --name "/zip02/localstack/internal-scheduler-token" `
  --value "localstack-test-token-12345" `
  --type "SecureString" `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1

# Create SSM parameter for Stripe webhook secret
aws ssm put-parameter `
  --name "/zip02/localstack/stripe-webhook-secret" `
  --value "whsec_test_12345" `
  --type "SecureString" `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1
```

---

## Step 4: Run LocalStack Integration Tests

The test suite includes LocalStack integration tests via `Testcontainers.LocalStack`:

```powershell
# Run all LocalStack-based tests
cd C:\Users\gamme\source\repos\zip02

dotnet test --filter "LocalStack" --configuration Release --verbosity normal
```

**Test suites that use LocalStack:**
- `tests/Zip02.Tests/Payments/Integration/LocalStackHarnessSmokeTests.cs`
- `tests/Zip02.Tests/EndToEnd/MvpFlowTests.cs` (when configured with LocalStack)

### Run a Specific Test

```powershell
# Run smoke tests
dotnet test --filter "FullyQualifiedName~LocalStackHarnessSmokeTests" --verbosity normal

# Run MVP flow tests
dotnet test --filter "FullyQualifiedName~MvpFlowTests" --verbosity normal
```

---

## Step 5: Run the Application Against LocalStack

### Option A: Run Locally (Development)

```powershell
cd C:\Users\gamme\source\repos\zip02

# Set LocalStack environment
$env:AWS_ACCESS_KEY_ID = "test"
$env:AWS_SECRET_ACCESS_KEY = "test"
$env:AWS_DEFAULT_REGION = "eu-west-1"
$env:LOCALSTACK_ENDPOINT = "http://localhost:4566"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Run the application
dotnet run --project src/zip02.csproj
```

**Expected output:**
```
info: Microsoft.Hosting.Lifetime[14]
	  Now listening on: http://localhost:5000
	  Now listening on: https://localhost:5001
```

### Option B: Deploy via SAM to LocalStack

```powershell
# Update dev parameters to use LocalStack endpoint
# Edit infra/sam/parameters/localstack.json (or create it):

$content = @"
[
  {
	"ParameterKey": "Env",
	"ParameterValue": "localstack"
  },
  {
	"ParameterKey": "TableName",
	"ParameterValue": "Tickets-LocalStack"
  },
  {
	"ParameterKey": "StripeWebhookSecretSsmPath",
	"ParameterValue": "/zip02/localstack/stripe-webhook-secret"
  },
  {
	"ParameterKey": "InternalSchedulerTokenSsmPath",
	"ParameterValue": "/zip02/localstack/internal-scheduler-token"
  },
  {
	"ParameterKey": "CognitoClientId",
	"ParameterValue": ""
  },
  {
	"ParameterKey": "CognitoUserPoolId",
	"ParameterValue": ""
  },
  {
	"ParameterKey": "CognitoRegion",
	"ParameterValue": "eu-west-1"
  }
]
"@

$content | Out-File -FilePath "infra/sam/parameters/localstack.json" -Encoding UTF8

# Build SAM template
cd infra/scripts
sam build --use-container --cached

# Deploy to LocalStack
sam deploy `
  --stack-name zip02-stack-localstack `
  --parameter-overrides '(Get-Content ../sam/parameters/localstack.json | ConvertFrom-Json | ForEach-Object { "$($_.ParameterKey)=$($_.ParameterValue)" })' `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1 `
  --capabilities CAPABILITY_IAM
```

---

## Step 6: Test the Application

### Using curl

```powershell
# If running locally (Option A)
$apiBase = "http://localhost:5000"

# If deployed via SAM (Option B)
$apiBase = "http://localhost:4566" # Need to look up the API GW endpoint

# Create an event
$event = @{
	Title = "LocalStack Test Event"
	Capacity = 50
	StartAtUtc = (Get-Date).AddDays(1).ToUniversalTime().ToString("o")
	EndAtUtc = (Get-Date).AddDays(1).AddHours(2).ToUniversalTime().ToString("o")
} | ConvertTo-Json

curl -X POST "$apiBase/events" `
  -H "Content-Type: application/json" `
  -d $event

# Get events
curl -X GET "$apiBase/events"

# Reserve a ticket
$reservation = @{
	EventId = "<event-id-from-above>"
	Email = "test@example.com"
	Name = "Test User"
} | ConvertTo-Json

curl -X POST "$apiBase/tickets/reserve" `
  -H "Content-Type: application/json" `
  -d $reservation
```

### Using Swagger/OpenAPI

If running locally with `ASPNETCORE_ENVIRONMENT=Development`:

```
http://localhost:5000/swagger/ui
```

---

## Step 7: Monitor LocalStack Activity

### View LocalStack Logs

```powershell
# Follow logs in real-time
docker logs -f zip02-localstack

# View last 100 lines
docker logs --tail 100 zip02-localstack
```

### Monitor DynamoDB

```powershell
# List tables
aws dynamodb list-tables --endpoint-url http://localhost:4566 --region eu-west-1

# Scan a table
aws dynamodb scan `
  --table-name Tickets-LocalStack `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1

# Get item count
aws dynamodb describe-table `
  --table-name Tickets-LocalStack `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1 `
  --query "Table.[TableName, ItemCount, TableStatus]"
```

### Monitor EventBridge Rules

```powershell
# List rules
aws events list-rules `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1

# List targets for a rule
aws events list-targets-by-rule `
  --rule "zip02-reservation-expiry-schedule" `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1
```

### View SSM Parameters

```powershell
# List all parameters
aws ssm describe-parameters `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1

# Get a parameter value
aws ssm get-parameter `
  --name "/zip02/localstack/internal-scheduler-token" `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1
```

---

## Step 8: Cleanup

### Stop LocalStack

```powershell
# Stop the container
docker-compose -f local/docker-compose.yml down

# Remove volumes (to start fresh next time)
docker-compose -f local/docker-compose.yml down -v

# Or just stop without removing
docker stop zip02-localstack
```

### Remove SAM Stack (if deployed)

```powershell
cd infra/scripts

sam delete `
  --stack-name zip02-stack-localstack `
  --endpoint-url http://localhost:4566 `
  --region eu-west-1
```

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| **Connection refused on localhost:4566** | LocalStack not running | Run `docker-compose -f local/docker-compose.yml up -d` |
| **DynamoDB table not found** | Table not created | Run `aws dynamodb create-table ...` or let SAM create it |
| **SSM parameter not found** | Parameter not created | Run `aws ssm put-parameter ...` |
| **Lambda fails to start** | Missing environment variable or permissions | Check Lambda logs in CloudWatch or `docker logs zip02-localstack` |
| **EventBridge rule not triggering** | Rule disabled or target misconfigured | Verify rule status: `aws events describe-rule ...` |
| **Out of memory in Docker** | Docker memory allocation too low | Increase Docker Desktop memory limit (Settings > Resources) |
| **Permission denied** | AWS credentials not set | Set `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` env vars |

---

## Quick Reference Commands

```powershell
# Start LocalStack
docker-compose -f local/docker-compose.yml up -d

# Check health
curl -s http://localhost:4566/_localstack/health | ConvertFrom-Json

# Run tests
dotnet test --filter "LocalStack"

# Run app locally
$env:LOCALSTACK_ENDPOINT = "http://localhost:4566"; dotnet run

# View logs
docker logs -f zip02-localstack

# Stop
docker-compose -f local/docker-compose.yml down

# Clean (start fresh)
docker-compose -f local/docker-compose.yml down -v
```

---

## Next Steps

1. **Start LocalStack**: `docker-compose -f local/docker-compose.yml up -d`
2. **Run tests**: `dotnet test --filter "LocalStack"`
3. **Run app locally**: `$env:LOCALSTACK_ENDPOINT = "http://localhost:4566"; dotnet run`
4. **Test endpoints**: Create events, reserve tickets, check payments
5. **Monitor**: Watch logs and verify DynamoDB data
6. **Deploy via SAM**: Try deploying to LocalStack with SAM (advanced)

---

**Ready to start? Begin with Step 1: Start LocalStack!**

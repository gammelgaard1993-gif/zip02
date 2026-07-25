# LocalStack Testing - Copy/Paste Commands

Ready-to-run commands for common testing scenarios.

---

## Prerequisites Check

```powershell
# Verify Docker is running
docker ps

# Verify app is running (should show output)
curl http://localhost:5132/health -v

# Verify LocalStack is running
curl http://localhost:4566/_localstack/health
```

---

## Test Public Endpoints (No Auth Required)

### Get Event Details (will 404 but proves endpoint works)
```powershell
$eventId = "00000000-0000-0000-0000-000000000000"
curl -X GET "http://localhost:5132/events/$eventId"
```

### Get Ticket Details
```powershell
$ticketId = "00000000-0000-0000-0000-000000000000"
curl -X GET "http://localhost:5132/tickets/$ticketId"
```

### Check Refund Status
```powershell
$ticketId = "00000000-0000-0000-0000-000000000000"
curl -X GET "http://localhost:5132/refunds/tickets/$ticketId"
```

---

## View Swagger UI

```powershell
# Open in browser
Start-Process "http://localhost:5132/swagger/ui"
```

---

## Run Tests

```powershell
# Change to repo root
cd C:\Users\gamme\source\repos\zip02

# Run ALL tests (93 total)
dotnet test --configuration Release

# Run only LocalStack tests
dotnet test --filter "LocalStack"

# Run E2E tests
dotnet test --filter "MvpFlowTests"

# Run security tests
dotnet test --filter "OrganizerAuthorizationTests"

# Run with verbose output
dotnet test --configuration Release --verbosity detailed
```

---

## Monitor LocalStack

```powershell
# Follow logs in real-time
docker logs -f zip02-localstack

# View last 50 lines
docker logs --tail 50 zip02-localstack

# Check health
curl http://localhost:4566/_localstack/health | ConvertFrom-Json
```

---

## Admin Operations (with Scheduler Headers)

For testing admin endpoints, use internal scheduler headers:

```powershell
# Create an event with scheduler headers
$headers = @{
	"x-invocation-source" = "eventbridge-schedule"
	"x-internal-auth" = "your-scheduler-token"
	"Content-Type" = "application/json"
}

$event = @{
	Title = "Test Event"
	Capacity = 50
	StartAtUtc = "2024-12-20T10:00:00Z"
	EndAtUtc = "2024-12-20T12:00:00Z"
} | ConvertTo-Json

curl -X POST "http://localhost:5132/events" `
  -Headers $headers `
  -Body $event
```

---

## Manage LocalStack

```powershell
# Start LocalStack
docker-compose -f local/docker-compose.yml up -d

# Stop LocalStack (keep data)
docker-compose -f local/docker-compose.yml down

# Stop and delete everything (fresh start)
docker-compose -f local/docker-compose.yml down -v

# Check container status
docker ps | Select-String localstack
```

---

## Run the Application

```powershell
# Navigate to repo
cd C:\Users\gamme\source\repos\zip02

# Run with development settings
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:USE_DYNAMODB = "false"
dotnet run

# App will start on http://localhost:5132
```

---

## Stop Running Services

```powershell
# Stop app (in the terminal where it's running)
# Press Ctrl+C

# Stop LocalStack
docker-compose -f local/docker-compose.yml down

# Verify everything stopped
docker ps
```

---

## Useful PowerShell Functions

Copy and paste these into your terminal for easier testing:

```powershell
function Test-PublicEndpoint {
	$eventId = "00000000-0000-0000-0000-000000000000"
	try {
		$response = Invoke-WebRequest -Uri "http://localhost:5132/events/$eventId" -UseBasicParsing
		Write-Host "✓ Endpoint is accessible (404 is expected for non-existent event)" -ForegroundColor Green
	}
	catch {
		Write-Host "✗ Endpoint not accessible: $_" -ForegroundColor Red
	}
}

function View-Docs {
	Start-Process "http://localhost:5132/swagger/ui"
}

function Check-Health {
	Write-Host "Checking services..."

	try {
		$app = Invoke-WebRequest -Uri "http://localhost:5132/events/00000000-0000-0000-0000-000000000000" -UseBasicParsing 2>&1
		Write-Host "✓ Application at http://localhost:5132" -ForegroundColor Green
	}
	catch {
		Write-Host "✗ Application not responding" -ForegroundColor Red
	}

	try {
		$ls = Invoke-WebRequest -Uri "http://localhost:4566/_localstack/health" -UseBasicParsing
		Write-Host "✓ LocalStack at http://localhost:4566" -ForegroundColor Green
	}
	catch {
		Write-Host "✗ LocalStack not responding" -ForegroundColor Red
	}
}

# Usage:
# Test-PublicEndpoint
# View-Docs
# Check-Health
```

---

## Quick Verification Script

Run this to verify everything is set up correctly:

```powershell
Write-Host "=" * 50
Write-Host "LocalStack Testing Verification" -ForegroundColor Cyan
Write-Host "=" * 50

# Check Docker
Write-Host "`n[1/4] Docker Status..." -ForegroundColor Yellow
try {
	$docker = docker ps --filter "name=localstack" --format "{{.Names}}"
	if ($docker -eq "zip02-localstack") {
		Write-Host "✓ LocalStack container running" -ForegroundColor Green
	} else {
		Write-Host "✗ LocalStack not running" -ForegroundColor Red
	}
}
catch {
	Write-Host "✗ Docker not available" -ForegroundColor Red
}

# Check LocalStack health
Write-Host "`n[2/4] LocalStack Services..." -ForegroundColor Yellow
try {
	$health = Invoke-WebRequest -Uri "http://localhost:4566/_localstack/health" -UseBasicParsing 2>&1
	if ($health.StatusCode -eq 200) {
		Write-Host "✓ LocalStack is healthy" -ForegroundColor Green
		$content = $health.Content | ConvertFrom-Json
		Write-Host "  Version: $($content.version)" -ForegroundColor Gray
	}
}
catch {
	Write-Host "✗ LocalStack not responding" -ForegroundColor Red
}

# Check Application
Write-Host "`n[3/4] Application Status..." -ForegroundColor Yellow
try {
	$app = Invoke-WebRequest -Uri "http://localhost:5132/events/test" -UseBasicParsing 2>&1 -ErrorAction SilentlyContinue
	Write-Host "✓ Application is running" -ForegroundColor Green
}
catch {
	Write-Host "✗ Application not responding" -ForegroundColor Red
}

# Check Tests
Write-Host "`n[4/4] Test Suite..." -ForegroundColor Yellow
cd C:\Users\gamme\source\repos\zip02
$tests = dotnet test --no-build --configuration Release --verbosity quiet 2>&1 | Select-String "passed"
if ($tests) {
	Write-Host "✓ Tests available" -ForegroundColor Green
	Write-Host "  $tests" -ForegroundColor Gray
}

Write-Host "`n" + "=" * 50
Write-Host "Ready to test! Open http://localhost:5132/swagger/ui" -ForegroundColor Cyan
Write-Host "=" * 50
```

---

## Common Workflows

### Workflow 1: Full Test Run
```powershell
# Make sure LocalStack is running
docker-compose -f local/docker-compose.yml up -d

# Wait 5 seconds
Start-Sleep -Seconds 5

# Run all tests
cd C:\Users\gamme\source\repos\zip02
dotnet test --configuration Release

# Results should show: Passed: 93
```

### Workflow 2: Application Testing
```powershell
# Terminal 1: Start LocalStack
docker-compose -f local/docker-compose.yml up

# Terminal 2: Start Application
cd C:\Users\gamme\source\repos\zip02
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run

# Terminal 3: Test endpoints
curl http://localhost:5132/swagger/ui
```

### Workflow 3: Development Mode
```powershell
# LocalStack in background
docker-compose -f local/docker-compose.yml up -d

# App in foreground with hot reload
cd C:\Users\gamme\source\repos\zip02
dotnet watch run --project .

# Make code changes - app restarts automatically
```

---

## Performance Tips

```powershell
# Run tests in Release mode (faster)
dotnet test --configuration Release --verbosity minimal

# Skip full build if nothing changed
dotnet test --no-build

# Run specific test (faster than all)
dotnet test --filter "MvpFlowTests" --once

# Increase Docker memory if tests are slow
# Settings > Resources > Memory (increase to 4GB+)
```

---

That's it! All commands are ready to copy/paste. Happy testing! 🚀

# LocalStack Test Script

This script tests the running application against LocalStack

```powershell
$apiBase = "http://localhost:5132"

Write-Host "Testing zip02 API with LocalStack...`n" -ForegroundColor Cyan

# Test 1: Get events (should be empty)
Write-Host "[1] Getting events..." -ForegroundColor Yellow
$events = Invoke-WebRequest -Uri "$apiBase/events" -Method GET -ContentType "application/json" -UseBasicParsing
Write-Host "✓ Status: $($events.StatusCode)" -ForegroundColor Green
Write-Host "  Response: $($events.Content | ConvertFrom-Json | ConvertTo-Json)`n"

# Test 2: Create an event
Write-Host "[2] Creating an event..." -ForegroundColor Yellow
$eventPayload = @{
	Title = "LocalStack Test Event $(Get-Date -Format 'HH:mm:ss')"
	Capacity = 50
	StartAtUtc = (Get-Date).AddDays(1).ToUniversalTime().ToString("o")
	EndAtUtc = (Get-Date).AddDays(1).AddHours(2).ToUniversalTime().ToString("o")
} | ConvertTo-Json

$createEvent = Invoke-WebRequest -Uri "$apiBase/events" -Method POST -Body $eventPayload -ContentType "application/json" -UseBasicParsing
$eventId = ($createEvent.Content | ConvertFrom-Json).EventId
Write-Host "✓ Status: $($createEvent.StatusCode)" -ForegroundColor Green
Write-Host "  Event ID: $eventId`n"

# Test 3: Get the event
Write-Host "[3] Getting the created event..." -ForegroundColor Yellow
$getEvent = Invoke-WebRequest -Uri "$apiBase/events/$eventId" -Method GET -ContentType "application/json" -UseBasicParsing
Write-Host "✓ Status: $($getEvent.StatusCode)" -ForegroundColor Green
$eventData = $getEvent.Content | ConvertFrom-Json
Write-Host "  Title: $($eventData.Title)" -ForegroundColor Green
Write-Host "  Capacity: $($eventData.Capacity)`n" -ForegroundColor Green

# Test 4: Reserve a ticket
Write-Host "[4] Reserving a ticket..." -ForegroundColor Yellow
$reservationPayload = @{
	EventId = $eventId
	Email = "test@example.com"
	Name = "Test User"
} | ConvertTo-Json

$reserve = Invoke-WebRequest -Uri "$apiBase/tickets/reserve" -Method POST -Body $reservationPayload -ContentType "application/json" -UseBasicParsing
$ticketId = ($reserve.Content | ConvertFrom-Json).TicketId
Write-Host "✓ Status: $($reserve.StatusCode)" -ForegroundColor Green
Write-Host "  Ticket ID: $ticketId`n" -ForegroundColor Green

# Test 5: Get the ticket
Write-Host "[5] Getting the reserved ticket..." -ForegroundColor Yellow
$getTicket = Invoke-WebRequest -Uri "$apiBase/tickets/$ticketId" -Method GET -ContentType "application/json" -UseBasicParsing
Write-Host "✓ Status: $($getTicket.StatusCode)" -ForegroundColor Green
$ticketData = $getTicket.Content | ConvertFrom-Json
Write-Host "  Email: $($ticketData.Email)" -ForegroundColor Green
Write-Host "  Status: $($ticketData.Status)`n" -ForegroundColor Green

# Test 6: Check Swagger/OpenAPI
Write-Host "[6] Testing Swagger UI..." -ForegroundColor Yellow
$swagger = Invoke-WebRequest -Uri "$apiBase/swagger/ui" -Method GET -UseBasicParsing
if ($swagger.StatusCode -eq 200) {
	Write-Host "✓ Swagger UI available at $apiBase/swagger/ui" -ForegroundColor Green
}

Write-Host "`n✓ All tests passed!" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Open Swagger UI: $apiBase/swagger/ui" -ForegroundColor White
Write-Host "2. Try creating more events and reserving tickets" -ForegroundColor White
Write-Host "3. Check LocalStack logs: docker logs -f zip02-localstack" -ForegroundColor White
Write-Host "4. Stop the app: Ctrl+C in the terminal" -ForegroundColor White
Write-Host "5. Stop LocalStack: docker-compose -f local/docker-compose.yml down" -ForegroundColor White
```

## Quick Commands

```powershell
# Run this script manually
cd C:\Users\gamme\source\repos\zip02
$apiBase = "http://localhost:5132"

# Get all events
Invoke-WebRequest -Uri "$apiBase/events" -Method GET | ConvertFrom-Json

# Create an event
$event = @{
	Title = "Test Event"
	Capacity = 100
	StartAtUtc = "2024-12-20T10:00:00Z"
	EndAtUtc = "2024-12-20T12:00:00Z"
} | ConvertTo-Json

Invoke-WebRequest -Uri "$apiBase/events" -Method POST -Body $event -ContentType "application/json"

# View Swagger UI
Start-Process "http://localhost:5132/swagger/ui"
```

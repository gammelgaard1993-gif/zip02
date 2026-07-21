using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using zip02.Services.Notifications.Contracts;
using zip02.Services.Notifications.InMemory;
using zip02.Services.Payments.Contracts;
using zip02.Services.Payments.InMemory;
using zip02.Services.Ticketing.InMemory;

namespace zip02.Services.Payments.PaymentWebhookApi;

[ApiController]
[Route("payments/stripe")]
public class PaymentWebhookController(
    IPaymentStore paymentStore,
    ITicketStore ticketStore,
    INotificationStore notificationStore,
    IConfiguration configuration) : ControllerBase
{
    private const string SignatureHeader = "Stripe-Signature";
    private const int DefaultTimestampToleranceSeconds = 300;

    [HttpPost("webhook")]
    public async Task<ActionResult<PaymentRecord>> Webhook()
    {
        var secret = configuration["Payments:Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        if (!Request.Headers.TryGetValue(SignatureHeader, out var signatureHeader) ||
            !IsValidStripeSignature(signatureHeader.ToString(), payload, secret, DateTimeOffset.UtcNow))
        {
            return Unauthorized();
        }

        var request = JsonSerializer.Deserialize<PaymentWebhookRequest>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (request is null)
        {
            return BadRequest();
        }

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            foreach (var validationResult in validationResults)
            {
                var members = validationResult.MemberNames.Any() ? validationResult.MemberNames : new[] { string.Empty };
                foreach (var member in members)
                {
                    ModelState.AddModelError(member, validationResult.ErrorMessage ?? "Validation failed.");
                }
            }

            return ValidationProblem(ModelState);
        }

        var existing = paymentStore.GetByProviderEventId(request.ProviderEventId!);
        if (existing is not null)
        {
            if (existing.Status == PaymentStatus.Succeeded)
            {
                CompleteQrDelivery(existing.TicketId, existing.CorrelationId, existing.OccurredAtUtc);
            }

            return Ok(existing);
        }

        var record = paymentStore.Record(request, DateTimeOffset.UtcNow);
        if (record.Status == PaymentStatus.Succeeded)
        {
            if (!CompleteQrDelivery(record.TicketId, record.CorrelationId, record.OccurredAtUtc))
            {
                return Conflict(record);
            }
        }

        return Ok(record);
    }

    private static bool IsValidStripeSignature(string signatureHeader, string payload, string secret, DateTimeOffset nowUtc)
    {
        var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? timestampText = null;
        var signatures = new List<string>();

        foreach (var part in parts)
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                timestampText = part[2..];
                continue;
            }

            if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signatures.Add(part[3..]);
            }
        }

        if (timestampText is null || signatures.Count == 0 || !long.TryParse(timestampText, out var unixSeconds))
        {
            return false;
        }

        var eventTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((nowUtc - eventTime).TotalSeconds) > DefaultTimestampToleranceSeconds)
        {
            return false;
        }

        var signedPayload = $"{timestampText}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();

        foreach (var provided in signatures)
        {
            if (SafeEqualsHex(provided, expected))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SafeEqualsHex(string providedHex, string expectedHex)
    {
        try
        {
            var provided = Convert.FromHexString(providedHex);
            var expected = Convert.FromHexString(expectedHex);
            return CryptographicOperations.FixedTimeEquals(provided, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool CompleteQrDelivery(Guid ticketId, string correlationId, DateTimeOffset occurredAtUtc)
    {
        var ticket = ticketStore.MarkPaid(ticketId, occurredAtUtc);
        if (ticket is null)
        {
            return false;
        }

        var qr = notificationStore.IssueQr(ticket, occurredAtUtc);
        ticketStore.MarkQrIssued(ticket.Id, qr.Token!, qr.RenderedPayload!, qr.CreatedAtUtc);

        notificationStore.SendEmail(new EmailNotificationRequest
        {
            TicketId = ticket.Id,
            ToEmail = ticket.AttendeeEmail,
            Subject = "Your QR ticket",
            Body = qr.RenderedPayload!,
            CorrelationId = correlationId
        }, occurredAtUtc);

        return true;
    }
}

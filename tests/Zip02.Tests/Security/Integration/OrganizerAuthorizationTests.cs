using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using zip02.Services.Events;
using zip02.Services.Ticketing.Contracts;

namespace Zip02.Tests.Security.Integration;

public sealed class OrganizerAuthorizationTests
{
    private const string SchedulerToken = "test-scheduler-token";

    [Fact]
    public async Task ExpireReservations_WhenCognitoEnabled_AndNoCredentials_ReturnsAuthFailure()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets/expire-reservations", new ExpireReservationsRequest());

        Assert.True(IsAuthFailure(response.StatusCode));
    }

    [Fact]
    public async Task ExpireReservations_WhenSchedulerHeadersAreValid_ReturnsOk()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tickets/expire-reservations")
        {
            Content = JsonContent.Create(new ExpireReservationsRequest())
        };
        request.Headers.Add("x-invocation-source", "eventbridge-schedule");
        request.Headers.Add("x-internal-auth", SchedulerToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpireReservations_WhenSchedulerSourceMissing_ReturnsAuthFailure()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tickets/expire-reservations")
        {
            Content = JsonContent.Create(new ExpireReservationsRequest())
        };
        request.Headers.Add("x-internal-auth", SchedulerToken);

        var response = await client.SendAsync(request);

        Assert.True(IsAuthFailure(response.StatusCode));
    }

    [Fact]
    public async Task ExpireReservations_WhenSchedulerTokenInvalid_ReturnsAuthFailure()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tickets/expire-reservations")
        {
            Content = JsonContent.Create(new ExpireReservationsRequest())
        };
        request.Headers.Add("x-invocation-source", "eventbridge-schedule");
        request.Headers.Add("x-internal-auth", "wrong-token");

        var response = await client.SendAsync(request);

        Assert.True(IsAuthFailure(response.StatusCode));
    }

    [Fact]
    public async Task ReconcileEndedEvents_WhenCognitoEnabled_AndNoCredentials_ReturnsAuthFailure()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/refunds/reconcile-ended-events", content: null);

        Assert.True(IsAuthFailure(response.StatusCode));
    }

    [Fact]
    public async Task ReconcileEndedEvents_WhenSchedulerHeadersAreValid_ReturnsOk()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/refunds/reconcile-ended-events");
        request.Headers.Add("x-invocation-source", "eventbridge-schedule");
        request.Headers.Add("x-internal-auth", SchedulerToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReconcileEndedEvents_WhenSchedulerTokenInvalid_ReturnsAuthFailure()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/refunds/reconcile-ended-events");
        request.Headers.Add("x-invocation-source", "eventbridge-schedule");
        request.Headers.Add("x-internal-auth", "wrong-token");

        var response = await client.SendAsync(request);

        Assert.True(IsAuthFailure(response.StatusCode));
    }

    [Fact]
    public async Task GetEventById_WhenCognitoEnabled_StillAllowsPublicReadRoute()
    {
        using var factory = new SecurityEnabledApiFactory();
        using var client = factory.CreateClient();

        // GET /events/{id} is intentionally public; unknown ids should return 404,
        // not 401/403, proving auth is scoped to organizer write endpoints.
        var response = await client.GetAsync($"/events/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static bool IsAuthFailure(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private sealed class SecurityEnabledApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // SECURITY TEST WIRING:
            // UseSetting guarantees these values are visible in Program.cs during
            // host construction so Cognito auth is actually enabled in test host.
            builder.UseSetting("Payments:Stripe:WebhookSecret", "security-test-webhook-secret");
            builder.UseSetting("Security:Cognito:Region", "eu-west-1");
            builder.UseSetting("Security:Cognito:UserPoolId", "eu-west-1_TESTPOOL");
            builder.UseSetting("Security:Cognito:ClientId", "test-client-id");
            builder.UseSetting("Security:Cognito:OrganizerGroups", "organizer-admin,organizer-operator");
            builder.UseSetting("Security:InternalSchedulerToken", SchedulerToken);
        }
    }
}

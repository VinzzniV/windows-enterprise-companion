using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Infrastructure.Microsoft365;

namespace Wec.Infrastructure.IntegrationTests.Microsoft365;

public sealed class GraphReaderTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task PagesThroughSdkAndPreservesNullFields()
    {
        var fake = new FakeHandler((_, index, _) => Task.FromResult(Json(index == 1
            ? """{"@odata.count":2,"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=opaque","value":[{"id":"one","displayName":"One","accountEnabled":false,"assignedLicenses":[]}]}"""
            : """{"value":[{"id":"two","displayName":null}]}""")));
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), fake));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        Microsoft365Data result = await MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter, new(Microsoft365Resource.Users), new(), CancellationToken.None);
        Assert.Equal(2, result.Users.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.False(result.Truncated);
        Assert.False(result.Users[0].AccountEnabled);
        Assert.Empty(result.Users[0].AssignedLicenses!);
        Assert.Null(result.Users[1].AssignedLicenses);
        Assert.Null(result.Users[1].AccountEnabled);
        Assert.Null(result.Users[1].DisplayName);
        Assert.Equal(2, fake.Count);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(100, 1)]
    public async Task PageAndItemLimitsAreExplicit(int maximumItems, int maximumPages)
    {
        using var http = new HttpClient(new FakeHandler((_, _, _) => Task.FromResult(Json(
            """{"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=next","value":[{"id":"one"},{"id":"two"}]}"""))));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var result = await MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter, new(Microsoft365Resource.Users),
            new() { MaximumItems = maximumItems, MaximumPages = maximumPages }, CancellationToken.None);
        Assert.True(result.Truncated);
        Assert.InRange(result.Users.Count, 1, maximumItems);
    }

    [Theory]
    [InlineData("https://attacker.example/v1.0/users")]
    [InlineData("http://graph.microsoft.com/v1.0/users")]
    [InlineData("https://graph.microsoft.com/beta/users")]
    [InlineData("https://graph.microsoft.com/v1.0/groups")]
    public async Task UnsafeContinuationNeverMakesASecondRequest(string next)
    {
        var fake = new FakeHandler((_, _, _) => Task.FromResult(Json($$"""{"@odata.nextLink":"{{next}}","value":[]} """)));
        using var http = new HttpClient(fake);
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        await Assert.ThrowsAsync<InvalidDataException>(() => MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.Users), new(), CancellationToken.None));
        Assert.Equal(1, fake.Count);
    }

    [Fact]
    public async Task RepeatedContinuationIsRejected()
    {
        var fake = new FakeHandler((request, _, _) => Task.FromResult(Json($$"""{"@odata.nextLink":"{{request.RequestUri}}","value":[]} """)));
        using var http = new HttpClient(fake);
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        await Assert.ThrowsAsync<InvalidDataException>(() => MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.Users), new(), CancellationToken.None));
        Assert.Equal(1, fake.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":null}")]
    public async Task MissingCollectionIsNotAnEmptySuccessfulInventory(string json)
    {
        using var http = new HttpClient(new FakeHandler((_, _, _) => Task.FromResult(Json(json))));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        await Assert.ThrowsAsync<InvalidDataException>(() => MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.Users), new(), CancellationToken.None));
    }

    [Theory]
    [InlineData(401, ErrorCode.Microsoft365AuthenticationRequired)]
    [InlineData(403, ErrorCode.Microsoft365AccessDenied)]
    [InlineData(404, ErrorCode.Microsoft365NotFound)]
    [InlineData(429, ErrorCode.Microsoft365Throttled)]
    [InlineData(503, ErrorCode.Microsoft365Unavailable)]
    [InlineData(400, ErrorCode.Microsoft365Unsupported)]
    public async Task SdkErrorsBecomeDistinctSanitizedFailures(int status, ErrorCode expected)
    {
        using var http = new HttpClient(new FakeHandler((_, _, _) => Task.FromResult(Json(
            """{"error":{"code":"fixture","message":"PRIVATE_RESPONSE_CONTENT"}}""", status))));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        ApiException error = await Assert.ThrowsAnyAsync<ApiException>(() => MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.Users), new(), CancellationToken.None));
        Error mapped = Microsoft365Errors.From(error);
        Assert.Equal(expected, mapped.Code);
        Assert.DoesNotContain("PRIVATE_RESPONSE_CONTENT", mapped.Message, StringComparison.Ordinal);
        Assert.Null(mapped.Details);
    }

    [Theory]
    [InlineData("POST", "https://graph.microsoft.com/v1.0/users")]
    [InlineData("DELETE", "https://graph.microsoft.com/v1.0/users")]
    [InlineData("GET", "https://graph.microsoft.com.evil.example/v1.0/users")]
    [InlineData("GET", "https://graph.microsoft.com:444/v1.0/users")]
    [InlineData("GET", "https://user@graph.microsoft.com/v1.0/users")]
    public async Task ReadOnlyTransportRejectsMutationAndUntrustedDestinations(string method, string url)
    {
        var fake = new FakeHandler((_, _, _) => Task.FromResult(Json("{}")));
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), fake));
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        await Assert.ThrowsAsync<InvalidDataException>(() => http.SendAsync(request));
        Assert.Equal(0, fake.Count);
    }

    [Fact]
    public async Task RetryAfterIsHonoredAndRetriesAreBounded()
    {
        var fake = new FakeHandler((_, _, _) =>
        {
            var response = Json("{}", 429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var http = new HttpClient(new GraphReadOnlyHandler(new() { MaximumRetries = 2 }, fake));
        using var result = await http.GetAsync("https://graph.microsoft.com/v1.0/users");
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal(3, fake.Count);
    }

    [Fact]
    public async Task LongRetryAfterIsSurfacedWithoutEarlyRetry()
    {
        var fake = new FakeHandler((_, _, _) =>
        {
            var response = Json("{}", 429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), fake));
        using var result = await http.GetAsync("https://graph.microsoft.com/v1.0/users");
        Assert.Equal(1, fake.Count);
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        using var http = new HttpClient(new GraphReadOnlyHandler(new() { MaximumResponseBytes = 10 },
            new FakeHandler((_, _, _) => Task.FromResult(Json(new string('x', 100))))));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://graph.microsoft.com/v1.0/users"));
    }

    [Fact]
    public async Task CancellationReachesTheHttpTransport()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), new FakeHandler(async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        })));
        var read = http.GetAsync("https://graph.microsoft.com/v1.0/users", cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(ErrorCode.Microsoft365Timeout, Microsoft365Errors.From(new TaskCanceledException()).Code);
    }

    [Fact]
    public void LeastPrivilegeProfileNeverIncludesWritesOrBroadDirectoryReads()
    {
        var baseline = new Microsoft365Configuration(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        string[] standard = Microsoft365Scopes.For(baseline);
        Assert.DoesNotContain("Organization.Read.All", standard);
        Assert.DoesNotContain("Directory.Read.All", standard);
        Assert.DoesNotContain("AuditLog.Read.All", standard);
        Assert.DoesNotContain("DeviceManagementManagedDevices.Read.All", standard);
        Assert.All(Microsoft365Scopes.For(baseline with { EnableIntune = true, EnableAuthenticationReports = true }),
            scope => Assert.Matches("^[A-Za-z]+\\.Read(\\.All)?$", scope));
    }

    [Fact]
    public void AuthenticationFailuresNeverExposeRawMsalMessages()
    {
        var exception = new MsalUiRequiredException("interaction_required", "PRIVATE_TOKEN_CONTENT");
        Assert.Equal(ErrorCode.Microsoft365AuthenticationRequired, Microsoft365Errors.From(exception).Code);
        Assert.DoesNotContain("PRIVATE_TOKEN_CONTENT", Microsoft365Errors.From(exception).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TokensWithUnexpectedPrivilegesAreRejected()
    {
        MsalGraphSession.ValidateScopes(["User.Read", "openid", "https://graph.microsoft.com/User.Read.All"], ["User.Read", "User.Read.All"]);
        var failure = Assert.Throws<MsalClientException>(() => MsalGraphSession.ValidateScopes(["User.ReadWrite.All"], ["User.Read.All"]));
        Assert.Equal(ErrorCode.Microsoft365ConfigurationInvalid, Microsoft365Errors.From(failure).Code);
    }

    [Fact]
    public void MappingsPreserveServicePlanStateAndDistinctDeviceIds()
    {
        var device = GraphMapping.ManagedDevice(new ManagedDevice { Id = "intune-id", AzureADDeviceId = "entra-device-id", ComplianceState = ComplianceState.Unknown });
        Assert.Equal("intune-id", device.Id);
        Assert.Equal("entra-device-id", device.EntraDeviceId);
        Assert.Equal("Unknown", device.ComplianceState);
        var license = GraphMapping.License(new LicenseDetails { ServicePlans = [new ServicePlanInfo { ServicePlanName = "dynamic-name", ProvisioningStatus = "Disabled" }] });
        Assert.Equal("Disabled", Assert.Single(license.ServicePlans!).Status);
        Assert.Null(license.EnabledSeats);
    }

    [Theory]
    [InlineData(Microsoft365Resource.User)]
    [InlineData(Microsoft365Resource.Group)]
    [InlineData(Microsoft365Resource.Device)]
    [InlineData(Microsoft365Resource.ManagedDevice)]
    [InlineData(Microsoft365Resource.UserRegistration)]
    [InlineData(Microsoft365Resource.UserActivity)]
    public async Task SingleObjectEndpointsDeserializeThroughSdk(Microsoft365Resource resource)
    {
        using var http = new HttpClient(new FakeHandler((_, _, _) => Task.FromResult(Json($$"""{"id":"{{UserId}}"} """))));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        Assert.NotNull(await MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter, new(resource, UserId), new(), CancellationToken.None));
    }

    [Fact]
    public async Task KnownIntuneIdReadsOnlyTheSelectedObjectWithExistingFieldAndPermissionLimits()
    {
        var fake = new FakeHandler((request, _, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/v1.0/deviceManagement/managedDevices/{UserId}", request.RequestUri!.AbsolutePath);
            Assert.Contains("azureADDeviceId", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("activationLockBypassCode", request.RequestUri.Query, StringComparison.Ordinal);
            return Task.FromResult(Json($$"""{"id":"{{UserId}}","deviceName":"PC","userId":null,"azureADDeviceId":"22222222-2222-2222-2222-222222222222"}"""));
        });
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), fake));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var data = await MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.ManagedDevice, UserId), new(), CancellationToken.None);
        var device = Assert.Single(data.ManagedDevices);
        Assert.Equal(UserId, device.Id);
        Assert.Null(device.UserId);
        Assert.Equal(1, fake.Count);
        Assert.Equal(["DeviceManagementManagedDevices.Read.All"], Microsoft365Scopes.For(Microsoft365Resource.ManagedDevice));
    }

    private static HttpResponseMessage Json(string json, int status = 200) => new((HttpStatusCode)status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SidLookupUsesExistingReadScopeAndStopsAfterDuplicateEvidence()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        var fake = new FakeHandler((request, _, _) =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains($"onPremisesSecurityIdentifier eq '{sid}'", query, StringComparison.Ordinal);
            Assert.Contains("$top=2", query, StringComparison.Ordinal);
            return Task.FromResult(Json($$"""{"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=next","value":[{"id":"one","onPremisesSecurityIdentifier":"{{sid}}"},{"id":"two","onPremisesSecurityIdentifier":"{{sid}}"}]}"""));
        });
        using var http = new HttpClient(new GraphReadOnlyHandler(new(), fake));
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var data = await MicrosoftGraphReader.ReadDataAsync(graph.RequestAdapter,
            new(Microsoft365Resource.UsersBySid, SecurityIdentifier: sid), new(), CancellationToken.None);
        Assert.Equal(2, data.Users.Count);
        Assert.True(data.Truncated);
        Assert.Equal(1, fake.Count);
        Assert.Equal(["User.Read.All"], Microsoft365Scopes.For(Microsoft365Resource.UsersBySid));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, ++Count, cancellationToken);
    }
}

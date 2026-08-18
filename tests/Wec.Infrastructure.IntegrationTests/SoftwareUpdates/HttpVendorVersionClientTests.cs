using System.Net;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Infrastructure.SoftwareUpdates;

namespace Wec.Infrastructure.IntegrationTests.SoftwareUpdates;

public sealed class HttpVendorVersionClientTests
{
    [Fact]
    public async Task MatchingHttpsSource_ReturnsFirstCaptureGroup()
    {
        using var client = new HttpVendorVersionClient(new StaticHandler(
            "<span>Latest version: 129.0.2</span>"));

        Result<string> result = await client.GetLatestVersionAsync(new VendorVersionRequest(
            new Uri("https://vendor.example/releases"),
            "Latest version: ([0-9.]+)",
            TimeSpan.FromSeconds(2)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("129.0.2", result.Value);
    }

    [Fact]
    public async Task HttpSource_IsRejectedWithoutSendingRequest()
    {
        var handler = new StaticHandler("Version: 1.0");
        using var client = new HttpVendorVersionClient(handler);

        Result<string> result = await client.GetLatestVersionAsync(new VendorVersionRequest(
            new Uri("http://vendor.example/releases"),
            "Version: ([0-9.]+)",
            TimeSpan.FromSeconds(2)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class StaticHandler(string response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
            });
        }
    }
}

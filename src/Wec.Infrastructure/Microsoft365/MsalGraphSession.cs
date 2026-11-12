using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Wec.Core.Microsoft365;

namespace Wec.Infrastructure.Microsoft365;

internal sealed class MsalGraphSession : IAuthenticationProvider
{
    private readonly IPublicClientApplication _application;
    private readonly string[] _scopes;
    private IAccount? _account;
    private string[] _granted = [];

    internal MsalGraphSession(Microsoft365Configuration configuration, IMicrosoft365AuthenticationWindow window)
    {
        _scopes = Microsoft365Scopes.For(configuration);
        _application = PublicClientApplicationBuilder.Create(configuration.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{configuration.TenantId}")
            .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{configuration.ClientId}")
            .WithParentActivityOrWindow(() => window.Handle)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Windows Enterprise Companion" })
            .Build();
    }

    internal IReadOnlyList<Microsoft365ScopeGrant> Permissions =>
        [.. _scopes.Select(scope => new Microsoft365ScopeGrant(scope, _granted.Contains(scope, StringComparer.OrdinalIgnoreCase)))];
    internal string? Account => _account?.Username;

    internal async Task SignInAsync(string tenantId, CancellationToken cancellationToken)
    {
        // Embedded fallback is deliberately unavailable: no Desktop package is
        // referenced, so a missing broker cannot open a loopback listener.
        AuthenticationResult result = await _application.AcquireTokenInteractive(_scopes)
            .WithUseEmbeddedWebView(true)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(result.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new MsalClientException("invalid_tenant", "The account belongs to another tenant.");
        }
        ValidateScopes(result.Scopes, _scopes);
        _account = result.Account;
        _granted = [.. result.Scopes.Select(NormalizeScope)];
    }

    public async Task AuthenticateRequestAsync(RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!GraphReadOnlyHandler.IsAllowed(request.URI) || request.HttpMethod != Method.GET)
        {
            throw new InvalidDataException("Graph authentication rejected the destination.");
        }
        AuthenticationResult result = await _application.AcquireTokenSilent(_scopes, _account)
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
        ValidateScopes(result.Scopes, _scopes);
        _granted = [.. result.Scopes.Select(NormalizeScope)];
        request.Headers.Add("Authorization", $"Bearer {result.AccessToken}");
    }

    private static string NormalizeScope(string scope) => scope.StartsWith("https://graph.microsoft.com/", StringComparison.OrdinalIgnoreCase)
        ? scope["https://graph.microsoft.com/".Length..] : scope;

    internal static void ValidateScopes(IEnumerable<string> granted, IEnumerable<string> requested)
    {
        var allowed = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase) { "openid", "profile", "offline_access", "email" };
        if (granted.Select(NormalizeScope).Any(scope => !allowed.Contains(scope)))
        {
            throw new MsalClientException("wec_scope_mismatch", "The token exceeds the selected read profile.");
        }
    }
}

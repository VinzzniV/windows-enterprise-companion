using System.Text.RegularExpressions;
using Wec.Core.Results;

namespace Wec.Core.Ccrx;

/// <summary>
/// Kyocera Command Center RX admin login. Configuration/UI-supplied, passed per
/// request, never logged and never persisted (ADR 0007). The fleet ships on the
/// factory default Admin/Admin.
/// </summary>
public sealed record CcrxCredentials(string UserName, string Password)
{
    public static CcrxCredentials Default { get; } = new("Admin", "Admin");

    // The generated record ToString would print the password; keep it out of
    // every log/exception path.
    public override string ToString() => $"CcrxCredentials {{ UserName = {UserName} }}";
}

/// <summary>Parsed CCRX settings model: its <c>_pp.name = 'value'</c> assignments.</summary>
public sealed record CcrxModel(string Path, IReadOnlyDictionary<string, string> Properties)
{
    public string? Get(string name) => Properties.TryGetValue(name, out string? value) ? value : null;
}

public sealed record CcrxReadResult(IReadOnlyList<CcrxModel> Models)
{
    public CcrxModel? Model(string path) =>
        Models.FirstOrDefault(model => string.Equals(model.Path, path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Read-only access to a Command Center RX device over HTTPS: establishes a
/// session, logs in, and returns the requested settings models. Implemented in
/// Infrastructure. No write operation — WEC never changes a printer's config.
/// </summary>
public interface ICcrxClient
{
    Task<Result<CcrxReadResult>> ReadModelsAsync(
        string host,
        CcrxCredentials credentials,
        IReadOnlyList<string> modelPaths,
        CancellationToken cancellationToken);
}

/// <summary>
/// Extracts the <c>_pp.&lt;name&gt; = '&lt;value&gt;'</c> assignments a CCRX
/// <c>*.model.htm</c> file carries. Pure and reusable so both the client and
/// tests share one parser.
/// </summary>
public static partial class CcrxModelParser
{
    public static IReadOnlyDictionary<string, string> Parse(string body)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in PropertyPattern().Matches(body))
        {
            // Last assignment wins, mirroring how the browser evaluates the script.
            properties[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        return properties;
    }

    [GeneratedRegex(@"_pp\.(?<name>[A-Za-z0-9_]+)\s*=\s*'(?<value>[^']*)'")]
    private static partial Regex PropertyPattern();
}

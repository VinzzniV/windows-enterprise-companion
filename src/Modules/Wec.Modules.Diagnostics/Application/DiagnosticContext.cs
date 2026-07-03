using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application;

/// <summary>Which machine a diagnostic run inspects, and with which identity.</summary>
public sealed record DiagnosticContext(
    ScanTarget Target,
    ScanCredentials Credentials,
    ConnectionOptions Connection)
{
    public static readonly DiagnosticContext Local =
        new(ScanTarget.Local, ScanCredentials.CurrentUser, ConnectionOptions.Default);
}

internal static class DiagnosticContextWmiExtensions
{
    public static Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        this IWmiQueryService wmiQueryService,
        DiagnosticContext context,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        wmiQueryService.QueryAsync(
            context.Target, context.Credentials, context.Connection, wmiNamespace, wqlQuery, cancellationToken);
}

internal static class DiagnosticResults
{
    /// <summary>
    /// Uniform result for checks that measure the WEC machine's own
    /// perspective (network probes, local APIs): running them "for" a remote
    /// target would report the wrong machine, so they are visibly skipped
    /// (ADR 0007: never a silent local fallback).
    /// </summary>
    public static DiagnosticResult LocalPerspective(
        string diagnosticId,
        string checkName,
        DiagnosticCategory category,
        string affectedResource,
        string targetDisplayName,
        DateTimeOffset capturedAtUtc) => new(
        diagnosticId,
        $"{checkName} runs on the WEC machine only",
        DiagnosticStatus.NotRun,
        category,
        affectedResource,
        new Dictionary<string, string>
        {
            ["errorCode"] = ErrorCode.UnsupportedRemoteOperation.ToString(),
            ["target"] = targetDisplayName,
        },
        ["Run WEC directly on the target machine to include this check."],
        RequiredPrivilege: null,
        capturedAtUtc);
}

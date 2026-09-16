using System.ComponentModel.DataAnnotations;
using Wec.Core.Microsoft365;

namespace Wec.Infrastructure.Microsoft365;

public sealed class Microsoft365Options
{
    public const string SectionName = "Wec:Microsoft365";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public bool EnableIntune { get; set; }
    public bool EnableAuthenticationReports { get; set; }
    [Range(1, 100)] public int PageSize { get; set; } = 100;
    [Range(1, 100)] public int MaximumPages { get; set; } = 20;
    [Range(1, 10000)] public int MaximumItems { get; set; } = 2000;
    [Range(0, 5)] public int MaximumRetries { get; set; } = 2;
    [Range(1, 30)] public int MaximumRetryDelaySeconds { get; set; } = 15;
    [Range(5, 90)] public int QueryTimeoutSeconds { get; set; } = 60;
    [Range(30, 300)] public int AuthenticationTimeoutSeconds { get; set; } = 180;
    [Range(1024, 16777216)] public int MaximumResponseBytes { get; set; } = 4194304;

    public Microsoft365Configuration Configuration => new(TenantId, ClientId,
        EnableIntune, EnableAuthenticationReports);
}

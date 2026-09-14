namespace Wec.Modules.Microsoft365;

public sealed class Microsoft365CacheOptions
{
    public const string SectionName = "Wec:Microsoft365:Cache";
    public TimeSpan FreshFor { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan RetainFor { get; set; } = TimeSpan.FromHours(1);
    public int MaximumEntries { get; set; } = 32;
    public double LicenseWarningRatio { get; set; } = 0.9;
}

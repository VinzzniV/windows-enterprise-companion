using System.ComponentModel.DataAnnotations;

namespace Wec.Host.Options;

public sealed class FrontendOptions
{
    public const string SectionName = "Wec:Frontend";

    public bool UseDevServer { get; set; }

    [Required]
    [Url]
    public string DevServerUrl { get; set; } = string.Empty;
}

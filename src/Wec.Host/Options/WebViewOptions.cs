using System.ComponentModel.DataAnnotations;

namespace Wec.Host.Options;

public sealed class WebViewOptions
{
    public const string SectionName = "Wec:WebView";

    [Required]
    public string UserDataDirectory { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace Wec.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Wec:Database";

    [Required]
    public string DatabasePath { get; set; } = string.Empty;
}

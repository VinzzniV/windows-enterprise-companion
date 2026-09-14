using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Clients;

public sealed class ObjectWorkingSetOptions
{
    public const string SectionName = "Wec:ObjectWorkingSet";
    [Range(1, 50000)]
    public int MaximumRecords { get; set; } = 5000;
}

using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.Clients;

public sealed class ObjectWorkingSetOptions
{
    public const string SectionName = "Wec:ObjectWorkingSet";
    [Range(1, 50000)]
    public int MaximumRecords { get; set; } = 5000;
    [Range(1, 1024)]
    public int MaximumSourceReads { get; set; } = 128;
}

using System.ComponentModel.DataAnnotations;

namespace Wec.Modules.UserManagement;

public sealed class UserManagementOptions
{
    public const string SectionName = "Wec:UserManagement";

    [Range(1, 50)]
    public int MaxLinkedDevices { get; set; } = 8;

    [Range(1, 50)]
    public int SoftwareSampleLimit { get; set; } = 5;
}

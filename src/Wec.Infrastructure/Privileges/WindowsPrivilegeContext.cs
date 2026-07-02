using System.Security.Principal;
using Wec.Core.Privileges;

namespace Wec.Infrastructure.Privileges;

public sealed class WindowsPrivilegeContext : IPrivilegeContext
{
    public WindowsPrivilegeContext()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        IsElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool IsElevated { get; }

    public PrivilegeLevel CurrentLevel => IsElevated ? PrivilegeLevel.Administrator : PrivilegeLevel.StandardUser;

    public bool Satisfies(PrivilegeLevel requiredLevel) => CurrentLevel >= requiredLevel;
}

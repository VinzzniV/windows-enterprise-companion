namespace Wec.Core.Privileges;

public interface IPrivilegeContext
{
    bool IsElevated { get; }

    PrivilegeLevel CurrentLevel { get; }

    bool Satisfies(PrivilegeLevel requiredLevel);
}

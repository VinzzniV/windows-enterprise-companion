using Wec.Core.Microsoft365;

namespace Wec.Host.Runtime;

internal sealed class Microsoft365AuthenticationWindow : IMicrosoft365AuthenticationWindow
{
    public nint Handle { get; set; }
}

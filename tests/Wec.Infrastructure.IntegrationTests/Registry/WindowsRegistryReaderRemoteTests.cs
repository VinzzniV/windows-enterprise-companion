using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Infrastructure.Registry;

namespace Wec.Infrastructure.IntegrationTests.Registry;

/// <summary>
/// Verifies the remote StdRegProv decode path of <see cref="WindowsRegistryReader"/>
/// (type probing DWORD → String → MultiString, ReturnValue gate, uint → int) with a fake
/// WMI service — no remote machine required.
/// </summary>
public sealed class WindowsRegistryReaderRemoteTests
{
    private const uint WbemTypeMismatch = 0x80041005;
    private static readonly ScanTarget Remote = ScanTarget.Remote("pc-1.contoso.local");

    private static WindowsRegistryReader CreateReader(FakeWmiQueryService wmi) =>
        new(wmi, NullLogger<WindowsRegistryReader>.Instance);

    [Fact]
    public async Task RemoteDwordValue_IsDecodedAsInt()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", 0u), ("uValue", 1u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Bar", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, Assert.IsType<int>(result.Value));
    }

    [Fact]
    public async Task RemoteStringValue_IsDecodedAsString()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch)); // not a DWORD
        wmi.OnMethod["GetStringValue"] = MethodResult(("ReturnValue", 0u), ("sValue", "NT5DS"));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.Equal("NT5DS", Assert.IsType<string>(result.Value));
    }

    [Fact]
    public async Task RemoteMultiStringValue_IsDecodedAsStringArray()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch)); // not a DWORD
        wmi.OnMethod["GetStringValue"] = MethodResult(("ReturnValue", WbemTypeMismatch)); // not a string
        wmi.OnMethod["GetMultiStringValue"] = MethodResult(("ReturnValue", 0u), ("sValue", new[] { "a", "b" }));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Bar", CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(result.Value));
    }

    [Fact]
    public async Task RemoteMissingValue_IsDecodedAsNull()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", 1u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Bar", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(["GetDWORDValue"], wmi.InvokedMethods);
    }

    [Fact]
    public async Task RemoteValueAccessDeniedReturnCode_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", 5u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(Wec.Core.Privileges.PrivilegeLevel.Administrator, result.Error.RequiredPrivilege);
        Assert.Equal(["GetDWORDValue"], wmi.InvokedMethods);
    }

    [Fact]
    public async Task RemoteStringProbeAccessDeniedReturnCode_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnMethod["GetStringValue"] = MethodResult(("ReturnValue", 5u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(["GetDWORDValue", "GetStringValue"], wmi.InvokedMethods);
    }

    [Fact]
    public async Task RemoteMultiStringProbeAccessDeniedReturnCode_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnMethod["GetStringValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnMethod["GetMultiStringValue"] = MethodResult(("ReturnValue", 5u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(["GetDWORDValue", "GetStringValue", "GetMultiStringValue"], wmi.InvokedMethods);
    }

    [Fact]
    public async Task RemoteValueMissingReturnCode_IsProviderFailure()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("uValue", 1u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Contains("return code: missing", result.Error.Message);
    }

    [Fact]
    public async Task RemoteValueUnexpectedReturnCode_IsProviderFailure()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", 87u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Contains("return code: 87", result.Error.Message);
    }

    [Fact]
    public async Task RemoteValueSuccessWithoutPayload_IsProviderFailure()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", 0u));

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Contains("success without a value", result.Error.Message);
    }

    [Fact]
    public async Task RemoteStringProbeTransportFailure_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnErrors["GetStringValue"] = Error.WmiUnavailable("transport failed");

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.DoesNotContain("GetMultiStringValue", wmi.InvokedMethods);
    }

    [Fact]
    public async Task RemoteMultiStringProbeTransportFailure_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["GetDWORDValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnMethod["GetStringValue"] = MethodResult(("ReturnValue", WbemTypeMismatch));
        wmi.OnErrors["GetMultiStringValue"] = Error.WmiUnavailable("transport failed");

        Result<object?> result = await CreateReader(wmi).ReadLocalMachineValueAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SYSTEM\Foo", "Type", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task RemoteEnumKey_ReturnsSubKeyNames()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["EnumKey"] = MethodResult(("ReturnValue", 0u), ("sNames", new[] { "RebootPending" }));

        Result<IReadOnlyList<string>> result = await CreateReader(wmi).ReadLocalMachineSubKeyNamesAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SOFTWARE\Foo", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "RebootPending" }, result.Value);
    }

    [Fact]
    public async Task RemoteMissingKey_ReturnsEmptySubKeyList()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["EnumKey"] = MethodResult(("ReturnValue", 2u)); // ERROR_FILE_NOT_FOUND

        Result<IReadOnlyList<string>> result = await CreateReader(wmi).ReadLocalMachineSubKeyNamesAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SOFTWARE\Missing", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task RemoteEnumKeyAccessDeniedReturnCode_IsPropagated()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["EnumKey"] = MethodResult(("ReturnValue", 5u));

        Result<IReadOnlyList<string>> result = await CreateReader(wmi).ReadLocalMachineSubKeyNamesAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SOFTWARE\Foo", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Equal(Wec.Core.Privileges.PrivilegeLevel.Administrator, result.Error.RequiredPrivilege);
    }

    [Fact]
    public async Task RemoteEnumKeyMissingReturnCode_IsProviderFailure()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["EnumKey"] = MethodResult(("sNames", new[] { "RebootPending" }));

        Result<IReadOnlyList<string>> result = await CreateReader(wmi).ReadLocalMachineSubKeyNamesAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SOFTWARE\Foo", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Contains("return code: missing", result.Error.Message);
    }

    [Fact]
    public async Task RemoteEnumKeyUnexpectedReturnCode_IsProviderFailure()
    {
        var wmi = new FakeWmiQueryService();
        wmi.OnMethod["EnumKey"] = MethodResult(("ReturnValue", 1u));

        Result<IReadOnlyList<string>> result = await CreateReader(wmi).ReadLocalMachineSubKeyNamesAsync(
            Remote, ScanCredentials.CurrentUser, ConnectionOptions.Default, @"SOFTWARE\Foo", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
        Assert.Contains("return code: 1", result.Error.Message);
    }

    private static WmiInstance MethodResult(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase));

    private sealed class FakeWmiQueryService : IWmiQueryService
    {
        public Dictionary<string, WmiInstance> OnMethod { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Error> OnErrors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> InvokedMethods { get; } = [];

        public Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
            ScanTarget target, ScanCredentials credentials, ConnectionOptions connection,
            string wmiNamespace, string wqlQuery, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<WmiInstance>>([]));

        public Task<Result<WmiInstance>> InvokeMethodAsync(
            ScanTarget target, ScanCredentials credentials, ConnectionOptions connection,
            string wmiNamespace, string className, string methodName,
            IReadOnlyDictionary<string, object?> inputParameters, CancellationToken cancellationToken)
        {
            InvokedMethods.Add(methodName);
            if (OnErrors.TryGetValue(methodName, out Error? error))
            {
                return Task.FromResult(Result.Failure<WmiInstance>(error));
            }

            return Task.FromResult(OnMethod.TryGetValue(methodName, out WmiInstance? instance)
                ? Result.Success(instance)
                : Result.Failure<WmiInstance>(Error.WmiUnavailable($"no stub for {methodName}")));
        }
    }
}

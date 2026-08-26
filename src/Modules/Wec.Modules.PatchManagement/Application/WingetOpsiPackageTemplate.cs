using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Wec.Modules.PatchManagement.Application;

public sealed record WingetOpsiPackageTemplateInput(
    string ProductId,
    string DisplayName,
    string WingetId,
    string WingetVersion,
    int PackageVersion);

public sealed record GeneratedWingetOpsiPackage(
    string ArchivePath,
    string ProductVersion,
    int PackageVersion);

public static class WingetOpsiPackageTemplate
{
    public const int Version = 1;
    private const string IconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static GeneratedWingetOpsiPackage Generate(WingetOpsiPackageTemplateInput input)
    {
        string root = Directory.CreateTempSubdirectory("wec-winget-package-").FullName;
        string archivePath = Path.Combine(Path.GetTempPath(), $"wec-winget-{Guid.NewGuid():N}.tar.gz");
        try
        {
            string clientData = Directory.CreateDirectory(Path.Combine(root, "CLIENT_DATA")).FullName;
            string opsi = Directory.CreateDirectory(Path.Combine(root, "OPSI")).FullName;
            WriteText(Path.Combine(opsi, "control.toml"), Control(input));
            WriteText(Path.Combine(opsi, "changelog.txt"), Changelog(input));
            WriteText(Path.Combine(clientData, "wec-winget-helper.opsiscript"), Helper());
            WriteText(Path.Combine(clientData, "setup.opsiscript"), Setup(input));
            WriteText(Path.Combine(clientData, "update.opsiscript"), Update(input));
            WriteText(Path.Combine(clientData, "uninstall.opsiscript"), Uninstall(input));
            File.WriteAllBytes(Path.Combine(clientData, "winget.png"), Convert.FromBase64String(IconBase64));

            using FileStream destination = File.Create(archivePath);
            using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize);
            TarFile.CreateFromDirectory(root, gzip, includeBaseDirectory: false);
            return new GeneratedWingetOpsiPackage(
                archivePath,
                input.WingetVersion,
                input.PackageVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Control(WingetOpsiPackageTemplateInput input) => $$"""
        [Package]
        version = "{{input.PackageVersion}}"
        depends = []

        [Product]
        type = "localboot"
        id = "{{Toml(input.ProductId)}}"
        name = "{{Toml(input.DisplayName)}}"
        description = "Installs, updates and uninstalls {{Toml(input.DisplayName)}} through Windows Package Manager. Winget id: {{Toml(input.WingetId)}}."
        advice = "Generated and managed by Windows Enterprise Companion. Do not edit this package manually; rebuild it through WEC."
        version = "{{Toml(input.WingetVersion)}}"
        priority = 0
        licenseRequired = false
        productClasses = []
        setupScript = "setup.opsiscript"
        uninstallScript = "uninstall.opsiscript"
        updateScript = "update.opsiscript"
        alwaysScript = ""
        onceScript = ""
        customScript = ""
        userLoginScript = ""
        windowsSoftwareIds = []
        """;

    private static string Changelog(WingetOpsiPackageTemplateInput input) => $$"""
        {{input.ProductId}} ({{input.WingetVersion}}-{{input.PackageVersion}})

          * Generated from winget package {{input.WingetId}} version {{input.WingetVersion}}.
          * WEC Winget template version {{Version}}.

        -- Windows Enterprise Companion <local>  {{DateTimeOffset.UtcNow:R}}
        """;

    private static string Helper() => """
        encoding=utf8

        DefFunc findWecWingetBinary() : string
            DefStringList $wingetFiles$
            set $result$ = ""
            set $wingetFiles$ = listFiles("%ProgramFiles64Dir%\WindowsApps", "winget.exe", "True")
            if count($wingetFiles$) int> "0"
                for %wingetFile% in $wingetFiles$ do set $result$ = '"%wingetFile%"'
            endif
            if $result$ = ""
                LogError "Windows Package Manager (winget.exe) was not found"
                isFatalError "winget missing"
            endif
        EndFunc
        """;

    private static string Setup(WingetOpsiPackageTemplateInput input) => CommonHeader(input, "Installing") + $$"""
        set $wingetArgs$ = ' install --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --version "{{Opsi(input.WingetVersion)}}" --scope machine --accept-source-agreements --accept-package-agreements --silent --disable-interactivity'
        shellScript_winget winst /showoutput
        set $exitCode$ = getLastExitCode
        sub_checkTargetVersion

        [shellScript_winget]
        $wingetBin$ $wingetArgs$

        [sub_checkTargetVersion]
        set $wingetArgs$ = ' list --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --accept-source-agreements --disable-interactivity'
        set $wingetOutput$ = shellCall($wingetBin$ + $wingetArgs$)
        if ("" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetId)}}")) or ("" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetVersion)}}"))
            LogError "Winget did not report {{Opsi(input.WingetId)}} version {{Opsi(input.WingetVersion)}} after setup; exit code: " + $exitCode$
            isFatalError "Winget setup verification failed"
        endif
        """;

    private static string Update(WingetOpsiPackageTemplateInput input) => CommonHeader(input, "Updating") + $$"""
        set $wingetArgs$ = ' list --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --accept-source-agreements --disable-interactivity'
        set $wingetOutput$ = shellCall($wingetBin$ + $wingetArgs$)
        if "" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetId)}}")
            LogError "{{Opsi(input.WingetId)}} is not installed; request setup instead"
            isFatalError "Winget update target missing"
        endif

        set $wingetArgs$ = ' upgrade --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --version "{{Opsi(input.WingetVersion)}}" --scope machine --accept-source-agreements --accept-package-agreements --silent --disable-interactivity'
        shellScript_winget winst /showoutput
        set $exitCode$ = getLastExitCode
        set $wingetArgs$ = ' list --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --accept-source-agreements --disable-interactivity'
        set $wingetOutput$ = shellCall($wingetBin$ + $wingetArgs$)
        if ("" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetId)}}")) or ("" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetVersion)}}"))
            LogError "Winget did not report {{Opsi(input.WingetId)}} version {{Opsi(input.WingetVersion)}} after update; exit code: " + $exitCode$
            isFatalError "Winget update verification failed"
        endif

        [shellScript_winget]
        $wingetBin$ $wingetArgs$
        """;

    private static string Uninstall(WingetOpsiPackageTemplateInput input) => CommonHeader(input, "Uninstalling") + $$"""
        set $wingetArgs$ = ' uninstall --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --scope machine --accept-source-agreements --silent --disable-interactivity'
        shellScript_winget winst /showoutput
        set $exitCode$ = getLastExitCode
        set $wingetArgs$ = ' list --id "{{Opsi(input.WingetId)}}" --exact --source "winget" --accept-source-agreements --disable-interactivity'
        set $wingetOutput$ = shellCall($wingetBin$ + $wingetArgs$)
        if not("" = getIndexFromListByContaining($wingetOutput$, "{{Opsi(input.WingetId)}}"))
            LogError "Winget still reports {{Opsi(input.WingetId)}} after uninstall; exit code: " + $exitCode$
            isFatalError "Winget uninstall verification failed"
        endif

        [shellScript_winget]
        $wingetBin$ $wingetArgs$
        """;

    private static string CommonHeader(WingetOpsiPackageTemplateInput input, string operation) => $$"""
        encoding=utf8

        [Actions]
        requiredOpsiscriptVersion >= "4.12.5.0"
        importlib "wec-winget-helper.opsiscript"
        DefVar $wingetBin$
        DefVar $wingetArgs$
        DefVar $exitCode$
        DefStringList $wingetOutput$
        Message "{{operation}} {{Opsi(input.DisplayName)}} through Winget ..."
        ShowBitmap "%ScriptPath%\winget.png" "{{Opsi(input.ProductId)}}"
        set $wingetBin$ = findWecWingetBinary()

        """;

    private static string Toml(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string Opsi(string value) => value
        .Replace("\"", "'", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static void WriteText(string path, string content) =>
        File.WriteAllText(path, content.ReplaceLineEndings("\n"), new UTF8Encoding(false));
}

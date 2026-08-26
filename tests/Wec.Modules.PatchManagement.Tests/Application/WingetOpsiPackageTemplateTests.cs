using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class WingetOpsiPackageTemplateTests
{
    [Fact]
    public void GeneratedPackage_HasDistinctPinnedLifecycleScripts()
    {
        GeneratedWingetOpsiPackage generated = WingetOpsiPackageTemplate.Generate(new(
            "7zip", "7-Zip", "7zip.7zip", "26.02", 1));
        try
        {
            Dictionary<string, string> files = ReadTextFiles(generated.ArchivePath);
            string control = files["OPSI/control.toml"];
            string setup = files["CLIENT_DATA/setup.opsiscript"];
            string update = files["CLIENT_DATA/update.opsiscript"];
            string uninstall = files["CLIENT_DATA/uninstall.opsiscript"];

            Assert.Contains("setupScript = \"setup.opsiscript\"", control);
            Assert.Contains("updateScript = \"update.opsiscript\"", control);
            Assert.Contains("uninstallScript = \"uninstall.opsiscript\"", control);
            Assert.Contains("version = \"26.02\"", control);
            Assert.Contains(" install --id \"7zip.7zip\"", setup);
            Assert.Contains("--version \"26.02\" --scope machine", setup);
            Assert.DoesNotContain(" uninstall ", setup);
            Assert.Contains(" upgrade --id \"7zip.7zip\"", update);
            Assert.Contains("--version \"26.02\" --scope machine", update);
            Assert.Contains(" list --id \"7zip.7zip\"", update);
            Assert.Contains(" uninstall --id \"7zip.7zip\"", uninstall);
            Assert.DoesNotContain("--version", uninstall);
        }
        finally
        {
            File.Delete(generated.ArchivePath);
        }
    }

    [Fact]
    public void GeneratedPackage_ContainsOnlyExpectedWorkbenchFiles()
    {
        GeneratedWingetOpsiPackage generated = WingetOpsiPackageTemplate.Generate(new(
            "firefox", "Mozilla Firefox", "Mozilla.Firefox", "145.0.1", 2));
        try
        {
            Dictionary<string, string> files = ReadTextFiles(generated.ArchivePath);
            Assert.Equal(
                [
                    "CLIENT_DATA/setup.opsiscript",
                    "CLIENT_DATA/uninstall.opsiscript",
                    "CLIENT_DATA/update.opsiscript",
                    "CLIENT_DATA/wec-winget-helper.opsiscript",
                    "OPSI/changelog.txt",
                    "OPSI/control.toml",
                ],
                files.Keys.Order(StringComparer.Ordinal));
            Assert.Contains("version = \"2\"", files["OPSI/control.toml"]);
        }
        finally
        {
            File.Delete(generated.ArchivePath);
        }
    }

    private static Dictionary<string, string> ReadTextFiles(string archivePath)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using FileStream archive = File.OpenRead(archivePath);
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null
                || entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            using var memory = new MemoryStream();
            entry.DataStream.CopyTo(memory);
            files[entry.Name.Replace('\\', '/')] = Encoding.UTF8.GetString(memory.ToArray());
        }
        return files;
    }
}

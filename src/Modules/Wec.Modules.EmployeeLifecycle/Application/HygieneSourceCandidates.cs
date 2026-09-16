using System.Security.Cryptography;
using System.Text.Json;
using Wec.Core.Contracts;
using Wec.Core.Targets;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed record HygieneSourceCandidate(AdComputerInventoryItem? Ad, KasperskyComputer? Kaspersky,
    OpsiComputerInventoryItem? Opsi, NessusComputerInventoryItem? Nessus, bool Ambiguous, string EvidenceKey);

internal static class HygieneSourceCandidates
{
    internal static IReadOnlyList<HygieneSourceCandidate> Collect(IReadOnlyList<AdComputerInventoryItem> adComputers,
        IReadOnlyList<KasperskyComputer> kasperskyComputers, IReadOnlyList<OpsiComputerInventoryItem> opsiComputers,
        IReadOnlyList<NessusComputerInventoryItem> nessusComputers)
    {
        var ad = adComputers.Select((value, index) => (Value: value, Index: index)).ToLookup(row => Alias(row.Value.ComputerName, "ad", row.Index));
        var ksc = kasperskyComputers.Select((value, index) => (Value: value, Index: index)).ToLookup(row => Alias(row.Value.ComputerName, "ksc", row.Index));
        var opsi = opsiComputers.Select((value, index) => (Value: value, Index: index)).ToLookup(row => Alias(row.Value.ComputerName, "opsi", row.Index));
        var nessus = nessusComputers.Select((value, index) => (Value: value, Index: index)).ToLookup(row => Alias(row.Value.ComputerName, "nessus", row.Index));
        List<HygieneSourceCandidate> candidates = [];
        foreach (string alias in ad.Select(group => group.Key).Concat(ksc.Select(group => group.Key))
            .Concat(opsi.Select(group => group.Key)).Concat(nessus.Select(group => group.Key)).Distinct().Order(StringComparer.Ordinal))
        {
            var directory = ad[alias].ToArray();
            var kaspersky = ksc[alias].ToArray();
            var deployment = opsi[alias].ToArray();
            var vulnerabilities = nessus[alias].ToArray();
            string[] qualifiedNames = directory.SelectMany(row => new[] { row.Value.DnsHostName, row.Value.ComputerName })
                .Concat(kaspersky.SelectMany(row => new[] { row.Value.Fqdn, row.Value.DnsName, row.Value.ComputerName }))
                .Concat(deployment.Select(row => row.Value.ComputerName))
                .Concat(vulnerabilities.SelectMany(row => new[] { row.Value.Fqdn, row.Value.ComputerName }))
                .Where(value => value is not null && Uri.CheckHostName(value) == UriHostNameType.Dns && value.TrimEnd('.').Contains('.'))
                .Select(HostAddress.ComparisonKey).Distinct().ToArray();
            bool ambiguous = directory.Length > 1 || kaspersky.Length > 1 || deployment.Length > 1 || vulnerabilities.Length > 1 || qualifiedNames.Length > 1;
            if (ambiguous)
            {
                candidates.AddRange(directory.Select(row => Candidate(row.Value, null, null, null, true, row.Index)));
                candidates.AddRange(kaspersky.Select(row => Candidate(null, row.Value, null, null, true, row.Index)));
                candidates.AddRange(deployment.Select(row => Candidate(null, null, row.Value, null, true, row.Index)));
                candidates.AddRange(vulnerabilities.Select(row => Candidate(null, null, null, row.Value, true, row.Index)));
            }
            else
            {
                candidates.Add(Candidate(directory.SingleOrDefault().Value, kaspersky.SingleOrDefault().Value,
                    deployment.SingleOrDefault().Value, vulnerabilities.SingleOrDefault().Value, false,
                    directory.SingleOrDefault().Index + kaspersky.SingleOrDefault().Index + deployment.SingleOrDefault().Index + vulnerabilities.SingleOrDefault().Index));
            }
        }
        return candidates;
    }

    private static string Alias(string? name, string source, int index) => string.IsNullOrWhiteSpace(name)
        ? $"unresolved:{source}:{index}" : HostAddress.ShortNameAlias(name) ?? HostAddress.ComparisonKey(name);

    private static HygieneSourceCandidate Candidate(AdComputerInventoryItem? ad, KasperskyComputer? ksc,
        OpsiComputerInventoryItem? opsi, NessusComputerInventoryItem? nessus, bool ambiguous, int occurrence)
    {
        string key = "evidence:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ad, ksc, opsi, nessus, occurrence })));
        return new(ad, ksc, opsi, nessus, ambiguous, key);
    }
}

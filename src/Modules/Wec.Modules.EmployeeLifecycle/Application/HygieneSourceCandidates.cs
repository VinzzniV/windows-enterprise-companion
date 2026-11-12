using System.Security.Cryptography;
using System.Text.Json;
using Wec.Core.Contracts;
using Wec.Core.Targets;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed record HygieneSourceCandidate(AdComputerInventoryItem? Ad, KasperskyComputer? Kaspersky,
    OpsiComputerInventoryItem? Opsi, NessusComputerInventoryItem? Nessus, bool Ambiguous,
    bool NessusAmbiguous, string EvidenceKey);

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
            .Concat(opsi.Select(group => group.Key)).Distinct().Order(StringComparer.Ordinal))
        {
            var directory = ad[alias].ToArray();
            var kaspersky = ksc[alias].ToArray();
            var deployment = opsi[alias].ToArray();
            var vulnerabilities = nessus[alias].ToArray();
            string[] primaryQualifiedNames = directory.SelectMany(row => new[] { row.Value.DnsHostName, row.Value.ComputerName })
                .Concat(kaspersky.SelectMany(row => new[] { row.Value.Fqdn, row.Value.DnsName, row.Value.ComputerName }))
                .Concat(deployment.Select(row => row.Value.ComputerName))
                .Where(value => value is not null && Uri.CheckHostName(value) == UriHostNameType.Dns && value.TrimEnd('.').Contains('.'))
                .Select(HostAddress.ComparisonKey).Distinct().ToArray();
            string[] nessusQualifiedNames = vulnerabilities.SelectMany(row => new[] { row.Value.Fqdn, row.Value.ComputerName })
                .Where(value => value is not null && Uri.CheckHostName(value) == UriHostNameType.Dns && value.TrimEnd('.').Contains('.'))
                .Select(HostAddress.ComparisonKey).Distinct().ToArray();
            bool primaryAmbiguous = directory.Length > 1 || kaspersky.Length > 1 || deployment.Length > 1 || primaryQualifiedNames.Length > 1;
            bool nessusAmbiguous = vulnerabilities.Length > 1
                || primaryQualifiedNames.Concat(nessusQualifiedNames).Distinct().Count() > 1;
            if (primaryAmbiguous)
            {
                candidates.AddRange(directory.Select(row => Candidate(row.Value, null, null, null, true, vulnerabilities.Length > 0, row.Index)));
                candidates.AddRange(kaspersky.Select(row => Candidate(null, row.Value, null, null, true, vulnerabilities.Length > 0, row.Index)));
                candidates.AddRange(deployment.Select(row => Candidate(null, null, row.Value, null, true, vulnerabilities.Length > 0, row.Index)));
            }
            else
            {
                int nessusOccurrence = vulnerabilities.Length == 1 ? vulnerabilities[0].Index : 0;
                candidates.Add(Candidate(directory.SingleOrDefault().Value, kaspersky.SingleOrDefault().Value,
                    deployment.SingleOrDefault().Value, nessusAmbiguous ? null : vulnerabilities.SingleOrDefault().Value, false,
                    nessusAmbiguous,
                    directory.SingleOrDefault().Index + kaspersky.SingleOrDefault().Index + deployment.SingleOrDefault().Index + nessusOccurrence));
            }
        }
        return candidates;
    }

    private static string Alias(string? name, string source, int index) => string.IsNullOrWhiteSpace(name)
        ? $"unresolved:{source}:{index}" : HostAddress.ShortNameAlias(name) ?? HostAddress.ComparisonKey(name);

    private static HygieneSourceCandidate Candidate(AdComputerInventoryItem? ad, KasperskyComputer? ksc,
        OpsiComputerInventoryItem? opsi, NessusComputerInventoryItem? nessus, bool ambiguous,
        bool nessusAmbiguous, int occurrence)
    {
        string key = "evidence:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ad, ksc, opsi, nessus, occurrence })));
        return new(ad, ksc, opsi, nessus, ambiguous, nessusAmbiguous, key);
    }
}

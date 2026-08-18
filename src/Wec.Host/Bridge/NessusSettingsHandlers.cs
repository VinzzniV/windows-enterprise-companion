using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.VulnerabilityManagement;

namespace Wec.Host.Bridge;

public sealed record NessusSettingsValue(string ServerUrl,int RequestTimeoutSeconds,string TrustedCertificateThumbprint,int CacheTtlMinutes,int BackfillDays,int RetentionDays,int StaleWarningDays,int StaleCriticalDays,IReadOnlyList<long> ExcludedScanIds,IReadOnlyList<string> MissingNessusExcludedOuPatterns,IReadOnlyList<string> MissingNessusExcludedHostPatterns);
public sealed record GetNessusSettingsRequest;
public sealed record SaveNessusSettingsRequest(NessusSettingsValue Settings);
public sealed record NessusSettingsResult(NessusSettingsValue Settings,bool RestartRequired);

internal sealed class GetNessusSettingsHandler(IOptions<VulnerabilityManagementOptions> options):IActionHandler<GetNessusSettingsRequest,NessusSettingsResult>
{
    public string Module=>"system";public string Action=>"getNessusSettings";
    public Task<Result<NessusSettingsResult>> HandleAsync(GetNessusSettingsRequest p,CancellationToken ct)=>Task.FromResult(Result.Success(new NessusSettingsResult(Map(options.Value),false)));
    internal static NessusSettingsValue Map(VulnerabilityManagementOptions x)=>new(x.ServerUrl,x.RequestTimeoutSeconds,x.TrustedCertificateThumbprint,x.CacheTtlMinutes,x.BackfillDays,x.RetentionDays,x.StaleWarningDays,x.StaleCriticalDays,x.ExcludedScanIds,x.MissingNessusExcludedOuPatterns,x.MissingNessusExcludedHostPatterns);
}
internal sealed class SaveNessusSettingsHandler(UserSettingsStore store):IActionHandler<SaveNessusSettingsRequest,NessusSettingsResult>
{
    public string Module=>"system";public string Action=>"saveNessusSettings";
    public async Task<Result<NessusSettingsResult>> HandleAsync(SaveNessusSettingsRequest p,CancellationToken ct)
    {
        NessusSettingsValue i=p.Settings;string pin=string.Concat(i.TrustedCertificateThumbprint.Where(Uri.IsHexDigit)).ToUpperInvariant();
        if(!Uri.TryCreate(i.ServerUrl,UriKind.Absolute,out Uri? uri)||uri.Scheme!=Uri.UriSchemeHttps)
        {
            return Invalid("Nessus requires a valid HTTPS URL.");
        }
        if(pin.Length is not 0 and not 40 and not 64)
        {
            return Invalid("The certificate fingerprint must be empty, SHA-1, or SHA-256.");
        }
        var o=new VulnerabilityManagementOptions{ServerUrl=uri.ToString().TrimEnd('/'),RequestTimeoutSeconds=i.RequestTimeoutSeconds,TrustedCertificateThumbprint=pin,CacheTtlMinutes=i.CacheTtlMinutes,BackfillDays=i.BackfillDays,RetentionDays=i.RetentionDays,StaleWarningDays=i.StaleWarningDays,StaleCriticalDays=i.StaleCriticalDays,ExcludedScanIds=(i.ExcludedScanIds??[]).Distinct().ToList(),MissingNessusExcludedOuPatterns=Clean(i.MissingNessusExcludedOuPatterns),MissingNessusExcludedHostPatterns=Clean(i.MissingNessusExcludedHostPatterns)};
        var validation=new List<ValidationResult>();
        if(!Validator.TryValidateObject(o,new ValidationContext(o),validation,true)||o.StaleCriticalDays<=o.StaleWarningDays)
        {
            return Invalid(string.Join(" ",validation.Select(x=>x.ErrorMessage).Append("The critical stale threshold must be greater than the warning threshold.")));
        }
        Result<bool> saved=await store.SaveVulnerabilityManagementAsync(o,ct);return saved.IsFailure?Result.Failure<NessusSettingsResult>(saved.Error!):Result.Success(new NessusSettingsResult(GetNessusSettingsHandler.Map(o),true));
    }
    private static List<string> Clean(IReadOnlyList<string>? values)=>(values??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static Result<NessusSettingsResult> Invalid(string message)=>Result.Failure<NessusSettingsResult>(new Error(ErrorCode.InvalidRequest,message));
}

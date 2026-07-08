using Microsoft.Extensions.Options;
using Wec.Core.Ccrx;
using Wec.Core.Results;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Application;

/// <summary>
/// Checks whether a Kyocera Command Center RX printer is configured to notify the
/// external service provider: SMTP on with the right mail server, a sender
/// address, and a low-toner event report to a recipient. Read-only.
/// </summary>
public sealed class NotificationConfigService
{
    // The two CCRX model files that carry the settings (reverse-engineered).
    internal const string EmailModelPath = "/js/jssrc/model/funcset/email/FuncSet_Email_EmailSet.model.htm";
    internal const string ReportModelPath = "/js/jssrc/model/mngset/nfcrptset/MngSet_NfcRpt_NfcRptSet.model.htm";

    private const string ModeOn = "_mode_on";

    private readonly ICcrxClient _ccrxClient;
    private readonly PrintManagementOptions _options;

    public NotificationConfigService(ICcrxClient ccrxClient, IOptions<PrintManagementOptions> options)
    {
        _ccrxClient = ccrxClient;
        _options = options.Value;
    }

    public async Task<PrinterNotificationCheck> CheckAsync(
        string host,
        string? siteCode,
        CcrxCredentials credentials,
        CancellationToken cancellationToken)
    {
        Result<CcrxReadResult> read = await _ccrxClient.ReadModelsAsync(
            host, credentials, [EmailModelPath, ReportModelPath], cancellationToken);
        if (read.IsFailure)
        {
            return new PrinterNotificationCheck(host, NotificationCheckStatus.NotChecked, [], read.Error!.Message);
        }

        IReadOnlyList<NotificationRule> rules = Evaluate(
            read.Value.Model(EmailModelPath), read.Value.Model(ReportModelPath), siteCode);
        NotificationCheckStatus status = rules.All(rule => rule.Passed)
            ? NotificationCheckStatus.Ok
            : NotificationCheckStatus.Warning;
        return new PrinterNotificationCheck(host, status, rules, null);
    }

    /// <summary>Pure rule evaluation — the values above are I/O, this is the logic.</summary>
    internal IReadOnlyList<NotificationRule> Evaluate(CcrxModel? email, CcrxModel? report, string? siteCode)
    {
        return [EvaluateSmtp(email, siteCode), EvaluateSender(email), EvaluateEventReport(report)];
    }

    private NotificationRule EvaluateSmtp(CcrxModel? email, string? siteCode)
    {
        if (email is null)
        {
            return new NotificationRule("smtp", RuleTitles.Smtp, false, "E-mail settings could not be read.");
        }

        bool smtpOn = email.Get("smtpMode") == ModeOn && email.Get("sndSmtpMode") == ModeOn;
        string server = email.Get("smtpServerName")?.Trim() ?? string.Empty;

        if (!smtpOn)
        {
            return new NotificationRule("smtp", RuleTitles.Smtp, false, "SMTP sending is turned off.");
        }

        if (server.Length == 0)
        {
            return new NotificationRule("smtp", RuleTitles.Smtp, false, "SMTP is on but no server is configured.");
        }

        string? expected = ExpectedServer(siteCode);
        if (expected is not null && !string.Equals(server, expected, StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationRule(
                "smtp", RuleTitles.Smtp, false, $"SMTP server is '{server}', expected '{expected}'.");
        }

        return new NotificationRule("smtp", RuleTitles.Smtp, true, $"SMTP on via {server}.");
    }

    private static NotificationRule EvaluateSender(CcrxModel? email)
    {
        string sender = email?.Get("senderAddress")?.Trim() ?? string.Empty;
        return sender.Length > 0
            ? new NotificationRule("sender", RuleTitles.Sender, true, sender)
            : new NotificationRule("sender", RuleTitles.Sender, false, "No sender e-mail address is set.");
    }

    private NotificationRule EvaluateEventReport(CcrxModel? report)
    {
        if (report is null)
        {
            return new NotificationRule("eventreport", RuleTitles.EventReport, false, "Report settings could not be read.");
        }

        string? expectedRecipient = string.IsNullOrWhiteSpace(_options.ExpectedEventRecipient)
            ? null
            : _options.ExpectedEventRecipient!.Trim();

        // Up to three schedules; a match = an address that reports low toner
        // (and, if configured, to the expected recipient).
        for (int schedule = 1; schedule <= 3; schedule++)
        {
            string address = report.Get($"evtSch{schedule}Address")?.Trim() ?? string.Empty;
            bool lowToner = report.Get($"evtSch{schedule}LowToner") == "1";
            if (address.Length == 0 || !lowToner)
            {
                continue;
            }

            if (expectedRecipient is not null
                && !string.Equals(address, expectedRecipient, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new NotificationRule("eventreport", RuleTitles.EventReport, true, $"Low toner reported to {address}.");
        }

        string detail = expectedRecipient is null
            ? "No low-toner event report to any recipient."
            : $"No low-toner event report to {expectedRecipient}.";
        return new NotificationRule("eventreport", RuleTitles.EventReport, false, detail);
    }

    private string? ExpectedServer(string? siteCode)
    {
        if (string.IsNullOrWhiteSpace(_options.ExpectedSmtpServer))
        {
            return null;
        }

        // {site} lets one setting cover per-site mail servers (pk-srvmail, kf-srvmail …).
        return _options.ExpectedSmtpServer!.Replace(
            "{site}", (siteCode ?? string.Empty).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase).Trim();
    }

    private static class RuleTitles
    {
        public const string Smtp = "SMTP enabled with the correct mail server";
        public const string Sender = "Sender e-mail address set";
        public const string EventReport = "Low-toner event report to a recipient";
    }
}

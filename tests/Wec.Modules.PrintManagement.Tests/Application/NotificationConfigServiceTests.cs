using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Ccrx;
using Wec.Core.Results;
using Wec.Modules.PrintManagement;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public class NotificationConfigServiceTests
{
    private readonly ICcrxClient _ccrxClient = Substitute.For<ICcrxClient>();

    private NotificationConfigService CreateService(
        string? expectedServer = null, string? expectedRecipient = null) =>
        new(_ccrxClient, Options.Create(new PrintManagementOptions
        {
            ExpectedSmtpServer = expectedServer,
            ExpectedEventRecipient = expectedRecipient,
        }));

    private static CcrxModel Email(params (string Name, string Value)[] properties) =>
        new(NotificationConfigService.EmailModelPath, properties.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal));

    private static CcrxModel Report(params (string Name, string Value)[] properties) =>
        new(NotificationConfigService.ReportModelPath, properties.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal));

    private static CcrxModel FullyConfiguredEmail() => Email(
        ("smtpMode", "_mode_on"), ("sndSmtpMode", "_mode_on"),
        ("smtpServerName", "pk-srvmail.kauth.local"),
        ("senderAddress", "PK-NETPRT039@kauth-denkingen.de"));

    private static CcrxModel LowTonerReport(string address) => Report(
        ("evtSch1Address", address), ("evtSch1LowToner", "1"),
        ("evtSch2Address", ""), ("evtSch2LowToner", "1"));

    private static bool Rule(IReadOnlyList<NotificationRule> rules, string id) =>
        rules.Single(rule => rule.Id == id).Passed;

    [Fact]
    public void Evaluate_FullyConfigured_AllRulesPass()
    {
        IReadOnlyList<NotificationRule> rules = CreateService().Evaluate(
            FullyConfiguredEmail(), LowTonerReport("mps@compend.de"), "PK");

        Assert.All(rules, rule => Assert.True(rule.Passed, rule.Detail));
    }

    [Fact]
    public void Evaluate_SmtpOff_FailsSmtpRuleOnly()
    {
        CcrxModel email = Email(
            ("smtpMode", "_mode_off"), ("sndSmtpMode", "_mode_off"),
            ("smtpServerName", "pk-srvmail.kauth.local"), ("senderAddress", "a@b.de"));

        IReadOnlyList<NotificationRule> rules = CreateService().Evaluate(email, LowTonerReport("mps@compend.de"), "PK");

        Assert.False(Rule(rules, "smtp"));
        Assert.True(Rule(rules, "sender"));
        Assert.True(Rule(rules, "eventreport"));
    }

    [Theory]
    [InlineData("pk-srvmail.kauth.local", true)]
    [InlineData("PK-SRVMAIL.kauth.local", true)] // case-insensitive
    [InlineData("wrong-server.local", false)]
    public void Evaluate_ExpectedServerWithSiteToken_IsEnforced(string server, bool expectedPass)
    {
        CcrxModel email = Email(
            ("smtpMode", "_mode_on"), ("sndSmtpMode", "_mode_on"),
            ("smtpServerName", server), ("senderAddress", "a@b.de"));

        IReadOnlyList<NotificationRule> rules = CreateService(expectedServer: "{site}-srvmail.kauth.local")
            .Evaluate(email, LowTonerReport("mps@compend.de"), "PK");

        Assert.Equal(expectedPass, Rule(rules, "smtp"));
    }

    [Fact]
    public void Evaluate_NoSender_FailsSenderRule()
    {
        CcrxModel email = Email(
            ("smtpMode", "_mode_on"), ("sndSmtpMode", "_mode_on"),
            ("smtpServerName", "pk-srvmail.kauth.local"), ("senderAddress", ""));

        IReadOnlyList<NotificationRule> rules = CreateService().Evaluate(email, LowTonerReport("x@y.de"), "PK");

        Assert.False(Rule(rules, "sender"));
    }

    [Fact]
    public void Evaluate_NoLowTonerReport_FailsEventRule()
    {
        // A recipient exists but low toner is not among the reported events.
        CcrxModel report = Report(("evtSch1Address", "x@y.de"), ("evtSch1LowToner", "0"));

        IReadOnlyList<NotificationRule> rules = CreateService().Evaluate(FullyConfiguredEmail(), report, "PK");

        Assert.False(Rule(rules, "eventreport"));
    }

    [Fact]
    public void Evaluate_ExpectedRecipientMismatch_FailsEventRule()
    {
        IReadOnlyList<NotificationRule> rules = CreateService(expectedRecipient: "mps@compend.de")
            .Evaluate(FullyConfiguredEmail(), LowTonerReport("someone-else@corp.de"), "PK");

        Assert.False(Rule(rules, "eventreport"));
    }

    [Fact]
    public async Task CheckAsync_ClientFailure_ReturnsNotChecked()
    {
        _ccrxClient
            .ReadModelsAsync(Arg.Any<string>(), Arg.Any<CcrxCredentials>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CcrxReadResult>(new Error(ErrorCode.AuthenticationFailed, "login refused")));

        PrinterNotificationCheck check = await CreateService().CheckAsync(
            "10.0.0.5", "PK", CcrxCredentials.Default, CancellationToken.None);

        Assert.Equal(NotificationCheckStatus.NotChecked, check.Status);
        Assert.Equal("login refused", check.Error);
        Assert.Empty(check.Rules);
    }

    [Fact]
    public async Task CheckAsync_FullyConfigured_ReturnsOk()
    {
        _ccrxClient
            .ReadModelsAsync(Arg.Any<string>(), Arg.Any<CcrxCredentials>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CcrxReadResult([FullyConfiguredEmail(), LowTonerReport("mps@compend.de")])));

        PrinterNotificationCheck check = await CreateService().CheckAsync(
            "10.0.0.5", "PK", CcrxCredentials.Default, CancellationToken.None);

        Assert.Equal(NotificationCheckStatus.Ok, check.Status);
    }
}

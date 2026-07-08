using Wec.Core.Ccrx;

namespace Wec.Core.Tests.Ccrx;

public class CcrxModelParserTests
{
    [Fact]
    public void Parse_ExtractsPpAssignments()
    {
        const string body = """
            //<script>
            var Model;
            _pp.smtpMode = '_mode_on';
            _pp.smtpServerName = 'pk-srvmail.kauth.local';
            _pp.senderAddress = 'PK-NETPRT039@kauth-denkingen.de';
            _pp.emailSize = '0';
            """;

        IReadOnlyDictionary<string, string> properties = CcrxModelParser.Parse(body);

        Assert.Equal("_mode_on", properties["smtpMode"]);
        Assert.Equal("pk-srvmail.kauth.local", properties["smtpServerName"]);
        Assert.Equal("PK-NETPRT039@kauth-denkingen.de", properties["senderAddress"]);
        Assert.Equal("0", properties["emailSize"]);
    }

    [Fact]
    public void Parse_IgnoresNonPpTokensAndTakesLastAssignment()
    {
        const string body = "var x = 'ignored'; _pp.a = 'first'; something.b = 'skip'; _pp.a = 'last';";

        IReadOnlyDictionary<string, string> properties = CcrxModelParser.Parse(body);

        Assert.Single(properties);
        Assert.Equal("last", properties["a"]); // last assignment wins, like the browser
    }

    [Fact]
    public void Parse_EmptyOrStubBody_YieldsNoProperties()
    {
        Assert.Empty(CcrxModelParser.Parse(""));
        Assert.Empty(CcrxModelParser.Parse("<html><body>login required</body></html>"));
    }
}

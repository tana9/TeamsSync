using TeamsSync.Infrastructure.Graph;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class GraphErrorFormatterTests
{
    [Fact]
    public void DiagnosticSummary_巨大なJSONはメッセージを制限し未知のフィールドを記録しない()
    {
        string secret = new('x', 5000);
        string body = $"{{\"error\":{{\"code\":\"Denied\",\"message\":\"{secret}\",\"token\":\"secret-token\"}}}}";

        string summary = GraphErrorFormatter.DiagnosticSummary(body);

        Assert.Contains("Code=Denied", summary);
        Assert.DoesNotContain("secret-token", summary);
        Assert.True(summary.Length < 700);
    }

    [Fact]
    public void DiagnosticSummary_HTML本文は内容を記録しない()
    {
        string summary = GraphErrorFormatter.DiagnosticSummary("<html>confidential</html>");

        Assert.Contains("構造化されたGraphエラー詳細なし", summary);
        Assert.DoesNotContain("confidential", summary);
    }
}
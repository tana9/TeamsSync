using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Architecture;

public sealed class LayerDependencyTests
{
    private static readonly string ProjectDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "TeamsSync"));

    [Fact]
    public void DomainとApplicationは外側の層へ依存しない()
    {
        AssertNoReferences(Path.Combine(ProjectDirectory, "Domain"),
            "TeamsSync.Application", "TeamsSync.Infrastructure", "TeamsSync.Presentation");
        AssertNoReferences(Path.Combine(ProjectDirectory, "Application"),
            "TeamsSync.Infrastructure", "TeamsSync.Presentation");
    }

    [Fact]
    public void ファイル監査進捗実行結果はApplicationモデルとして公開する()
    {
        Type[] applicationModels =
        [
            typeof(MemberListDocument), typeof(SyncAuditContext), typeof(SyncProgress),
            typeof(SyncOperationResult), typeof(SyncOperationsResult), typeof(SyncPlanRevalidation)
        ];

        Assert.All(applicationModels,
            type => Assert.Equal("TeamsSync.Application.Models", type.Namespace));
        Assert.Equal("TeamsSync.Domain.Teams", typeof(SyncPlan).Namespace);
        Assert.Equal("TeamsSync.Domain.Teams", typeof(SyncChange).Namespace);
    }

    private static void AssertNoReferences(string directory, params string[] forbiddenNamespaces)
    {
        foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            foreach (string forbiddenNamespace in forbiddenNamespaces)
            {
                Assert.DoesNotContain(forbiddenNamespace, source, StringComparison.Ordinal);
            }
        }
    }
}
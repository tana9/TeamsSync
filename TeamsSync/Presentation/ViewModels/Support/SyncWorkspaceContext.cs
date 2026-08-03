using TeamsSync.Domain.Teams;
using TeamsSync.Application.Models;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>同期画面で共有する選択チーム、入力文書、認証コンテキストを保持する。</summary>
internal sealed class SyncWorkspaceContext
{
    public TeamInfo? Team { get; private set; }
    public MemberListDocument? Document { get; private set; }
    public bool SignedIn { get; private set; }
    public string? TenantId { get; private set; }
    public string? ActorObjectId { get; private set; }

    public bool Update(TeamInfo? team, MemberListDocument? document, bool signedIn,
        string? tenantId, string? actorObjectId)
    {
        if (ReferenceEquals(Team, team) && ReferenceEquals(Document, document) && SignedIn == signedIn &&
            TenantId == tenantId && ActorObjectId == actorObjectId)
        {
            return false;
        }

        Team = team;
        Document = document;
        SignedIn = signedIn;
        TenantId = tenantId;
        ActorObjectId = actorObjectId;
        return true;
    }
}

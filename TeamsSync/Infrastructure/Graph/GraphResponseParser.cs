using System.Text.Json;

using TeamsSync.Domain.Teams;

using GraphAadUserConversationMember = Microsoft.Graph.Models.AadUserConversationMember;
using GraphConversationMember = Microsoft.Graph.Models.ConversationMember;
using GraphDirectoryObject = Microsoft.Graph.Models.DirectoryObject;
using GraphGroup = Microsoft.Graph.Models.Group;
using GraphUser = Microsoft.Graph.Models.User;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
///     Microsoft Graph応答(生JSONおよび公式SDKモデルの両方)を、ドメインモデルの
///     <see cref="TeamMember" />・<see cref="DirectoryUser" />へ変換する
/// </summary>
internal static class GraphResponseParser
{
    /// <summary>公式SDKのメンバーモデル配列を<see cref="TeamMember" />一覧へ変換する</summary>
    public static List<TeamMember> ParseTeamMembers(IEnumerable<GraphConversationMember> values)
    {
        return values.Select(member =>
        {
            GraphAadUserConversationMember? user = member as GraphAadUserConversationMember;
            return new TeamMember(Required(member.Id, "id"),
                Required(user?.UserId ?? AdditionalString(member, "userId"), "userId"),
                member.DisplayName ?? "", user?.Email ?? AdditionalString(member, "email") ?? "",
                HasOwnerRole(member.Roles));
        }).ToList();
    }

    // /me/ownedObjectsは所有するディレクトリオブジェクト全般(グループ・アプリ・サービスプリンシパル等)を
    // 返すため、Group型のIDだけへ絞り込む。呼び出し元でjoinedTeams(実際に参加しているチーム)との
    // 積集合を取るため、Teams化済みかどうかをここで判定する必要はない。
    // 実際、resourceProvisioningOptionsで絞り込もうとすると、このアプリのスコープ(User.Read等)では
    // Group型オブジェクトの詳細プロパティを読む権限がなく、Graphが「制限された情報」(@odata.typeと
    // idのみ、他プロパティはnull)として返すため、resourceProvisioningOptionsが常にnullになり
    // 所有チームを1件も検出できなくなる。idは制限モードでも必ず返るためこちらだけに依拠する
    /// <summary>所有オブジェクト一覧から、グループ型オブジェクトのIDだけを抽出する</summary>
    public static HashSet<string> ParseOwnedTeamIds(IEnumerable<GraphDirectoryObject> ownedObjects)
    {
        return ownedObjects
            .OfType<GraphGroup>()
            .Select(group => Required(group.Id, "id"))
            .ToHashSet();
    }

    /// <summary>ロール一覧に"owner"が含まれるかどうかを判定する</summary>
    private static bool HasOwnerRole(IEnumerable<string?>? roles)
    {
        return roles?.Any(role => role == "owner") == true;
    }

    private static string? AdditionalString(GraphConversationMember member, string name)
    {
        if (!member.AdditionalData.TryGetValue(name, out object? value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => value?.ToString()
        };
    }

    /// <summary>Graph応答(生JSON)のユーザー要素を<see cref="DirectoryUser" />へ変換する</summary>
    public static DirectoryUser ToDirectoryUser(JsonElement user)
    {
        return new DirectoryUser(Required(user, "id"), Required(user, "displayName"),
            Required(user, "userPrincipalName"), Optional(user, "mail"));
    }

    /// <summary>Graph応答(生JSON)のユーザー配列を<see cref="DirectoryUser" />一覧へ変換する</summary>
    public static List<DirectoryUser> ToDirectoryUsers(IEnumerable<JsonElement> users)
    {
        return users.Select(ToDirectoryUser).ToList();
    }

    /// <summary>公式SDKのユーザーモデルを<see cref="DirectoryUser" />へ変換する</summary>
    public static DirectoryUser ToDirectoryUser(GraphUser user)
    {
        return new DirectoryUser(Required(user.Id, "id"), Required(user.DisplayName, "displayName"),
            Required(user.UserPrincipalName, "userPrincipalName"), user.Mail);
    }

    /// <summary>公式SDKのユーザーモデル配列を<see cref="DirectoryUser" />一覧へ変換する</summary>
    public static List<DirectoryUser> ToDirectoryUsers(IEnumerable<GraphUser> users)
    {
        return users.Select(ToDirectoryUser).ToList();
    }

    /// <summary>文字列値がnullでないことを検証する。nullの場合は例外をスローする</summary>
    public static string Required(string? value, string name)
    {
        return value ?? throw new InvalidDataException($"{name} がありません。");
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合は例外をスローする</summary>
    public static string Required(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{name} がありません。");
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合はnullを返す</summary>
    public static string? Optional(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
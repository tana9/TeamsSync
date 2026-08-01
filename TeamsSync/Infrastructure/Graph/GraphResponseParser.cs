using System.Text.Json;

using TeamsSync.Domain.Teams;

using GraphAadUserConversationMember = Microsoft.Graph.Models.AadUserConversationMember;
using GraphConversationMember = Microsoft.Graph.Models.ConversationMember;
using GraphUser = Microsoft.Graph.Models.User;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
///     Microsoft Graph応答(生JSONおよび公式SDKモデルの両方)を、ドメインモデルの
///     <see cref="TeamMember" />・<see cref="DirectoryUser" />へ変換する。
/// </summary>
internal static class GraphResponseParser
{
    /// <summary>Graph応答($batch等の生JSON)のメンバー配列を<see cref="TeamMember" />一覧へ変換する。</summary>
    public static List<TeamMember> ParseTeamMembers(IEnumerable<JsonElement> values)
    {
        return values.Select(x => new TeamMember(
                Required(x, "id"),
                Required(x, "userId"),
                Optional(x, "displayName") ?? "",
                Optional(x, "email") ?? "",
                x.TryGetProperty("roles", out JsonElement roles) &&
                roles.EnumerateArray().Any(r => r.GetString() == "owner")))
            .ToList();
    }

    /// <summary>公式SDKのメンバーモデル配列を<see cref="TeamMember" />一覧へ変換する。</summary>
    public static List<TeamMember> ParseTeamMembers(IEnumerable<GraphConversationMember> values)
    {
        return values.Select(member =>
        {
            GraphAadUserConversationMember? user = member as GraphAadUserConversationMember;
            return new TeamMember(Required(member.Id, "id"),
                Required(user?.UserId ?? AdditionalString(member, "userId"), "userId"),
                member.DisplayName ?? "", user?.Email ?? AdditionalString(member, "email") ?? "",
                member.Roles?.Any(role => role == "owner") == true);
        }).ToList();
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
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value?.ToString()
        };
    }

    /// <summary>Graph応答(生JSON)のユーザー要素を<see cref="DirectoryUser" />へ変換する。</summary>
    public static DirectoryUser ToDirectoryUser(JsonElement user)
    {
        return new DirectoryUser(Required(user, "id"), Required(user, "displayName"),
            Required(user, "userPrincipalName"), Optional(user, "mail"));
    }

    /// <summary>Graph応答(生JSON)のユーザー配列を<see cref="DirectoryUser" />一覧へ変換する。</summary>
    public static List<DirectoryUser> ToDirectoryUsers(IEnumerable<JsonElement> users)
    {
        return users.Select(ToDirectoryUser).ToList();
    }

    /// <summary>公式SDKのユーザーモデルを<see cref="DirectoryUser" />へ変換する。</summary>
    public static DirectoryUser ToDirectoryUser(GraphUser user)
    {
        return new DirectoryUser(Required(user.Id, "id"), Required(user.DisplayName, "displayName"),
            Required(user.UserPrincipalName, "userPrincipalName"), user.Mail);
    }

    /// <summary>公式SDKのユーザーモデル配列を<see cref="DirectoryUser" />一覧へ変換する。</summary>
    public static List<DirectoryUser> ToDirectoryUsers(IEnumerable<GraphUser> users)
    {
        return users.Select(ToDirectoryUser).ToList();
    }

    private static string Required(string? value, string name)
    {
        return value ?? throw new InvalidDataException($"{name} がありません。");
    }

    /// <summary><see cref="GraphHttpClient.Required" />への委譲。</summary>
    private static string Required(JsonElement element, string name)
    {
        return GraphHttpClient.Required(element, name);
    }

    /// <summary><see cref="GraphHttpClient.Optional" />への委譲。</summary>
    private static string? Optional(JsonElement element, string name)
    {
        return GraphHttpClient.Optional(element, name);
    }
}
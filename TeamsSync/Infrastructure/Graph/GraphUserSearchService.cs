using System.Globalization;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
/// 直接参照(UPN/メールの完全一致)でユーザーが見つからなかった場合の、
/// ディレクトリユーザー検索フォールバックを担当する。
/// </summary>
internal sealed class GraphUserSearchService(GraphSdkClient sdk)
{
    // 直接参照(UPN/メールの完全一致)で見つからない場合のフォールバック。
    // 1) mail/UPNの完全一致フィルター、2) 表示名の全文検索、3) 表示名の前方一致、の順に緩めて試す。
    // 段階を分けることで、$search特有のあいまい一致による誤検出を最小限にしている。
    /// <summary>
    /// 直接参照で見つからなかった場合のフォールバック検索。メール/UPNの完全一致、
    /// 表示名の全文検索、表示名の前方一致の順に段階的に緩めて試す。
    /// </summary>
    public async Task<IReadOnlyList<DirectoryUser>> SearchAsync(string identifier,
        CancellationToken cancellationToken)
    {
        var escaped = identifier.Replace("'", "''");
        var filter = $"mail eq '{escaped}' or userPrincipalName eq '{escaped}'";
        var addressMatches =
            GraphResponseParser.ToDirectoryUsers(await sdk.FindUsersAsync(filter, null, 2, cancellationToken));
        if (addressMatches.Count > 0) return addressMatches;

        var searchText = identifier.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var search = $"\"displayName:{searchText}\"";
        var nameMatches = GraphResponseParser
            .ToDirectoryUsers(await sdk.FindUsersAsync(null, search, 25, cancellationToken))
            .Where(user => UserIdentifier.NameEquals(user.DisplayName, identifier)).ToList();
        if (nameMatches.Count > 0) return nameMatches;

        var normalized = UserIdentifier.NormalizeName(identifier);
        if (normalized.Length == 0) return [];
        var firstCharacter = StringInfo.GetNextTextElement(normalized).Replace("'", "''");
        var nameFilter = $"startswith(displayName,'{firstCharacter}')";
        return GraphResponseParser.ToDirectoryUsers(await sdk.FindUsersAsync(nameFilter, null, 100, cancellationToken))
            .Where(user => UserIdentifier.NameEquals(user.DisplayName, identifier)).ToList();
    }
}

namespace TeamsSync.Domain.Teams;

/// <summary>
///     1件の入力アドレスを現メンバーまたはディレクトリ検索結果と突き合わせて解決する業務ルール。
///     Graph通信を伴わない、純粋な判定だけを担う
/// </summary>
public static class AddressResolver
{
    /// <summary>
    ///     メールアドレスまたは正規化した氏名で現メンバーから一致を探す。
    ///     複数一致した場合は特定不能としてエラーにする。一致がなければnullを返し、
    ///     呼び出し側にディレクトリ検索を委ねる
    /// </summary>
    public static AddressResolution? TryResolveFromRoster(string address, TeamRoster roster)
    {
        IReadOnlyList<TeamMember> existingMatches = roster.FindByAddressOrName(address);
        if (existingMatches.Count > 1)
        {
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorReason: ChangeReason.AmbiguousCurrentMember);
        }

        return existingMatches.Count == 1
            ? new AddressResolution(ResolutionOutcome.ExistingSingle, address, existingMatches[0])
            : null;
    }

    /// <summary>
    ///     ディレクトリ検索の候補から一意なユーザーを特定し、既に別アドレスでメンバーかどうかを判定する
    /// </summary>
    public static AddressResolution ResolveFromDirectory(string address,
        IReadOnlyList<DirectoryUser> candidates, TeamRoster roster)
    {
        List<DirectoryUser> exactMatches = candidates
            .DistinctBy(user => user.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (exactMatches.Count != 1)
        {
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorReason: exactMatches.Count > 1
                    ? ChangeReason.AmbiguousDirectoryUser
                    : ChangeReason.UserNotFound);
        }

        DirectoryUser user = exactMatches[0];
        TeamMember? sameUser = roster.FindByUserId(user.Id);
        return sameUser is not null
            ? new AddressResolution(ResolutionOutcome.SameUserDifferentAddress, address,
                sameUser, user)
            : new AddressResolution(ResolutionOutcome.NewUser, address, User: user);
    }
}
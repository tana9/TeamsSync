namespace TeamsSync.Domain.Teams;

/// <summary>
///     現在のチームメンバー一覧を保持し、メールアドレス・正規化氏名・ユーザーIDでの検索や、
///     同期プラン作成に必要な集計・スナップショットを提供する。
/// </summary>
public sealed class TeamRoster
{
    private readonly Dictionary<string, List<TeamMember>> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TeamMember>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TeamMember> _byUserId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>コンストラクター。現在のメンバー一覧から検索用の索引を構築する。</summary>
    public TeamRoster(IReadOnlyList<TeamMember> members)
    {
        Members = members;
        foreach (TeamMember member in members)
        {
            Add(_byEmail, member.Email, member);
            Add(_byName, UserIdentifier.NormalizeName(member.DisplayName), member);
            _byUserId.TryAdd(member.UserId, member);
        }
    }

    /// <summary>現在のメンバー一覧。</summary>
    public IReadOnlyList<TeamMember> Members { get; }

    /// <summary>一般メンバー(所有者を除く)の件数。</summary>
    public int NonOwnerCount => Members.Count(x => !x.IsOwner);

    /// <summary>
    ///     メールアドレスまたは正規化した氏名で現メンバーから一致を探す。
    /// </summary>
    public IReadOnlyList<TeamMember> FindByAddressOrName(string value)
    {
        List<TeamMember> matches = [];
        if (_byEmail.TryGetValue(value, out List<TeamMember>? emailMatches))
        {
            matches.AddRange(emailMatches);
        }

        string normalizedName = UserIdentifier.NormalizeName(value);
        if (_byName.TryGetValue(normalizedName, out List<TeamMember>? nameMatches))
        {
            foreach (TeamMember member in nameMatches)
            {
                if (!matches.Any(match => string.Equals(match.MembershipId, member.MembershipId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(member);
                }
            }
        }

        return matches;
    }

    /// <summary>ユーザーIDで現メンバーを検索する。</summary>
    public TeamMember? FindByUserId(string userId)
    {
        return _byUserId.GetValueOrDefault(userId);
    }

    /// <summary>
    ///     同期入力として取り込み可能な一般メンバー(所有者を除き、メールアドレスがあるメンバー)を、
    ///     メールアドレスの重複を除いてメールアドレス順に並べて返す。
    /// </summary>
    public IReadOnlyList<TeamMember> ImportableMembers()
    {
        return Members
            .Where(member => !member.IsOwner && !string.IsNullOrWhiteSpace(member.Email))
            .GroupBy(member => member.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(member => member.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>再検証(プラン同値性の判定)で使う、現メンバーシップのスナップショットを作成する。</summary>
    public IReadOnlyList<TeamMembershipSnapshot> BuildMembershipSnapshot()
    {
        return Members
            .Select(member => new TeamMembershipSnapshot(member.MembershipId, member.UserId, member.IsOwner))
            .OrderBy(member => member.MembershipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.UserId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Add(Dictionary<string, List<TeamMember>> index, string key, TeamMember member)
    {
        if (!index.TryGetValue(key, out List<TeamMember>? matches))
        {
            matches = [];
            index.Add(key, matches);
        }

        matches.Add(member);
    }
}

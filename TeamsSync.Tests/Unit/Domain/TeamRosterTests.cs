using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Domain;

public sealed class TeamRosterTests
{
    [Fact]
    public void FindByAddressOrName_メールアドレスの完全一致で見つかる()
    {
        TeamMember member = Member("membership-1", "user-1", "山田 太郎", "taro@example.com");
        TeamRoster roster = new([member]);

        IReadOnlyList<TeamMember> matches = roster.FindByAddressOrName("taro@example.com");

        Assert.Equal([member], matches);
    }

    [Fact]
    public void FindByAddressOrName_全角半角の表記ゆれを無視して氏名で一致する()
    {
        TeamMember member = Member("membership-1", "user-1", "山田　太郎", "taro@example.com");
        TeamRoster roster = new([member]);

        IReadOnlyList<TeamMember> matches = roster.FindByAddressOrName("山田 太郎");

        Assert.Equal([member], matches);
    }

    [Fact]
    public void FindByAddressOrName_同姓同名が複数いる場合は両方返す()
    {
        TeamMember first = Member("membership-1", "user-1", "山田 太郎", "taro1@example.com");
        TeamMember second = Member("membership-2", "user-2", "山田 太郎", "taro2@example.com");
        TeamRoster roster = new([first, second]);

        IReadOnlyList<TeamMember> matches = roster.FindByAddressOrName("山田太郎");

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void FindByAddressOrName_一致しない場合は空を返す()
    {
        TeamRoster roster = new([]);

        Assert.Empty(roster.FindByAddressOrName("nobody@example.com"));
    }

    [Fact]
    public void FindByUserId_一致するメンバーを返す()
    {
        TeamMember member = Member("membership-1", "user-1", "山田 太郎", "taro@example.com");
        TeamRoster roster = new([member]);

        Assert.Equal(member, roster.FindByUserId("user-1"));
    }

    [Fact]
    public void FindByUserId_一致しない場合はnullを返す()
    {
        TeamRoster roster = new([]);

        Assert.Null(roster.FindByUserId("missing"));
    }

    [Fact]
    public void NonOwnerCount_所有者を除いた件数を返す()
    {
        TeamRoster roster = new(
        [
            Member("m1", "u1", "Owner", "owner@example.com", true),
            Member("m2", "u2", "Member", "member@example.com")
        ]);

        Assert.Equal(1, roster.NonOwnerCount);
    }

    [Fact]
    public void BuildMembershipSnapshot_MembershipIdの昇順で並べる()
    {
        TeamRoster roster = new(
        [
            Member("m2", "u2", "B", "b@example.com"),
            Member("m1", "u1", "A", "a@example.com", true)
        ]);

        IReadOnlyList<TeamMembershipSnapshot> snapshot = roster.BuildMembershipSnapshot();

        Assert.Equal(["m1", "m2"], snapshot.Select(x => x.MembershipId));
    }

    private static TeamMember Member(string membershipId, string userId, string name, string email,
        bool owner = false)
    {
        return new TeamMember(membershipId, userId, name, email, owner);
    }
}

using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Domain;

public sealed class AddressResolverTests
{
    [Fact]
    public void TryResolveFromRoster_現メンバーに一致がなければnullを返す()
    {
        TeamRoster roster = new([Member("m1", "u1", "Other", "other@example.com")]);

        AddressResolution? resolution = AddressResolver.TryResolveFromRoster("missing@example.com", roster);

        Assert.Null(resolution);
    }

    [Fact]
    public void TryResolveFromRoster_メールアドレスが一致すればExistingSingleを返す()
    {
        TeamMember existing = Member("m1", "u1", "Existing", "existing@example.com");
        TeamRoster roster = new([existing]);

        AddressResolution? resolution = AddressResolver.TryResolveFromRoster("existing@example.com", roster);

        Assert.NotNull(resolution);
        Assert.Equal(ResolutionOutcome.ExistingSingle, resolution.Outcome);
        Assert.Equal(existing, resolution.ExistingMember);
    }

    [Fact]
    public void TryResolveFromRoster_同姓同名で複数の現メンバーに一致するとエラーになる()
    {
        TeamRoster roster = new(
        [
            Member("m1", "u1", "山田 太郎", "taro1@example.com"),
            Member("m2", "u2", "山田 太郎", "taro2@example.com")
        ]);

        AddressResolution? resolution = AddressResolver.TryResolveFromRoster("山田太郎", roster);

        Assert.NotNull(resolution);
        Assert.Equal(ResolutionOutcome.Error, resolution.Outcome);
        Assert.Equal(ChangeReason.AmbiguousCurrentMember, resolution.ErrorReason);
    }

    [Fact]
    public void ResolveFromDirectory_候補が0件ならUserNotFoundエラーになる()
    {
        AddressResolution resolution = AddressResolver.ResolveFromDirectory(
            "missing@example.com", [], new TeamRoster([]));

        Assert.Equal(ResolutionOutcome.Error, resolution.Outcome);
        Assert.Equal(ChangeReason.UserNotFound, resolution.ErrorReason);
    }

    [Fact]
    public void ResolveFromDirectory_候補が複数件ならAmbiguousDirectoryUserエラーになる()
    {
        DirectoryUser[] candidates =
        [
            new DirectoryUser("u1", "山田 太郎", "one@example.com", "one@example.com"),
            new DirectoryUser("u2", "山田　太郎", "two@example.com", "two@example.com")
        ];

        AddressResolution resolution = AddressResolver.ResolveFromDirectory(
            "山田太郎", candidates, new TeamRoster([]));

        Assert.Equal(ResolutionOutcome.Error, resolution.Outcome);
        Assert.Equal(ChangeReason.AmbiguousDirectoryUser, resolution.ErrorReason);
    }

    [Fact]
    public void ResolveFromDirectory_同一ユーザーIDの重複候補は1件として扱いNewUserになる()
    {
        DirectoryUser[] candidates =
        [
            new DirectoryUser("u1", "New", "new@example.com", "new@example.com"),
            new DirectoryUser("u1", "New", "new@example.com", "new@example.com")
        ];

        AddressResolution resolution = AddressResolver.ResolveFromDirectory(
            "new@example.com", candidates, new TeamRoster([]));

        Assert.Equal(ResolutionOutcome.NewUser, resolution.Outcome);
        Assert.Equal("u1", resolution.User!.Id);
    }

    [Fact]
    public void ResolveFromDirectory_一致したユーザーが現メンバーになければNewUserになる()
    {
        DirectoryUser user = new("u1", "New", "new@example.com", "new@example.com");

        AddressResolution resolution = AddressResolver.ResolveFromDirectory(
            "new@example.com", [user], new TeamRoster([]));

        Assert.Equal(ResolutionOutcome.NewUser, resolution.Outcome);
        Assert.Equal(user, resolution.User);
        Assert.Null(resolution.ExistingMember);
    }

    [Fact]
    public void ResolveFromDirectory_一致したユーザーが別アドレスで既に現メンバーならSameUserDifferentAddressになる()
    {
        TeamMember existing = Member("m1", "u1", "Existing", "primary@example.com");
        TeamRoster roster = new([existing]);
        DirectoryUser user = new("u1", "Existing", "primary@example.com", "primary@example.com");

        AddressResolution resolution = AddressResolver.ResolveFromDirectory("alias@example.com", [user], roster);

        Assert.Equal(ResolutionOutcome.SameUserDifferentAddress, resolution.Outcome);
        Assert.Equal(existing, resolution.ExistingMember);
        Assert.Equal(user, resolution.User);
    }

    [Fact]
    public void ResolveFromDirectory_ユーザーIDの一致は大文字小文字を区別しない()
    {
        TeamMember existing = Member("m1", "user-1", "Existing", "primary@example.com");
        TeamRoster roster = new([existing]);
        DirectoryUser user = new("USER-1", "Existing", "primary@example.com", "primary@example.com");

        AddressResolution resolution = AddressResolver.ResolveFromDirectory("alias@example.com", [user], roster);

        Assert.Equal(ResolutionOutcome.SameUserDifferentAddress, resolution.Outcome);
    }

    private static TeamMember Member(string membershipId, string userId, string name, string email,
        bool owner = false)
    {
        return new TeamMember(membershipId, userId, name, email, owner);
    }
}

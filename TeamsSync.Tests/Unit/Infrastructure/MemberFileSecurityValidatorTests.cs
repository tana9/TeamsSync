using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class MemberFileSecurityValidatorTests
{
    [Fact]
    public void ZIPエントリー数は上限まで許可して超過を拒否する()
    {
        MemberFileSecurityValidator.ArchiveEntryMetadata entry = new(1, 1);
        MemberFileSecurityValidator.ValidateArchiveMetadata(
            Enumerable.Repeat(entry, MemberFileSecurityValidator.MaximumArchiveEntries).ToList());

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            MemberFileSecurityValidator.ValidateArchiveMetadata(
                Enumerable.Repeat(entry, MemberFileSecurityValidator.MaximumArchiveEntries + 1).ToList()));

        Assert.Contains("エントリー数", exception.Message);
    }

    [Fact]
    public void 単一エントリーサイズは上限まで許可して超過を拒否する()
    {
        MemberFileSecurityValidator.ValidateArchiveMetadata(
        [
            new MemberFileSecurityValidator.ArchiveEntryMetadata(MemberFileSecurityValidator.MaximumArchiveEntryBytes,
                MemberFileSecurityValidator.MaximumArchiveEntryBytes)
        ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            MemberFileSecurityValidator.ValidateArchiveMetadata(
            [
                new MemberFileSecurityValidator.ArchiveEntryMetadata(
                    MemberFileSecurityValidator.MaximumArchiveEntryBytes + 1,
                    MemberFileSecurityValidator.MaximumArchiveEntryBytes + 1)
            ]));

        Assert.Contains("単一ファイル", exception.Message);
    }

    [Fact]
    public void 圧縮率は上限まで許可して超過を拒否する()
    {
        MemberFileSecurityValidator.ValidateArchiveMetadata(
        [
            new MemberFileSecurityValidator.ArchiveEntryMetadata(MemberFileSecurityValidator.MaximumCompressionRatio, 1)
        ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            MemberFileSecurityValidator.ValidateArchiveMetadata(
            [
                new MemberFileSecurityValidator.ArchiveEntryMetadata(
                    MemberFileSecurityValidator.MaximumCompressionRatio + 1, 1)
            ]));

        Assert.Contains("圧縮率", exception.Message);
    }

    [Fact]
    public void 展開後合計サイズは上限まで許可して超過を拒否する()
    {
        long fortyMb = 40L * 1024 * 1024;
        long twentyMb = 20L * 1024 * 1024;
        MemberFileSecurityValidator.ValidateArchiveMetadata(
        [
            new MemberFileSecurityValidator.ArchiveEntryMetadata(fortyMb, fortyMb),
            new MemberFileSecurityValidator.ArchiveEntryMetadata(fortyMb, fortyMb),
            new MemberFileSecurityValidator.ArchiveEntryMetadata(twentyMb, twentyMb)
        ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            MemberFileSecurityValidator.ValidateArchiveMetadata(
            [
                new MemberFileSecurityValidator.ArchiveEntryMetadata(fortyMb, fortyMb),
                new MemberFileSecurityValidator.ArchiveEntryMetadata(fortyMb, fortyMb),
                new MemberFileSecurityValidator.ArchiveEntryMetadata(twentyMb + 1, twentyMb + 1)
            ]));

        Assert.Contains("展開後サイズ", exception.Message);
    }
}
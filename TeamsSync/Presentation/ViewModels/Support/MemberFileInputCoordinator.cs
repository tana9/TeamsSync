using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>ファイル読込と貼り付け解析のバックグラウンド実行を調整する。</summary>
internal sealed class MemberFileInputCoordinator(IMemberListReader reader, IMemberTextParser parser)
{
    public Task<MemberListDocument> LoadAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() => reader.Read(path, cancellationToken), cancellationToken);
    }

    public Task<MemberListDocument> ParseAsync(string text, CancellationToken cancellationToken)
    {
        return Task.Run(() => parser.Parse(text, cancellationToken), cancellationToken);
    }
}
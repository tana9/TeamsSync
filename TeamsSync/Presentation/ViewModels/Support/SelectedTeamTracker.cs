using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels.Support;

// TeamMemberImportViewModel・TeamMemberExportViewModelが「選択中チームが変わったら進行中の
// 非同期操作をキャンセルする」という同じロジックを個別に持っていたため、ここへ集約する
/// <summary>
///     選択中チームの変更を検知し、変わっていれば進行中の非同期操作をキャンセルしたうえで更新する
/// </summary>
internal static class SelectedTeamTracker
{
    /// <summary>
    ///     選択中チーム(<paramref name="current" />)を<paramref name="team" />へ更新する。
    ///     チームIDが変わっていれば<paramref name="cancellation" />をキャンセルする
    /// </summary>
    public static void Update(ref TeamInfo? current, TeamInfo? team, RestartableCancellation cancellation)
    {
        if (current?.Id != team?.Id)
        {
            cancellation.Cancel();
        }

        current = team;
    }
}

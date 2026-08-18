using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>選択中チームの現在の全メンバー(所有者を含む)をCSVファイルへ出力する操作を管理する</summary>
public partial class TeamMemberExportViewModel : ObservableObject
{
    private readonly IMemberListExporter _exporter;
    private readonly RestartableCancellation _exportCancellation = new();
    private readonly IFilePickerService _filePicker;
    private readonly INotificationService _notifications;
    private readonly IUserPreferences _preferences;
    private readonly TeamsAccessService _teamsAccess;
    private readonly TimeProvider _timeProvider;
    private bool _enabled = true;
    private TeamInfo? _selectedTeam;

    /// <summary>コンストラクター</summary>
    public TeamMemberExportViewModel(TeamsAccessService teamsAccess, IFilePickerService filePicker,
        IMemberListExporter exporter, INotificationService notifications, IUserPreferences preferences,
        TimeProvider? timeProvider = null)
    {
        _teamsAccess = teamsAccess;
        _filePicker = filePicker;
        _exporter = exporter;
        _notifications = notifications;
        _preferences = preferences;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>メンバー一覧を取得・出力中かどうか</summary>
    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    /// <summary>現在選択されているチームを設定する。チームが変わった場合は進行中の出力をキャンセルする</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        SelectedTeamTracker.Update(ref _selectedTeam, team, _exportCancellation);
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>外部の状態に応じて、出力操作を有効/無効にする</summary>
    public void SetEnabled(bool value)
    {
        _enabled = value;
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択中チームの現在の全メンバーをGraph APIから取得し、選んだ保存先へCSVとして書き出す</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_selectedTeam is null)
        {
            return;
        }

        TeamInfo team = _selectedTeam;
        CancellationTokenSource cts = _exportCancellation.Begin();
        IsExporting = true;
        ExportCommand.NotifyCanExecuteChanged();
        try
        {
            IReadOnlyList<TeamMember> members = await _teamsAccess.GetTeamMembersAsync(team, cts.Token);
            if (members.Count == 0)
            {
                _notifications.ShowWarning("出力できるメンバーがいません", $"{team.DisplayName}にはメンバーがいません。");
                return;
            }

            string suggestedFileName = BuildSuggestedFileName(team.DisplayName);
            string? path = _filePicker.PickSaveLocation(suggestedFileName, _preferences.LastFolder);
            if (path is null)
            {
                return;
            }

            // CSV書き込みはファイルへの同期I/O(fsync含む)のため、UIスレッドをブロックしないよう
            // ファイル読込(MemberFileInputCoordinator)と同様にTask.Runでオフロードする
            await Task.Run(() => _exporter.Export(members, path), cts.Token);
            _notifications.ShowSuccess("メンバー一覧を出力しました",
                $"{members.Count}件を書き出しました: {Path.GetFileName(path)}");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 選択中チームが変わったことによるキャンセル。利用者への通知は不要
        }
        catch (Exception ex)
        {
            await _notifications.ShowErrorAsync(ex.Message, "メンバー一覧を出力できませんでした");
        }
        finally
        {
            IsExporting = false;
            _exportCancellation.End(cts);
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanExport()
    {
        return _enabled && !IsExporting && _selectedTeam is not null;
    }

    /// <summary>保存ダイアログへ提案するファイル名を、チーム名と日付から組み立てる</summary>
    private string BuildSuggestedFileName(string teamDisplayName)
    {
        string sanitizedTeamName = _exporter.SanitizeForFileName(teamDisplayName);
        return $"メンバー一覧_{sanitizedTeamName}_{_timeProvider.GetLocalNow():yyyyMMdd}.csv";
    }
}

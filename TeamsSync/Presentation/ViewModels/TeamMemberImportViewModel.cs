using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

// 以前は「実行可否」「既存入力の有無」を2つのFunc<bool>としてコンストラクターへ渡していたため、
// それぞれが何を意味するかは呼び出し元(MemberFileViewModel)のラムダを遡らないと分からなかった。
// 名前付きのインターフェースへ切り出すことで、このクラス単体を読むだけで依存の内容が分かるようにする
/// <summary>
///     Teamsからのメンバー取り込みが、呼び出し元(入力欄)の状態から見て実行可能かどうかを表す
/// </summary>
internal interface IMemberInputAvailability
{
    /// <summary>入力欄側の状態(ファイル読込中・解析中・テキスト貼り付けタブが選択されているかなど)から見た実行可否</summary>
    bool CanImportCurrentMembers { get; }

    /// <summary>置き換え確認が必要な既存入力があるかどうか</summary>
    bool HasExistingInput { get; }
}

/// <summary>選択中チームの現在の一般メンバーをGraph APIから取得し、入力欄へ反映する取り込み操作を管理する</summary>
public partial class TeamMemberImportViewModel : ObservableObject
{
    private readonly IMemberInputAvailability _availability;
    private readonly RestartableCancellation _importCancellation = new();
    private readonly MemberFileInputCoordinator _inputCoordinator;
    private readonly IMemberInputConfirmationService _inputConfirmation;
    private readonly INotificationService _notifications;
    private readonly TeamsAccessService _teamsAccess;
    private TeamInfo? _selectedTeam;

    /// <summary>
    ///     コンストラクター。<paramref name="availability" />には呼び出し元(<see cref="MemberFileViewModel" />)の
    ///     入力状態から見た実行可否・既存入力の有無を渡す。<paramref name="inputCoordinator" />は
    ///     <see cref="MemberFileViewModel" />が持つものを共有し、貼り付け入力と同じ経路
    ///     (UIスレッド外でのTask.Run実行)でテキストを解析する
    /// </summary>
    internal TeamMemberImportViewModel(TeamsAccessService teamsAccess, MemberFileInputCoordinator inputCoordinator,
        INotificationService notifications, IMemberInputConfirmationService inputConfirmation,
        IMemberInputAvailability availability)
    {
        _teamsAccess = teamsAccess;
        _inputCoordinator = inputCoordinator;
        _notifications = notifications;
        _inputConfirmation = inputConfirmation;
        _availability = availability;
    }

    /// <summary>現在のチームメンバーを取得中かどうか</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFetchingMembers))]
    public partial bool IsImportingMembers { get; set; }

    // 置き換え確認ダイアログの応答待ちの間は、Graph API通信自体は完了しているため取得中の
    // 進捗表示(ProgressRing)を出し続けると「まだ何か処理中」と誤解される。取り込みコマンドの
    // 実行不可・キャンセル可能といった他の状態はIsImportingMembers側に残したまま、
    // 進捗表示の可視性だけをこのプロパティで分けて制御する
    /// <summary>置き換え確認ダイアログの応答待ちかどうか</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFetchingMembers))]
    public partial bool IsAwaitingConfirmation { get; set; }

    /// <summary>取得中の進捗表示を出すべきかどうか(確認ダイアログの応答待ち中は除く)</summary>
    public bool IsFetchingMembers => IsImportingMembers && !IsAwaitingConfirmation;

    /// <summary>ステータスメッセージを通知するために発行される</summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>取り込みに成功したときに、対象チーム・整形済みテキスト・解析済み文書を渡して発行される</summary>
    public event Action<TeamInfo, string, MemberListDocument>? Imported;

    /// <summary>現在選択されているチームを設定する。チームが変わった場合は進行中の取り込みをキャンセルする</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        SelectedTeamTracker.Update(ref _selectedTeam, team, _importCancellation);
        NotifyCanExecuteChanged();
    }

    /// <summary>呼び出し元(入力欄)側の状態変化に応じて、取り込みコマンドの実行可否を再評価させる</summary>
    public void NotifyCanExecuteChanged()
    {
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
        CancelImportCurrentMembersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択中チームの一般メンバーを取得し、テキスト入力として反映する</summary>
    [RelayCommand(CanExecute = nameof(CanImportCurrentMembers))]
    private async Task ImportCurrentMembersAsync()
    {
        if (_selectedTeam is null)
        {
            return;
        }

        TeamInfo team = _selectedTeam;
        CancellationTokenSource cts = _importCancellation.Begin();
        IsImportingMembers = true;
        NotifyCanExecuteChanged();
        try
        {
            StatusChanged?.Invoke($"{team.DisplayName}の現在の一般メンバーを取得しています…", false);
            CurrentMemberImport? import = await _teamsAccess.ImportCurrentMembersAsync(team, cts.Token);
            if (import is null)
            {
                NotifyNoImportableMembers(team);
                return;
            }

            if (!await ConfirmReplacementAsync(team, import.Members.Count, cts.Token))
            {
                return;
            }

            if (_selectedTeam?.Id != team.Id)
            {
                StatusChanged?.Invoke("取り込み中に対象チームが変わったため、取得結果を破棄しました", false);
                return;
            }

            await ApplyImportedMembersAsync(import, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusChanged?.Invoke("現在のメンバーの取り込みをキャンセルしました。以前の入力は維持しています", false);
        }
        catch (Exception ex)
        {
            await _notifications.ShowErrorAsync(ex.Message, "現在のメンバーを取り込めませんでした");
            StatusChanged?.Invoke("現在のメンバーを取得できなかったため、以前の入力を維持しました", true);
        }
        finally
        {
            IsImportingMembers = false;
            _importCancellation.End(cts);
            NotifyCanExecuteChanged();
        }
    }

    private void NotifyNoImportableMembers(TeamInfo team)
    {
        _notifications.ShowWarning("取り込める一般メンバーがいません",
            $"{team.DisplayName}には、取得できる一般メンバーがいません。現在の入力は維持します。");
        StatusChanged?.Invoke("現在の入力を維持しました", false);
    }

    private async Task<bool> ConfirmReplacementAsync(TeamInfo team, int memberCount,
        CancellationToken cancellationToken)
    {
        if (!_availability.HasExistingInput)
        {
            return true;
        }

        IsAwaitingConfirmation = true;
        try
        {
            return await _inputConfirmation.ConfirmReplaceMemberInputAsync(
                team.DisplayName, memberCount, cancellationToken);
        }
        finally
        {
            IsAwaitingConfirmation = false;
        }
    }

    private async Task ApplyImportedMembersAsync(CurrentMemberImport import, CancellationToken cancellationToken)
    {
        MemberListDocument parsed = await _inputCoordinator.ParseAsync(import.Text, cancellationToken);
        MemberListDocument document = parsed with
        {
            FileName = $"Teamsから取り込み - {import.Team.DisplayName}",
            SourceName = $"Teamsから取り込み: {import.Team.DisplayName}"
        };
        Imported?.Invoke(import.Team, import.Text, document);
    }

    private bool CanImportCurrentMembers()
    {
        return !IsImportingMembers && _selectedTeam is not null && _availability.CanImportCurrentMembers;
    }

    /// <summary>実行中の現在メンバー取り込みをキャンセルする</summary>
    [RelayCommand(CanExecute = nameof(CanCancelImportCurrentMembers))]
    private void CancelImportCurrentMembers()
    {
        _importCancellation.Cancel();
    }

    private bool CanCancelImportCurrentMembers()
    {
        return IsImportingMembers && _importCancellation.IsActive;
    }
}
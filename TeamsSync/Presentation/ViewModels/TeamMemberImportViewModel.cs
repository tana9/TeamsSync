using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>選択中チームの現在の一般メンバーをGraph APIから取得し、入力欄へ反映する取り込み操作を管理する。</summary>
public partial class TeamMemberImportViewModel : ObservableObject
{
    private readonly ITeamsGateway _teamsGateway;
    private readonly IMemberTextParser _textParser;
    private readonly INotificationService _notifications;
    private readonly IMemberInputConfirmationService _inputConfirmation;
    private readonly Func<bool> _canImport;
    private readonly Func<bool> _hasExistingInput;
    private CancellationTokenSource? _importCancellation;
    private TeamInfo? _selectedTeam;

    /// <summary>現在のチームメンバーを取得中かどうか。</summary>
    [ObservableProperty] public partial bool IsImportingMembers { get; set; }

    /// <summary>
    /// コンストラクター。<paramref name="canImport"/>には入力欄側(ファイル読込中・解析中・
    /// テキスト貼り付けタブが選択されているかなど)の実行可否を、<paramref name="hasExistingInput"/>には
    /// 置き換え確認が必要な既存入力の有無を、それぞれ呼び出し元(<see cref="MemberFileViewModel"/>)から渡す。
    /// </summary>
    public TeamMemberImportViewModel(ITeamsGateway teamsGateway, IMemberTextParser textParser,
        INotificationService notifications, IMemberInputConfirmationService inputConfirmation,
        Func<bool> canImport, Func<bool> hasExistingInput)
    {
        _teamsGateway = teamsGateway;
        _textParser = textParser;
        _notifications = notifications;
        _inputConfirmation = inputConfirmation;
        _canImport = canImport;
        _hasExistingInput = hasExistingInput;
    }

    /// <summary>ステータスメッセージを通知するために発行される。</summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>取り込みに成功したときに、対象チーム・整形済みテキスト・解析済み文書を渡して発行される。</summary>
    public event Action<TeamInfo, string, MemberListDocument>? Imported;

    /// <summary>現在選択されているチームを設定する。チームが変わった場合は進行中の取り込みをキャンセルする。</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        if (_selectedTeam?.Id != team?.Id) _importCancellation?.Cancel();
        _selectedTeam = team;
        NotifyCanExecuteChanged();
    }

    /// <summary>呼び出し元(入力欄)側の状態変化に応じて、取り込みコマンドの実行可否を再評価させる。</summary>
    public void NotifyCanExecuteChanged()
    {
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
        CancelImportCurrentMembersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択中チームの一般メンバーを取得し、テキスト入力として反映する。</summary>
    [RelayCommand(CanExecute = nameof(CanImportCurrentMembers))]
    private async Task ImportCurrentMembersAsync()
    {
        if (_selectedTeam is null) return;
        var team = _selectedTeam;
        _importCancellation?.Cancel();
        _importCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _importCancellation = cts;
        IsImportingMembers = true;
        NotifyCanExecuteChanged();
        try
        {
            StatusChanged?.Invoke($"{team.DisplayName}の現在の一般メンバーを取得しています…", false);
            var members = await _teamsGateway.GetTeamMembersAsync(team.Id, cts.Token);
            var importedMembers = members
                .Where(member => !member.IsOwner && !string.IsNullOrWhiteSpace(member.Email))
                .GroupBy(member => member.Email.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(member => member.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (importedMembers.Count == 0)
            {
                _notifications.ShowWarning("取り込める一般メンバーがいません",
                    $"{team.DisplayName}には、メールアドレスを取得できる一般メンバーがいません。現在の入力は維持します。");
                StatusChanged?.Invoke("現在の入力を維持しました", false);
                return;
            }

            if (_hasExistingInput() &&
                !await _inputConfirmation.ConfirmReplaceMemberInputAsync(
                    team.DisplayName, importedMembers.Count, cts.Token))
                return;

            if (_selectedTeam?.Id != team.Id)
            {
                StatusChanged?.Invoke("取り込み中に対象チームが変わったため、取得結果を破棄しました", false);
                return;
            }

            var text = string.Join(Environment.NewLine, importedMembers.Select(FormatImportedMember));
            var document = _textParser.Parse(text, cts.Token) with
            {
                FileName = $"Teamsから取り込み - {team.DisplayName}",
                SourceName = $"Teamsから取り込み: {team.DisplayName}"
            };
            Imported?.Invoke(team, text, document);
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
            if (ReferenceEquals(_importCancellation, cts)) _importCancellation = null;
            cts.Dispose();
            NotifyCanExecuteChanged();
        }
    }

    private bool CanImportCurrentMembers()
    {
        return !IsImportingMembers && _selectedTeam is not null && _canImport();
    }

    /// <summary>実行中の現在メンバー取り込みをキャンセルする。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelImportCurrentMembers))]
    private void CancelImportCurrentMembers()
    {
        _importCancellation?.Cancel();
    }

    private bool CanCancelImportCurrentMembers() =>
        IsImportingMembers && _importCancellation is not null;

    /// <summary>取り込んだメンバーを、編集時に人物を識別できる`表示名 &lt;メールアドレス&gt;`形式へ整形する。</summary>
    private static string FormatImportedMember(TeamMember member)
    {
        var displayName = new string(member.DisplayName
            .Select(character => char.IsControl(character) || character is '<' or '>' ? ' ' : character)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? member.Email.Trim()
            : $"{displayName} <{member.Email.Trim()}>";
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
/// メンバーリストの入力(ファイル選択・ドラッグ&ドロップ・テキスト貼り付け)を管理し、
/// 解析結果の<see cref="MemberListDocument"/>を保持する。
/// </summary>
public partial class MemberFileViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly IMemberInputConfirmationService _inputConfirmation;
    private readonly INotificationService _notifications;
    private readonly IUserPreferences _preferences;
    private readonly IMemberListReader _reader;
    private readonly IMemberTextParser _textParser;
    private readonly ITeamsGateway _teamsGateway;
    private bool _enabled = true;
    private MemberListDocument? _fileDocument;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _importCancellation;
    private TeamInfo? _selectedTeam;

    /// <summary>ファイル入力の状態説明テキスト。</summary>
    [ObservableProperty] public partial string FileInfoText { get; set; } = "ファイルを選択するか、ここへドロップしてください";

    /// <summary>選択中のファイルパス。</summary>
    [ObservableProperty] public partial string FilePath { get; set; } = "";

    /// <summary>テキスト貼り付け入力の状態説明テキスト。</summary>
    [ObservableProperty] public partial string PasteInfoText { get; set; } = "1行につき1ユーザー（氏名またはメールアドレス）";

    /// <summary>貼り付けテキストを解析中かどうか。</summary>
    [ObservableProperty] public partial bool IsParsing { get; set; }

    /// <summary>ファイルを読み込み中かどうか。</summary>
    [ObservableProperty] public partial bool IsLoadingFile { get; set; }

    /// <summary>現在のチームメンバーを取得中かどうか。</summary>
    [ObservableProperty] public partial bool IsImportingMembers { get; set; }

    /// <summary>貼り付け入力の解析でエラーが発生したかどうか。</summary>
    [ObservableProperty] public partial bool IsPasteError { get; set; }

    /// <summary>テキスト貼り付け入力欄の内容。</summary>
    [ObservableProperty] public partial string PastedText { get; set; } = "";

    /// <summary>選択中の入力方法(0=ファイル、1=テキスト貼り付け)。</summary>
    [ObservableProperty] public partial int SelectedInputIndex { get; set; }

    /// <summary>コンストラクター。</summary>
    public MemberFileViewModel(IMemberListReader reader, IMemberTextParser textParser,
        IUserPreferences preferences, IFilePickerService filePicker, INotificationService notifications,
        ITeamsGateway teamsGateway, IMemberInputConfirmationService inputConfirmation)
    {
        _reader = reader;
        _textParser = textParser;
        _preferences = preferences;
        _filePicker = filePicker;
        _notifications = notifications;
        _teamsGateway = teamsGateway;
        _inputConfirmation = inputConfirmation;
    }

    /// <summary>現在有効なメンバーリスト文書(未確定の場合はnull)。</summary>
    public MemberListDocument? Document { get; private set; }

    /// <summary><see cref="Document"/>が変化したときに発行される。</summary>
    public event Action? DocumentChanged;

    /// <summary>ステータスメッセージを通知するために発行される。</summary>
    public event Action<string, bool>? StatusChanged;

    // ファイル読込またはテキスト解析が失敗したときに1回だけ発行する。
    // Viewはこれを受けて、選択中の入力方法(ファイル/貼り付け)に応じた修正対象へフォーカスを移す。
    /// <summary>入力エラー発生時、修正対象へフォーカスを移すために発行される。</summary>
    public event Action? InputFocusRequested;

    /// <summary>外部の状態に応じて、この画面の入力操作を有効/無効にする。</summary>
    public void SetEnabled(bool value)
    {
        _enabled = value;
        BrowseCommand.NotifyCanExecuteChanged();
        LoadDroppedFileCommand.NotifyCanExecuteChanged();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>現在選択されているチームを設定し、メンバー取り込みコマンドの状態を更新する。</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        if (_selectedTeam?.Id != team?.Id) _importCancellation?.Cancel();
        _selectedTeam = team;
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>ファイル読込関連コマンドのCanExecute状態を再評価させる。</summary>
    private void NotifyFileLoadCommandsCanExecuteChanged()
    {
        BrowseCommand.NotifyCanExecuteChanged();
        LoadDroppedFileCommand.NotifyCanExecuteChanged();
        CancelLoadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>入力方法の切り替えに応じて、有効な文書を切り替える。</summary>
    partial void OnSelectedInputIndexChanged(int value)
    {
        ApplySelectedInput();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>貼り付けテキストの変更を検知し、反映前の文書を無効化して状態を更新する。</summary>
    partial void OnPastedTextChanged(string value)
    {
        if (SelectedInputIndex != 1) return;
        Document = null;
        PasteInfoText = string.IsNullOrWhiteSpace(value)
            ? "1行につき1ユーザー（氏名またはメールアドレス）"
            : "内容が変更されました。「入力を反映」を押してください";
        IsPasteError = false;
        DocumentChanged?.Invoke();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
    }

    /// <summary>ファイル選択ダイアログを表示し、選ばれたファイルを読み込む。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task Browse()
    {
        var path = _filePicker.PickMemberFile(_preferences.LastFolder);
        if (path is not null) await Load(path);
    }

    /// <summary>ドラッグ&ドロップされたファイルを読み込む。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        SelectedInputIndex = 0;
        await Load(path);
    }

    private bool CanLoad()
    {
        return _enabled && !IsLoadingFile && !IsImportingMembers;
    }

    // CSV/Excelの解析は行数・列数によって時間がかかりうるため、貼り付け入力(ApplyPastedTextInputAsync)と同様に
    // UIスレッド外(Task.Run)で実行し、IsLoadingFileで処理中表示、CancellationTokenSourceでキャンセルを可能にする。
    /// <summary>
    /// 指定したパスのファイルをUIスレッド外で読み込み、成功時は<see cref="Document"/>と
    /// 前回フォルダー設定を更新する。失敗・キャンセル時は状態を適切にロールバックする。
    /// </summary>
    private async Task Load(string path)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCancellation = cts;
        IsLoadingFile = true;
        FilePath = path;
        FileInfoText = "ファイルを読み込んでいます…";
        NotifyFileLoadCommandsCanExecuteChanged();
        try
        {
            var document = await Task.Run(() => _reader.Read(path, cts.Token), cts.Token);
            _fileDocument = document;
            Document = document;
            FilePath = path;
            FileInfoText =
                $"{Document.Addresses.Count}件 • {Document.SourceName} • 列: {Document.DetectedColumn} • 更新: {Document.LastModified:g}";
            _preferences.LastFolder = Path.GetDirectoryName(path);
            try
            {
                _preferences.Save();
            }
            catch (Exception ex)
            {
                _notifications.ShowWarning("設定を保存できませんでした",
                    $"メンバーリストは読み込み済みです。前回のフォルダーだけ保存できませんでした。{Environment.NewLine}{ex.Message}");
            }
            NotifyDocumentChanged();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 利用者によるキャンセル。以前の文書は変更せず、その旨だけを表示する
            FileInfoText = "読込をキャンセルしました";
        }
        catch (Exception ex)
        {
            _fileDocument = null;
            Document = null;
            FilePath = path;
            FileInfoText = $"読込に失敗しました: {Path.GetFileName(path)}";
            DocumentChanged?.Invoke();
            StatusChanged?.Invoke("ファイルを読み込めなかったため、以前の同期差分を無効化しました", true);
            // ダイアログが閉じる前にフォーカスを移動すると裏でフォーカスが奪われてしまうため、
            // ダイアログを閉じた後(onClosed)にInputFocusRequestedを発火する。
            await _notifications.ShowErrorAsync(ex.Message, "ファイル読込エラー",
                () => InputFocusRequested?.Invoke());
        }
        finally
        {
            IsLoadingFile = false;
            if (ReferenceEquals(_loadCancellation, cts))
                _loadCancellation = null;
            cts.Dispose();
            NotifyFileLoadCommandsCanExecuteChanged();
        }
    }

    /// <summary>実行中のファイル読込をキャンセルする。</summary>
    [RelayCommand(CanExecute = nameof(CanCancelLoad))]
    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
    }

    private bool CanCancelLoad()
    {
        return IsLoadingFile && _loadCancellation is not null;
    }

    /// <summary>選択中の入力方法に応じて<see cref="Document"/>をファイル文書または未反映状態へ切り替える。</summary>
    private void ApplySelectedInput()
    {
        if (SelectedInputIndex == 0)
        {
            Document = _fileDocument;
            NotifyDocumentChanged();
        }
        else
        {
            Document = null;
            PasteInfoText = string.IsNullOrWhiteSpace(PastedText)
                ? "1行につき1ユーザー（氏名またはメールアドレス）"
                : "「入力を反映」を押してください";
            DocumentChanged?.Invoke();
        }
    }

    /// <summary>貼り付けテキストをUIスレッド外で解析し、成功時は<see cref="Document"/>へ反映する。</summary>
    [RelayCommand(CanExecute = nameof(CanApplyPastedText))]
    private async Task ApplyPastedTextInputAsync()
    {
        if (string.IsNullOrWhiteSpace(PastedText)) return;
        var text = PastedText;
        IsParsing = true;
        IsPasteError = false;
        PasteInfoText = "入力内容を解析しています…";
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        try
        {
            var document = await Task.Run(() => _textParser.Parse(text));
            if (!string.Equals(text, PastedText, StringComparison.Ordinal))
            {
                PasteInfoText = "解析中に内容が変更されました。「入力を反映」を押してください";
                return;
            }

            Document = document;
            var entered = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries).Length;
            var duplicates = Math.Max(0, entered - document.Addresses.Count);
            PasteInfoText = $"{document.Addresses.Count}件 • 重複{duplicates}件を除外";
            NotifyDocumentChanged();
        }
        catch (Exception ex)
        {
            Document = null;
            IsPasteError = true;
            PasteInfoText = ex.Message;
            DocumentChanged?.Invoke();
            StatusChanged?.Invoke($"貼り付け入力を確認してください: {ex.Message}", true);
            InputFocusRequested?.Invoke();
        }
        finally
        {
            IsParsing = false;
            ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApplyPastedText()
    {
        return _enabled && !IsParsing && !IsImportingMembers && SelectedInputIndex == 1 &&
               !string.IsNullOrWhiteSpace(PastedText);
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
        ImportCurrentMembersCommand.NotifyCanExecuteChanged();
        CancelImportCurrentMembersCommand.NotifyCanExecuteChanged();
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

            var hasExistingInput = Document is not null || _fileDocument is not null ||
                                   !string.IsNullOrWhiteSpace(PastedText);
            if (hasExistingInput &&
                !await _inputConfirmation.ConfirmReplaceMemberInputAsync(
                    team.DisplayName, importedMembers.Count, cts.Token))
                return;

            if (_selectedTeam?.Id != team.Id)
            {
                StatusChanged?.Invoke("取り込み中に対象チームが変わったため、取得結果を破棄しました", false);
                return;
            }

            var text = string.Join(Environment.NewLine, importedMembers.Select(FormatImportedMember));
            var document = _textParser.Parse(text) with
            {
                FileName = $"Teamsから取り込み - {team.DisplayName}",
                SourceName = $"Teamsから取り込み: {team.DisplayName}"
            };
            SelectedInputIndex = 1;
            PastedText = text;
            Document = document;
            IsPasteError = false;
            PasteInfoText = $"{document.Addresses.Count}件 • Teamsから取り込み: {team.DisplayName}";
            NotifyDocumentChanged();
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
            ImportCurrentMembersCommand.NotifyCanExecuteChanged();
            CancelImportCurrentMembersCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImportCurrentMembers()
    {
        return _enabled && !IsLoadingFile && !IsParsing && !IsImportingMembers &&
               SelectedInputIndex == 1 && _selectedTeam is not null;
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

    /// <summary><see cref="DocumentChanged"/>を発行し、成功時は件数をステータスとして通知する。</summary>
    private void NotifyDocumentChanged()
    {
        DocumentChanged?.Invoke();
        if (Document is not null)
            StatusChanged?.Invoke($"{Document.Addresses.Count}件の一意な氏名／メールアドレスを読み込みました", false);
    }
}

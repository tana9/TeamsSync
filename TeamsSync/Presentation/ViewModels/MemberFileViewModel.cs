using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     メンバーリストの入力(ファイル選択・ドラッグ&ドロップ・テキスト貼り付け)を管理し、
///     解析結果の<see cref="MemberListDocument" />を保持する。Teamsからの現在メンバー取り込みは
///     <see cref="Import" />(<see cref="TeamMemberImportViewModel" />)へ委譲する。
/// </summary>
public partial class MemberFileViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly IMemberInputConfirmationService _inputConfirmation;
    private readonly INotificationService _notifications;
    private readonly IUserPreferences _preferences;
    private readonly IMemberListReader _reader;
    private readonly IMemberTextParser _textParser;
    private bool _enabled = true;
    private MemberListDocument? _fileDocument;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _parseCancellation;

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
        _inputConfirmation = inputConfirmation;
        Import = new TeamMemberImportViewModel(teamsGateway, textParser, notifications, inputConfirmation,
            () => _enabled && !IsLoadingFile && !IsParsing && SelectedInputIndex == 1,
            () => Document is not null || _fileDocument is not null || !string.IsNullOrWhiteSpace(PastedText));
        Import.Imported += OnMembersImported;
        Import.StatusChanged += (message, isError) => StatusChanged?.Invoke(message, isError);
        DocumentChanged += () => OnPropertyChanged(nameof(HasUnappliedPastedText));
    }

    /// <summary>ファイル入力の状態説明テキスト。</summary>
    [ObservableProperty]
    public partial string FileInfoText { get; set; } = "ファイルを選択するか、ここへドロップしてください";

    /// <summary>選択中のファイルパス。</summary>
    [ObservableProperty]
    public partial string FilePath { get; set; } = "";

    /// <summary>テキスト貼り付け入力の状態説明テキスト。</summary>
    [ObservableProperty]
    public partial string PasteInfoText { get; set; } = "1行につき1ユーザー（氏名またはメールアドレス）";

    /// <summary>貼り付けテキストを解析中かどうか。</summary>
    [ObservableProperty]
    public partial bool IsParsing { get; set; }

    /// <summary>ファイルを読み込み中かどうか。</summary>
    [ObservableProperty]
    public partial bool IsLoadingFile { get; set; }

    /// <summary>貼り付け入力の解析でエラーが発生したかどうか。</summary>
    [ObservableProperty]
    public partial bool IsPasteError { get; set; }

    /// <summary>テキスト貼り付け入力欄の内容。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnappliedPastedText))]
    public partial string PastedText { get; set; } = "";

    /// <summary>選択中の入力方法(0=ファイル、1=テキスト貼り付け)。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnappliedPastedText))]
    public partial int SelectedInputIndex { get; set; }

    /// <summary>Teamsからの現在メンバー取り込みを管理するViewModel。</summary>
    public TeamMemberImportViewModel Import { get; }

    /// <summary>現在有効なメンバーリスト文書(未確定の場合はnull)。</summary>
    public MemberListDocument? Document { get; private set; }

    /// <summary>テキスト貼り付け入力に、まだ「入力を反映」していない変更があるかどうか。</summary>
    public bool HasUnappliedPastedText =>
        SelectedInputIndex == 1 && Document is null && !string.IsNullOrWhiteSpace(PastedText);

    /// <summary><see cref="Document" />が変化したときに発行される。</summary>
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
        CopyFileContentToTextCommand.NotifyCanExecuteChanged();
        Import.NotifyCanExecuteChanged();
    }

    /// <summary>現在選択されているチームを設定し、メンバー取り込みコマンドの状態を更新する。</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        Import.SetSelectedTeam(team);
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
        CopyFileContentToTextCommand.NotifyCanExecuteChanged();
        Import.NotifyCanExecuteChanged();
    }

    /// <summary>貼り付けテキストの変更を検知し、反映前の文書を無効化して状態を更新する。</summary>
    partial void OnPastedTextChanged(string value)
    {
        if (SelectedInputIndex != 1)
        {
            return;
        }

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
        string? path = _filePicker.PickMemberFile(_preferences.LastFolder);
        if (path is not null)
        {
            await Load(path);
        }
    }

    /// <summary>ドラッグ&ドロップされたファイルを読み込む。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedInputIndex = 0;
        await Load(path);
    }

    private bool CanLoad()
    {
        return _enabled && !IsLoadingFile && !Import.IsImportingMembers;
    }

    // CSV/Excelの解析は行数・列数によって時間がかかりうるため、貼り付け入力(ApplyPastedTextInputAsync)と同様に
    // UIスレッド外(Task.Run)で実行し、IsLoadingFileで処理中表示、CancellationTokenSourceでキャンセルを可能にする。
    /// <summary>
    ///     指定したパスのファイルをUIスレッド外で読み込み、成功時は<see cref="Document" />と
    ///     前回フォルダー設定を更新する。失敗・キャンセル時は状態を適切にロールバックする。
    /// </summary>
    private async Task Load(string path)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        CancellationTokenSource cts = new();
        _loadCancellation = cts;
        IsLoadingFile = true;
        FilePath = path;
        FileInfoText = "ファイルを読み込んでいます…";
        NotifyFileLoadCommandsCanExecuteChanged();
        try
        {
            MemberListDocument document = await Task.Run(() => _reader.Read(path, cts.Token), cts.Token);
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
            CopyFileContentToTextCommand.NotifyCanExecuteChanged();
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
            CopyFileContentToTextCommand.NotifyCanExecuteChanged();
            StatusChanged?.Invoke("ファイルを読み込めなかったため、以前の同期差分を無効化しました", true);
            // Snackbarはフォーカスを奪わないため、通知表示後のコールバックで修正対象へ戻す。
            await _notifications.ShowErrorAsync(ex.Message, "ファイル読込エラー",
                () => InputFocusRequested?.Invoke());
        }
        finally
        {
            IsLoadingFile = false;
            if (ReferenceEquals(_loadCancellation, cts))
            {
                _loadCancellation = null;
            }

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

    /// <summary>ファイルから読み取った識別子を1行1件のテキストへコピーし、編集できる状態にする。</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFileContentToText))]
    private async Task CopyFileContentToTextAsync()
    {
        MemberListDocument? fileDocument = _fileDocument;
        if (fileDocument is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(PastedText) &&
            !await _inputConfirmation.ConfirmReplaceTextWithFileContentAsync(
                fileDocument.SourceName, fileDocument.Addresses.Count))
        {
            return;
        }

        string text = string.Join(Environment.NewLine, fileDocument.Addresses);
        SelectedInputIndex = 1;
        PastedText = text;
        PasteInfoText = "ファイル内容をコピーしました。編集後に「入力を反映」を押してください（元ファイルは変更されません）";
        StatusChanged?.Invoke("ファイル内容をテキストへコピーしました。編集後に入力を反映してください", false);
    }

    private bool CanCopyFileContentToText()
    {
        return _enabled && !IsLoadingFile && !IsParsing && !Import.IsImportingMembers && _fileDocument is not null;
    }

    /// <summary>選択中の入力方法に応じて<see cref="Document" />をファイル文書または未反映状態へ切り替える。</summary>
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

    /// <summary>貼り付けテキストをUIスレッド外で解析し、成功時は<see cref="Document" />へ反映する。</summary>
    [RelayCommand(CanExecute = nameof(CanApplyPastedText))]
    private async Task ApplyPastedTextInputAsync()
    {
        if (string.IsNullOrWhiteSpace(PastedText))
        {
            return;
        }

        _parseCancellation?.Cancel();
        _parseCancellation?.Dispose();
        CancellationTokenSource cts = new();
        _parseCancellation = cts;
        string text = PastedText;
        IsParsing = true;
        IsPasteError = false;
        PasteInfoText = "入力内容を解析しています…";
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        CancelParsingCommand.NotifyCanExecuteChanged();
        try
        {
            MemberListDocument document = await Task.Run(() => _textParser.Parse(text, cts.Token), cts.Token);
            if (!string.Equals(text, PastedText, StringComparison.Ordinal))
            {
                PasteInfoText = "解析中に内容が変更されました。「入力を反映」を押してください";
                return;
            }

            Document = document;
            int entered = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries).Length;
            int duplicates = Math.Max(0, entered - document.Addresses.Count);
            PasteInfoText = $"{document.Addresses.Count}件 • 重複{duplicates}件を除外";
            NotifyDocumentChanged();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            PasteInfoText = "入力内容の解析をキャンセルしました";
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
            CancelParsingCommand.NotifyCanExecuteChanged();
            if (ReferenceEquals(_parseCancellation, cts))
            {
                _parseCancellation = null;
            }

            cts.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelParsing))]
    private void CancelParsing()
    {
        _parseCancellation?.Cancel();
    }

    private bool CanCancelParsing()
    {
        return IsParsing && _parseCancellation is not null;
    }

    private bool CanApplyPastedText()
    {
        return _enabled && !IsParsing && !Import.IsImportingMembers && SelectedInputIndex == 1 &&
               !string.IsNullOrWhiteSpace(PastedText);
    }

    /// <summary>Teamsからの取り込み結果を入力欄へ反映し、テキスト貼り付けタブへ切り替える。</summary>
    private void OnMembersImported(TeamInfo team, string text, MemberListDocument document)
    {
        SelectedInputIndex = 1;
        PastedText = text;
        Document = document;
        IsPasteError = false;
        PasteInfoText = $"{document.Addresses.Count}件 • Teamsから取り込み: {team.DisplayName}";
        NotifyDocumentChanged();
    }

    /// <summary><see cref="DocumentChanged" />を発行し、成功時は件数をステータスとして通知する。</summary>
    private void NotifyDocumentChanged()
    {
        DocumentChanged?.Invoke();
        if (Document is not null)
        {
            StatusChanged?.Invoke($"{Document.Addresses.Count}件の一意な氏名／メールアドレスを読み込みました", false);
        }
    }
}

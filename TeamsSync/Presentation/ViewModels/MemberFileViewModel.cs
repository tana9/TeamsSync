using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     メンバーリストの入力(ファイル選択・ドラッグ&amp;ドロップ・テキスト貼り付け)を管理し、
///     解析結果の<see cref="MemberListDocument" />を保持する。Teamsからの現在メンバー取り込みは
///     <see cref="Import" />(<see cref="TeamMemberImportViewModel" />)へ、現在メンバーのCSV出力は
///     <see cref="Export" />(<see cref="TeamMemberExportViewModel" />)へそれぞれ委譲する
/// </summary>
public partial class MemberFileViewModel : ObservableObject, IMemberInputAvailability
{
    private readonly IFilePickerService _filePicker;
    private readonly IMemberInputConfirmationService _inputConfirmation;
    private readonly RestartableCancellation _loadCancellation = new();
    private readonly INotificationService _notifications;
    private readonly RestartableCancellation _parseCancellation = new();
    private readonly IUserPreferences _preferences;
    private readonly MemberFileInputCoordinator _inputCoordinator;
    private readonly ViewModelUiEvents _uiEvents = new();
    private bool _enabled = true;
    private readonly MemberInputDocumentState _documents = new();

    /// <summary>コンストラクター</summary>
    public MemberFileViewModel(IMemberListReader reader, IMemberTextParser textParser,
        IUserPreferences preferences, IFilePickerService filePicker, INotificationService notifications,
        TeamsAccessService teamsAccess, IMemberInputConfirmationService inputConfirmation,
        IMemberListExporter exporter)
    {
        _inputCoordinator = new MemberFileInputCoordinator(reader, textParser);
        _preferences = preferences;
        _filePicker = filePicker;
        _notifications = notifications;
        _inputConfirmation = inputConfirmation;
        Import = new TeamMemberImportViewModel(teamsAccess, _inputCoordinator, notifications, inputConfirmation, this);
        Export = new TeamMemberExportViewModel(teamsAccess, filePicker, exporter, notifications, preferences);
        Import.Imported += OnMembersImported;
        Import.StatusChanged += (message, isError) => _uiEvents.Status(message, isError);
        Import.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TeamMemberImportViewModel.IsImportingMembers))
            {
                NotifyCommandStates();
            }
        };
        // FileLoad/PasteInputはSyncWorkspaceViewModelのPreview/Executionと同様、XAMLから
        // 直接バインドされる公開の子状態。ここではコマンドの実行可否を再評価する必要があるため購読する
        FileLoad.PropertyChanged += (_, _) => NotifyCommandStates();
        PasteInput.PropertyChanged += (_, _) => NotifyCommandStates();
        DocumentChanged += () => OnPropertyChanged(nameof(HasUnappliedPastedText));
        DocumentChanged += () => OnPropertyChanged(nameof(ShowCollapsedSummary));
        DocumentChanged += () => OnPropertyChanged(nameof(ShowFullInput));
        DocumentChanged += () => OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    ///     利用者が「変更」を押して、折りたたみ済みの入力要約を再びファイル/テキスト入力の
    ///     編集画面へ戻しているかどうか。新しい入力が確定するたびに<see cref="NotifyDocumentChanged" />で
    ///     falseへ戻し、次に文書が確定すると再び折りたたみ表示になる
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCollapsedSummary))]
    [NotifyPropertyChangedFor(nameof(ShowFullInput))]
    public partial bool IsEditingInput { get; set; }

    /// <summary>入力確定済みで編集中でないとき、1行の要約表示に折りたたむかどうか</summary>
    public bool ShowCollapsedSummary => Document is not null && !IsEditingInput;

    /// <summary>ファイル/テキスト入力のタブUIをフル表示するかどうか(<see cref="ShowCollapsedSummary" />の否定)</summary>
    public bool ShowFullInput => !ShowCollapsedSummary;

    /// <summary>折りたたみ表示に使う、入力元・検出列・件数の要約テキスト</summary>
    public string SummaryText => SyncWorkspaceTextFormatter.BuildInputSummary(Document);

    /// <summary>ファイル入力の読込中状態と表示状態</summary>
    public MemberFileLoadState FileLoad { get; } = new();

    /// <summary>テキスト貼り付け入力の解析状態と表示状態</summary>
    public MemberPasteInputState PasteInput { get; } = new();

    /// <summary>テキスト貼り付け入力欄の内容</summary>
    public string PastedText
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnappliedPastedText));
            OnPastedTextChanged(value);
        }
    } = "";

    /// <summary>選択中の入力方法(0=ファイル、1=テキスト貼り付け)</summary>
    public int SelectedInputIndex
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnappliedPastedText));
            OnSelectedInputIndexChanged(value);
        }
    }

    /// <summary>Teamsからの現在メンバー取り込みを管理するViewModel</summary>
    public TeamMemberImportViewModel Import { get; }

    /// <summary>選択中チームの現在メンバーのCSV出力を管理するViewModel</summary>
    public TeamMemberExportViewModel Export { get; }

    /// <summary>現在有効なメンバーリスト文書(未確定の場合はnull)</summary>
    public MemberListDocument? Document { get; private set; }

    /// <summary>テキスト貼り付け入力に、まだ「入力を反映」していない変更があるかどうか</summary>
    public bool HasUnappliedPastedText =>
        SelectedInputIndex == (int)MemberInputMethod.Paste && Document is null &&
        !string.IsNullOrWhiteSpace(PastedText);

    /// <summary><see cref="Document" />が変化したときに発行される</summary>
    public event Action? DocumentChanged;

    /// <summary>ステータスメッセージを通知するために発行される</summary>
    public event Action<string, bool>? StatusChanged
    {
        add => _uiEvents.StatusChanged += value;
        remove => _uiEvents.StatusChanged -= value;
    }

    // ファイル読込またはテキスト解析が失敗したとき、またはファイル内容をテキストへコピーして
    // 入力方法を切り替えたときに発行する。Viewはこれを受けて、選択中の入力方法(ファイル/貼り付け)
    // に応じた対象(ファイル選択ボタン、または貼り付けテキスト欄)へフォーカスを移す
    /// <summary>入力エラー発生時や入力方法の切り替え後、対象へフォーカスを移すために発行される</summary>
    public event Action? InputFocusRequested
    {
        add => _uiEvents.FocusRequested += value;
        remove => _uiEvents.FocusRequested -= value;
    }

    /// <summary>外部の状態に応じて、この画面の入力操作を有効/無効にする</summary>
    public void SetEnabled(bool value)
    {
        _enabled = value;
        NotifyCommandStates();
        Export.SetEnabled(value);
    }

    /// <summary>現在選択されているチームを設定し、メンバー取り込み・出力コマンドの状態を更新する</summary>
    public void SetSelectedTeam(TeamInfo? team)
    {
        Import.SetSelectedTeam(team);
        Export.SetSelectedTeam(team);
    }

    // IMemberInputAvailabilityはTeamMemberImportViewModel専用の内部依存であり、XAMLバインディング等の
    // 一般公開プロパティと混同されないよう、明示的インターフェース実装としてIntelliSense上も分離している
    bool IMemberInputAvailability.CanImportCurrentMembers =>
        _enabled && !FileLoad.IsLoading && !PasteInput.IsParsing &&
        SelectedInputIndex == (int)MemberInputMethod.Paste;

    bool IMemberInputAvailability.HasExistingInput =>
        Document is not null || _documents.FileDocument is not null || !string.IsNullOrWhiteSpace(PastedText);

    /// <summary>入力状態に依存するすべてのコマンドの実行可否を再評価する</summary>
    private void NotifyCommandStates()
    {
        BrowseCommand.NotifyCanExecuteChanged();
        LoadDroppedFileCommand.NotifyCanExecuteChanged();
        CancelLoadCommand.NotifyCanExecuteChanged();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
        CancelParsingCommand.NotifyCanExecuteChanged();
        CopyFileContentToTextCommand.NotifyCanExecuteChanged();
        Import.NotifyCanExecuteChanged();
    }

    /// <summary>入力方法の切り替えに応じて、有効な文書を切り替える</summary>
    private void OnSelectedInputIndexChanged(int value)
    {
        MemberListDocument? document = _documents.Select(value, PastedText, out string? pasteInfo);
        Document = document;
        if (pasteInfo is not null)
        {
            // pasteInfoが返るのは反映済みの貼り付け文書がない場合だけなので、以前のエラー表示が
            // 案内文へ置き換わった後もHasErrorが残って赤字強調のままにならないようにする
            PasteInput.InfoText = pasteInfo;
            PasteInput.HasError = false;
        }

        NotifyDocumentChanged();
        NotifyCommandStates();
    }

    /// <summary>貼り付けテキストの変更を検知し、反映前の文書を無効化して状態を更新する</summary>
    private void OnPastedTextChanged(string value)
    {
        if (SelectedInputIndex != (int)MemberInputMethod.Paste)
        {
            return;
        }

        Document = null;
        _documents.InvalidatePastedDocument();
        PasteInput.InfoText = string.IsNullOrWhiteSpace(value)
            ? MemberPasteInputState.DefaultInfoText
            : "内容が変更されました。「入力を反映」を押してください";
        PasteInput.HasError = false;
        DocumentChanged?.Invoke();
        NotifyCommandStates();
    }

    /// <summary>ファイル選択ダイアログを表示し、選ばれたファイルを読み込む</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task Browse()
    {
        string? path = _filePicker.PickMemberFile(_preferences.LastFolder);
        if (path is not null)
        {
            SelectedInputIndex = (int)MemberInputMethod.File;
            await Load(path);
        }
    }

    /// <summary>ドラッグ&amp;ドロップされたファイルを読み込む</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedInputIndex = (int)MemberInputMethod.File;
        await Load(path);
    }

    private bool CanLoad()
    {
        return _enabled && !FileLoad.IsLoading && !Import.IsImportingMembers;
    }

    // CSV/Excelの解析は行数・列数によって時間がかかりうるため、貼り付け入力(ApplyPastedTextInputAsync)と同様に
    // UIスレッド外(Task.Run)で実行し、FileLoad.IsLoadingで処理中表示、CancellationTokenSourceでキャンセルを可能にする
    /// <summary>
    ///     指定したパスのファイルをUIスレッド外で読み込み、成功時は<see cref="Document" />と
    ///     前回フォルダー設定を更新する。失敗・キャンセル時は状態を適切にロールバックする
    /// </summary>
    private async Task Load(string path)
    {
        CancellationTokenSource cts = _loadCancellation.Begin();
        FileLoad.IsLoading = true;
        FileLoad.Path = path;
        FileLoad.InfoText = "ファイルを読み込んでいます…";
        NotifyCommandStates();
        try
        {
            MemberListDocument document = await _inputCoordinator.LoadAsync(path, cts.Token);
            _documents.SetFileDocument(document);
            Document = document;
            FileLoad.Path = path;
            FileLoad.InfoText =
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
            NotifyCommandStates();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 利用者によるキャンセル。以前の文書は変更せず、その旨だけを表示する
            FileLoad.InfoText = "読込をキャンセルしました";
        }
        catch (Exception ex)
        {
            _documents.SetFileDocument(null);
            Document = null;
            FileLoad.Path = path;
            FileLoad.InfoText = $"読込に失敗しました: {Path.GetFileName(path)}";
            DocumentChanged?.Invoke();
            NotifyCommandStates();
            _uiEvents.Status("ファイルを読み込めなかったため、以前の同期差分を無効化しました", true);
            // Snackbarはフォーカスを奪わないため、通知表示後のコールバックで修正対象へ戻す
            await _notifications.ShowErrorAsync(ex.Message, "ファイル読込エラー",
                _uiEvents.RequestFocus);
        }
        finally
        {
            FileLoad.IsLoading = false;
            _loadCancellation.End(cts);
            NotifyCommandStates();
        }
    }

    /// <summary>実行中のファイル読込をキャンセルする</summary>
    [RelayCommand(CanExecute = nameof(CanCancelLoad))]
    private void CancelLoad()
    {
        _loadCancellation.Cancel();
    }

    private bool CanCancelLoad()
    {
        return FileLoad.IsLoading && _loadCancellation.IsActive;
    }

    /// <summary>ファイルから読み取った識別子を1行1件のテキストへコピーし、編集できる状態にする</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFileContentToText))]
    private async Task CopyFileContentToTextAsync()
    {
        MemberListDocument? fileDocument = _documents.FileDocument;
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
        SelectedInputIndex = (int)MemberInputMethod.Paste;
        PastedText = text;
        PasteInput.InfoText = "ファイル内容をコピーしました。編集後に「入力を反映」を押してください（元ファイルは変更されません）";
        _uiEvents.Status("ファイル内容をテキストへコピーしました。編集後に入力を反映してください", false);
        // 確認ダイアログのShowRestoringFocusAsyncは表示前にフォーカスしていた「ファイル内容をコピーして
        // 編集」ボタンへの復元を試みるが、直後にタブを「テキスト貼り付け」へ切り替えるため、そのボタンは
        // ビジュアルツリーから外れており復元に失敗する。切替後に有効な貼り付けテキスト欄へ明示的に移す
        _uiEvents.RequestFocus();
    }

    private bool CanCopyFileContentToText()
    {
        return _enabled && !FileLoad.IsLoading && !PasteInput.IsParsing && !Import.IsImportingMembers &&
               _documents.FileDocument is not null;
    }

    /// <summary>貼り付けテキストをUIスレッド外で解析し、成功時は<see cref="Document" />へ反映する</summary>
    [RelayCommand(CanExecute = nameof(CanApplyPastedText))]
    private async Task ApplyPastedTextInputAsync()
    {
        if (string.IsNullOrWhiteSpace(PastedText))
        {
            return;
        }

        CancellationTokenSource cts = _parseCancellation.Begin();
        string text = PastedText;
        PasteInput.IsParsing = true;
        PasteInput.HasError = false;
        PasteInput.InfoText = "入力内容を解析しています…";
        NotifyCommandStates();
        try
        {
            MemberListDocument document = await _inputCoordinator.ParseAsync(text, cts.Token);
            if (!string.Equals(text, PastedText, StringComparison.Ordinal))
            {
                PasteInput.InfoText = "解析中に内容が変更されました。「入力を反映」を押してください";
                return;
            }

            _documents.SetPastedDocument(document);
            Document = document;
            int entered = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries).Length;
            int duplicates = Math.Max(0, entered - document.Addresses.Count);
            PasteInput.InfoText = $"{document.Addresses.Count}件 • 重複{duplicates}件を除外";
            NotifyDocumentChanged();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            PasteInput.InfoText = "入力内容の解析をキャンセルしました";
        }
        catch (Exception ex)
        {
            _documents.InvalidatePastedDocument();
            Document = null;
            PasteInput.HasError = true;
            PasteInput.InfoText = ex.Message;
            DocumentChanged?.Invoke();
            _uiEvents.Status($"貼り付け入力を確認してください: {ex.Message}", true);
            _uiEvents.RequestFocus();
        }
        finally
        {
            PasteInput.IsParsing = false;
            NotifyCommandStates();
            _parseCancellation.End(cts);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelParsing))]
    private void CancelParsing()
    {
        _parseCancellation.Cancel();
    }

    private bool CanCancelParsing()
    {
        return PasteInput.IsParsing && _parseCancellation.IsActive;
    }

    private bool CanApplyPastedText()
    {
        return _enabled && !PasteInput.IsParsing && !Import.IsImportingMembers &&
               SelectedInputIndex == (int)MemberInputMethod.Paste && !string.IsNullOrWhiteSpace(PastedText);
    }

    /// <summary>Teamsからの取り込み結果を入力欄へ反映し、テキスト貼り付けタブへ切り替える</summary>
    private void OnMembersImported(TeamInfo team, string text, MemberListDocument document)
    {
        SelectedInputIndex = (int)MemberInputMethod.Paste;
        PastedText = text;
        _documents.SetPastedDocument(document);
        Document = document;
        PasteInput.HasError = false;
        PasteInput.InfoText = $"{document.Addresses.Count}件 • Teamsから取り込み: {team.DisplayName}";
        NotifyDocumentChanged();
    }

    /// <summary>折りたたみ済みの入力要約を、元のファイル/テキスト入力の編集画面へ戻す</summary>
    [RelayCommand]
    private void EditInput()
    {
        IsEditingInput = true;
    }

    /// <summary><see cref="DocumentChanged" />を発行し、成功時は件数をステータスとして通知する</summary>
    private void NotifyDocumentChanged()
    {
        IsEditingInput = false;
        DocumentChanged?.Invoke();
        if (Document is not null)
        {
            _uiEvents.Status($"{Document.Addresses.Count}件の一意な氏名／メールアドレスを読み込みました", false);
        }
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

public partial class MemberFileViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly INotificationService _notifications;
    private readonly IUserPreferences _preferences;
    private readonly IMemberListReader _reader;
    private readonly IMemberTextParser _textParser;
    private bool _enabled = true;
    private MemberListDocument? _fileDocument;
    private CancellationTokenSource? _loadCancellation;
    [ObservableProperty] public partial string FileInfoText { get; set; } = "ファイルを選択するか、ここへドロップしてください";
    [ObservableProperty] public partial string FilePath { get; set; } = "";
    [ObservableProperty] public partial string PasteInfoText { get; set; } = "1行につき1ユーザー（氏名またはメールアドレス）";
    [ObservableProperty] public partial bool IsParsing { get; set; }
    [ObservableProperty] public partial bool IsLoadingFile { get; set; }
    [ObservableProperty] public partial bool IsPasteError { get; set; }
    [ObservableProperty] public partial string PastedText { get; set; } = "";

    [ObservableProperty] public partial int SelectedInputIndex { get; set; }

    public MemberFileViewModel(IMemberListReader reader, IMemberTextParser textParser,
        IUserPreferences preferences, IFilePickerService filePicker, INotificationService notifications)
    {
        _reader = reader;
        _textParser = textParser;
        _preferences = preferences;
        _filePicker = filePicker;
        _notifications = notifications;
    }

    public MemberListDocument? Document { get; private set; }
    public event Action? DocumentChanged;
    public event Action<string, bool>? StatusChanged;

    // ファイル読込またはテキスト解析が失敗したときに1回だけ発行する。
    // Viewはこれを受けて、選択中の入力方法(ファイル/貼り付け)に応じた修正対象へフォーカスを移す。
    public event Action? InputFocusRequested;

    public void SetEnabled(bool value)
    {
        _enabled = value;
        BrowseCommand.NotifyCanExecuteChanged();
        LoadDroppedFileCommand.NotifyCanExecuteChanged();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFileLoadCommandsCanExecuteChanged()
    {
        BrowseCommand.NotifyCanExecuteChanged();
        LoadDroppedFileCommand.NotifyCanExecuteChanged();
        CancelLoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedInputIndexChanged(int value)
    {
        ApplySelectedInput();
        ApplyPastedTextInputCommand.NotifyCanExecuteChanged();
    }

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

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task Browse()
    {
        var path = _filePicker.PickMemberFile(_preferences.LastFolder);
        if (path is not null) await Load(path);
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadDroppedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        SelectedInputIndex = 0;
        await Load(path);
    }

    private bool CanLoad()
    {
        return _enabled && !IsLoadingFile;
    }

    // CSV/Excelの解析は行数・列数によって時間がかかりうるため、貼り付け入力(ApplyPastedTextInputAsync)と同様に
    // UIスレッド外(Task.Run)で実行し、IsLoadingFileで処理中表示、CancellationTokenSourceでキャンセルを可能にする。
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
            _notifications.ShowError(ex.Message, "ファイル読込エラー", () => InputFocusRequested?.Invoke());
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

    [RelayCommand(CanExecute = nameof(CanCancelLoad))]
    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
    }

    private bool CanCancelLoad()
    {
        return IsLoadingFile && _loadCancellation is not null;
    }

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
        return _enabled && !IsParsing && SelectedInputIndex == 1 && !string.IsNullOrWhiteSpace(PastedText);
    }

    private void NotifyDocumentChanged()
    {
        DocumentChanged?.Invoke();
        if (Document is not null)
            StatusChanged?.Invoke($"{Document.Addresses.Count}件の一意な氏名／メールアドレスを読み込みました", false);
    }
}

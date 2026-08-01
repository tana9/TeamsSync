using System.Collections.ObjectModel;
using System.Windows;

using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.UiHarness;

public partial class ScenarioWindow
{
    private readonly DemoTeamsGateway _gateway;
    private readonly MainWindowViewModel _viewModel;

    public ScenarioWindow(MainWindowViewModel viewModel, DemoTeamsGateway gateway)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _gateway = gateway;
        Scenarios =
        [
            new DemoScenario("初期状態", "チーム選択後、入力前の空状態を確認します。", "demo-team", "", SyncMode.AddOnly),
            new DemoScenario("追加のみ", "既存メンバーを維持し、新規メンバー1名を追加する差分です。", "demo-team",
                "taro@example.com\nhanako@example.com\nlong.user@example.com\nnew.member@example.com",
                SyncMode.AddOnly),
            new DemoScenario("完全同期・削除あり", "リスト外の一般メンバーが赤い削除行として表示されます。", "demo-team",
                "taro@example.com", SyncMode.FullSync),
            new DemoScenario("指定メンバーを削除", "入力した一般メンバーだけを削除するケースです。", "demo-team",
                "hanako@example.com", SyncMode.RemoveSpecified),
            new DemoScenario("未解決ユーザー", "存在しないメールアドレスによるエラー行と実行不可状態です。", "demo-team",
                "unknown.user@example.com", SyncMode.AddOnly),
            new DemoScenario("所有者保護", "所有者を削除対象にせず保護するケースです。", "demo-team",
                "owner@example.com", SyncMode.RemoveSpecified),
            new DemoScenario("同姓同名", "同じ表示名のユーザーが複数いて特定できないケースです。", "demo-team",
                "同姓 同名", SyncMode.AddOnly),
            new DemoScenario("429再試行", "Graphのスロットリング待機後に検索が成功するケースを疑似再現します。", "demo-team",
                "new.member@example.com", SyncMode.AddOnly, false, null, false, "new.member@example.com"),
            new DemoScenario("エラー通知", "閉じるまで残るSnackbar、詳細コピー、閉じるボタンを確認します。", "demo-team",
                "error.notice@example.com", SyncMode.AddOnly, false, null, false, null,
                "error.notice@example.com"),
            new DemoScenario("長い表示内容", "長いチーム名・表示名を使って折り返しとDPI拡大を確認します。", "long-team",
                "とても長い表示名を持つレイアウト確認用ユーザー <long.user@example.com>\nsecond.new@example.com", SyncMode.AddOnly),
            new DemoScenario("同期成功結果", "追加処理を実行し、成功サマリーと保存ボタンを表示します。", "demo-team",
                "taro@example.com\nhanako@example.com\nlong.user@example.com\nnew.member@example.com", SyncMode.AddOnly,
                true),
            new DemoScenario("同期一部失敗", "2件の追加のうち1件を失敗させ、失敗一覧を表示します。", "demo-team",
                "taro@example.com\nhanako@example.com\nlong.user@example.com\nnew.member@example.com\nsecond.new@example.com",
                SyncMode.AddOnly, true, "user-second"),
            new DemoScenario("途中キャンセル", "遅い追加処理を開始し、途中でキャンセルして未実行表示と再確認導線を確認します。", "demo-team",
                "new.member@example.com\nsecond.new@example.com", SyncMode.AddOnly, true, null, true),
            new DemoScenario("大量入力", "1,000件の未解決入力で進捗表示と大量行レイアウトを確認します。", "demo-team",
                string.Join('\n', Enumerable.Range(1, 1000).Select(index => $"load{index}@example.invalid")),
                SyncMode.AddOnly)
        ];
        SelectedScenario = Scenarios[0];
        DataContext = this;
    }

    public ObservableCollection<DemoScenario> Scenarios { get; }
    public DemoScenario SelectedScenario { get; set; }

    private async void ApplyScenario(object sender, RoutedEventArgs e)
    {
        DemoScenario scenario = SelectedScenario;
        ApplyButton.IsEnabled = false;
        ApplyButton.Content = "適用中…";
        ScenarioPicker.IsEnabled = false;
        try
        {
            _gateway.Reset();
            _gateway.FailingUserId = scenario.FailingUserId;
            _gateway.OperationDelay = scenario.CancelDuringExecution ? TimeSpan.FromSeconds(2) : TimeSpan.Zero;
            _gateway.ThrottledIdentifier = scenario.ThrottledIdentifier;
            _gateway.FailingSearchIdentifier = scenario.FailingSearchIdentifier;
            _viewModel.TeamSelection.SelectedTeam = _viewModel.TeamSelection.Teams
                .Single(team => team.Id == scenario.TeamId);
            _viewModel.SyncWorkspace.SelectedMode = _viewModel.SyncWorkspace.Modes
                .Single(mode => mode.Mode == scenario.Mode);
            _viewModel.MemberFile.SelectedInputIndex = 1;
            _viewModel.MemberFile.PastedText = scenario.Input;
            if (string.IsNullOrWhiteSpace(scenario.Input))
            {
                return;
            }

            await _viewModel.MemberFile.ApplyPastedTextInputCommand.ExecuteAsync(null);
            await _viewModel.SyncWorkspace.PreviewCommand.ExecuteAsync(null);
            if (scenario.Execute)
            {
                Task execution = _viewModel.SyncWorkspace.ExecuteSyncCommand.ExecuteAsync(null);
                if (scenario.CancelDuringExecution)
                {
                    for (int attempt = 0; attempt < 600 && !execution.IsCompleted &&
                         !_viewModel.SyncWorkspace.IsSyncing; attempt++)
                    {
                        await Task.Delay(100);
                    }

                    if (_viewModel.SyncWorkspace.IsSyncing)
                    {
                        await Task.Delay(250);
                        _viewModel.SyncWorkspace.CancelCommand.Execute(null);
                    }
                }

                await execution;
            }
        }
        finally
        {
            ScenarioPicker.IsEnabled = true;
            ApplyButton.Content = "適用";
            ApplyButton.IsEnabled = true;
            Activate();
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed record DemoScenario(
    string Name,
    string Description,
    string TeamId,
    string Input,
    SyncMode Mode,
    bool Execute = false,
    string? FailingUserId = null,
    bool CancelDuringExecution = false,
    string? ThrottledIdentifier = null,
    string? FailingSearchIdentifier = null);

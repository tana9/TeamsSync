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
            new DemoScenario("長い表示内容", "長いチーム名・表示名を使って折り返しとDPI拡大を確認します。", "long-team",
                "とても長い表示名を持つレイアウト確認用ユーザー <long.user@example.com>\nsecond.new@example.com", SyncMode.AddOnly),
            new DemoScenario("同期成功結果", "追加処理を実行し、成功サマリーと保存ボタンを表示します。", "demo-team",
                "taro@example.com\nhanako@example.com\nlong.user@example.com\nnew.member@example.com", SyncMode.AddOnly,
                true),
            new DemoScenario("同期一部失敗", "2件の追加のうち1件を失敗させ、失敗一覧を表示します。", "demo-team",
                "taro@example.com\nhanako@example.com\nlong.user@example.com\nnew.member@example.com\nsecond.new@example.com",
                SyncMode.AddOnly, true, "user-second")
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
                await _viewModel.SyncWorkspace.ExecuteSyncCommand.ExecuteAsync(null);
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
    string? FailingUserId = null);
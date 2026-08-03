using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>同期プラン、差分一覧、フィルター、削除警告の画面表示状態を管理する</summary>
public sealed partial class SyncPlanDisplayState : ObservableObject
{
    private readonly BulkObservableCollection<SyncChangeRowViewModel> _changes = [];

    public SyncPlanDisplayState()
    {
        ChangesView = CollectionViewSource.GetDefaultView(Changes);
        ChangesView.Filter = FilterChange;
        Changes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChanges));
        SelectedFilter = Filters[0];
    }

    /// <summary>現在表示している同期プラン。未確認の場合はnull</summary>
    public SyncPlan? Current { get; private set; }

    [ObservableProperty]
    public partial bool HasPlan { get; private set; }

    [ObservableProperty]
    public partial bool IsRemovalWarningOpen { get; private set; }

    [ObservableProperty]
    public partial string RemovalWarningMessage { get; private set; } = "";

    [ObservableProperty]
    public partial string RemovalWarningTitle { get; private set; } = "";

    [ObservableProperty]
    public partial ChangeFilter SelectedFilter { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = "";

    public ObservableCollection<SyncChangeRowViewModel> Changes => _changes;

    public ICollectionView ChangesView { get; }

    public IReadOnlyList<ChangeFilter> Filters { get; } =
    [
        new("変更あり", null, true), new("すべて", null), new("追加", ChangeKind.Add),
        new("削除", ChangeKind.Remove), new("所有者", ChangeKind.Protected),
        new("未所属", ChangeKind.NotMember),
        new("エラー", ChangeKind.Error), new("変更なし", ChangeKind.Keep)
    ];

    public bool HasChanges => Changes.Count > 0;

    public bool HasErrors => Current?.HasErrors ?? false;

    public bool HasNoChangesToApply =>
        Current is not null && Current.HasNoActionableChanges && SelectedFilter.Count == 0;

    public string NoChangesMessage =>
        Current is null ? "" : SyncWorkspaceTextFormatter.BuildPreviewStatusText(Current);

    partial void OnSelectedFilterChanged(ChangeFilter value)
    {
        ChangesView.Refresh();
        OnPropertyChanged(nameof(HasNoChangesToApply));
    }

    public void Apply(SyncPlan plan)
    {
        Current = plan;
        HasPlan = true;
        ReplaceChanges(plan.Changes);
        PlanPresentation presentation = SyncWorkspaceTextFormatter.BuildPlan(plan);
        SummaryText = presentation.Summary;
        IsRemovalWarningOpen = plan.RemoveCount > 0;
        RemovalWarningTitle = presentation.RemovalTitle;
        RemovalWarningMessage = presentation.RemovalMessage;
        NotifyPlanPropertiesChanged();
    }

    public void Clear()
    {
        Current = null;
        HasPlan = false;
        Changes.Clear();
        SummaryText = "";
        IsRemovalWarningOpen = false;
        RemovalWarningTitle = "";
        RemovalWarningMessage = "";
        SyncWorkspaceTextFormatter.ClearFilterCounts(Filters);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
        NotifyPlanPropertiesChanged();
    }

    public void SelectErrors()
    {
        SelectedFilter = Filters.Single(filter => filter.Kind == ChangeKind.Error);
    }

    private void ReplaceChanges(IEnumerable<SyncChange> changes)
    {
        _changes.ReplaceAll(changes.OrderBy(x => x.Kind switch
            {
                ChangeKind.Error => 0, ChangeKind.Remove => 1, ChangeKind.Add => 2, ChangeKind.Protected => 3,
                _ => 4
            })
            .Select(change => new SyncChangeRowViewModel(change)));

        ChangesView.Refresh();
        SyncWorkspaceTextFormatter.UpdateFilterCounts(Filters, Changes);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
    }

    private bool FilterChange(object item)
    {
        if (item is not SyncChangeRowViewModel row)
        {
            return false;
        }

        if (SelectedFilter.ChangesOnly)
        {
            return ChangeFilter.MatchesChangesOnly(row.Kind);
        }

        return SelectedFilter.Kind is null || row.Kind == SelectedFilter.Kind;
    }

    private void NotifyPlanPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNoChangesToApply));
        OnPropertyChanged(nameof(NoChangesMessage));
    }
}

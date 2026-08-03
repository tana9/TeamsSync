using System.Windows;

using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>
///     手順が「現在の操作」の間だけ、ブロッカーメッセージ(赤いエラーメッセージ)を表示するView
/// </summary>
public partial class StepIncompleteWarningView
{
    /// <summary>表示条件となる手順の進捗状態</summary>
    public static readonly DependencyProperty StepStateProperty = DependencyProperty.Register(
        nameof(StepState), typeof(WorkflowStepState), typeof(StepIncompleteWarningView));

    /// <summary>表示する案内文</summary>
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(StepIncompleteWarningView));

    /// <summary>コンストラクター</summary>
    public StepIncompleteWarningView()
    {
        InitializeComponent();
    }

    /// <summary>表示条件となる手順の進捗状態</summary>
    public WorkflowStepState StepState
    {
        get => (WorkflowStepState)GetValue(StepStateProperty);
        set => SetValue(StepStateProperty, value);
    }

    /// <summary>表示する案内文</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}

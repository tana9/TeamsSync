using System.Windows;

using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Views;

/// <summary>番号付き手順見出し(アイコン・タイトル・サブタイトル)を共通レイアウトで表示する</summary>
public partial class StepHeaderView
{
    /// <summary>見出しの左に表示するアイコン</summary>
    public static readonly DependencyProperty IconSymbolProperty = DependencyProperty.Register(
        nameof(IconSymbol), typeof(SymbolRegular), typeof(StepHeaderView));

    /// <summary>「番号 見出し」のテキスト</summary>
    public static readonly DependencyProperty HeaderTextProperty = DependencyProperty.Register(
        nameof(HeaderText), typeof(string), typeof(StepHeaderView));

    /// <summary>見出し下に表示するサブタイトル</summary>
    public static readonly DependencyProperty SubtitleTextProperty = DependencyProperty.Register(
        nameof(SubtitleText), typeof(string), typeof(StepHeaderView));

    /// <summary>コンストラクター</summary>
    public StepHeaderView()
    {
        InitializeComponent();
    }

    /// <summary>見出しの左に表示するアイコン</summary>
    public SymbolRegular IconSymbol
    {
        get => (SymbolRegular)GetValue(IconSymbolProperty);
        set => SetValue(IconSymbolProperty, value);
    }

    /// <summary>「番号 見出し」のテキスト</summary>
    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    /// <summary>見出し下に表示するサブタイトル</summary>
    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }
}

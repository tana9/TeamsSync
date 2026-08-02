using System.Xml.Linq;

namespace TeamsSync.Tests.Architecture;

public sealed class AccessibilityMarkupTests
{
    private static readonly string ViewsDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "TeamsSync",
        "Presentation", "Views"));

    private static readonly string MainWindowPath = Path.Combine(ViewsDirectory, "MainWindow.xaml");

    [Fact]
    public void MainWindow_テキスト入力と競合する単独Enter割当がない()
    {
        XDocument document = XDocument.Load(MainWindowPath);
        IEnumerable<XElement> keyBindings = document.Descendants()
            .Where(element => element.Name.LocalName == "KeyBinding");

        Assert.DoesNotContain(keyBindings, binding =>
            string.Equals((string?)binding.Attribute("Key"), "Enter", StringComparison.OrdinalIgnoreCase) &&
            binding.Attribute("Modifiers") is null);
    }

    [Fact]
    public void MainWindow_主要な操作にAutomationNameが設定されている()
    {
        string text = string.Concat(Directory.GetFiles(ViewsDirectory, "*.xaml").Select(File.ReadAllText));
        string[] names =
        [
            "Microsoft 365へサインイン", "同期対象チームの検索と選択", "メンバー入力方法",
            "同期モード", "同期差分一覧", "同期進捗", "同期処理をキャンセル",
            "同期差分を確認", "確認した差分をチームに反映"
        ];

        foreach (string name in names)
        {
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", text);
        }
    }

    [Fact]
    public void Views_フォーカス表示を無効化せず動的状態をLiveRegionとして公開する()
    {
        string text = string.Concat(Directory.GetFiles(ViewsDirectory, "*.xaml").Select(File.ReadAllText));

        Assert.DoesNotContain("FocusVisualStyle\" Value=\"{x:Null}", text);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", text);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", text);
    }
}
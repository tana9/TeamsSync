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
        var document = XDocument.Load(MainWindowPath);
        var keyBindings = document.Descendants()
            .Where(element => element.Name.LocalName == "KeyBinding");

        Assert.DoesNotContain(keyBindings, binding =>
            string.Equals((string?)binding.Attribute("Key"), "Enter", StringComparison.OrdinalIgnoreCase) &&
            binding.Attribute("Modifiers") is null);
    }

    [Fact]
    public void MainWindow_主要な操作にAutomationNameが設定されている()
    {
        var text = string.Concat(Directory.GetFiles(ViewsDirectory, "*.xaml").Select(File.ReadAllText));
        string[] names =
        [
            "Microsoft 365へサインイン", "同期対象チームの検索と選択", "メンバー入力方法",
            "同期モード", "同期差分一覧", "同期進捗", "同期処理をキャンセル",
            "同期結果をCSVで保存", "同期差分を確認", "確認した差分で同期を実行"
        ];

        foreach (var name in names)
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", text);
    }
}

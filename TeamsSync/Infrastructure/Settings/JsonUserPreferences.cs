using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Settings;

public sealed class JsonUserPreferences : IUserPreferences
{
    private readonly ILogger<JsonUserPreferences> _logger;
    private readonly string _path;

    public JsonUserPreferences(ILogger<JsonUserPreferences> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TeamsSync", "preferences.json"), logger)
    {
    }

    public JsonUserPreferences(string path, ILogger<JsonUserPreferences> logger)
    {
        _path = path;
        _logger = logger;
        Load();
    }

    public string? LastFolder { get; set; }
    public string? LoadWarning { get; private set; }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Data(LastFolder)));
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
                throw new JsonException("設定ファイルが空です。");
            var value = JsonSerializer.Deserialize<Data>(text)
                        ?? throw new JsonException("設定データを読み取れません。");
            LastFolder = value.LastFolder;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "ユーザー設定を読み込めないため初期値を使用します。Path={SettingsPath}", _path);
            var backupPath = TryBackup();
            LoadWarning = backupPath is null
                ? "設定ファイルを読み込めなかったため、初期値で起動しました。"
                : $"破損した設定ファイルを退避し、初期値で起動しました。退避先: {backupPath}";
        }
    }

    private string? TryBackup()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            var fileName = Path.GetFileNameWithoutExtension(_path);
            var extension = Path.GetExtension(_path);
            var backupPath = Path.Combine(directory,
                $"{fileName}.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}{extension}");
            File.Copy(_path, backupPath);
            _logger.LogWarning("破損したユーザー設定を退避しました。BackupPath={BackupPath}", backupPath);
            return backupPath;
        }
        catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(backupException, "破損したユーザー設定を退避できませんでした。Path={SettingsPath}", _path);
            return null;
        }
    }

    private sealed record Data(string? LastFolder);
}
using System.Text.Json;

using Microsoft.Extensions.Logging;

using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Infrastructure.Settings;

/// <summary>
///     ユーザー設定(最終利用フォルダーなど)をJSONファイルとして永続化する。
///     読み込みに失敗した場合は破損ファイルを退避し、初期値で継続する
/// </summary>
public sealed class JsonUserPreferences : IUserPreferences
{
    private readonly ILogger<JsonUserPreferences> _logger;
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly IIdentifierGenerator _identifierGenerator;

    /// <summary>既定の保存先(%LocalAppData%\TeamsSync\preferences.json)を使用するコンストラクター</summary>
    public JsonUserPreferences(ILogger<JsonUserPreferences> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TeamsSync", "preferences.json"), logger, null, null)
    {
    }

    /// <summary>保存先パスを指定できるコンストラクター(主にテスト用)</summary>
    public JsonUserPreferences(string path, ILogger<JsonUserPreferences> logger, TimeProvider? timeProvider = null,
        IIdentifierGenerator? identifierGenerator = null)
    {
        _path = path;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();
        Load();
    }

    /// <summary>最後にファイル選択に使用したフォルダー</summary>
    public string? LastFolder { get; set; }

    /// <summary>設定の読み込み時に発生した警告メッセージ(なければnull)</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>現在の設定値をJSONファイルへ書き出す</summary>
    public void Save()
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new Data(LastFolder));
        AtomicFileWriter.Write(_path, stream => stream.Write(json), true, _identifierGenerator);
    }

    /// <summary>
    ///     設定ファイルを読み込む。存在しない場合は何もせず、破損している場合は
    ///     バックアップを試みたうえで<see cref="LoadWarning" />を設定する
    /// </summary>
    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            string text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new JsonException("設定ファイルが空です。");
            }

            Data value = JsonSerializer.Deserialize<Data>(text)
                         ?? throw new JsonException("設定データを読み取れません。");
            LastFolder = value.LastFolder;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "ユーザー設定を読み込めないため初期値を使用します。Path={SettingsPath}", _path);
            string? backupPath = TryBackup();
            LoadWarning = backupPath is null
                ? "設定ファイルを読み込めなかったため、初期値で起動しました。"
                : $"破損した設定ファイルを退避し、初期値で起動しました。退避先: {backupPath}";
        }
    }

    /// <summary>破損した設定ファイルをタイムスタンプ付きの別名でコピーし、退避先のパスを返す</summary>
    private string? TryBackup()
    {
        try
        {
            string directory = Path.GetDirectoryName(_path)!;
            string fileName = Path.GetFileNameWithoutExtension(_path);
            string extension = Path.GetExtension(_path);
            string backupPath = Path.Combine(directory,
                $"{fileName}.corrupt-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmssfff}{extension}");
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

    /// <summary>JSONファイルへ書き出す設定データの形</summary>
    private sealed record Data(string? LastFolder);
}
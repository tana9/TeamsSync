namespace TeamsSync.Infrastructure.Files;

// Excelなどが書込み用に開いたまま読み取り共有は許可しているケースを読めるようにするため、
// File.OpenRead既定のFileShare.ReadWriteへ緩め、他プロセスの読み書きを妨げないようにする。
// 完全排他(FileShare.None)のロックはこれでも読めないため、読込前後のハッシュ比較など
// 呼び出し元側の安全策と組み合わせて使うこと
/// <summary>他プロセスによる読み書きを妨げないよう共有モードでファイルを開く</summary>
internal static class SharedFileAccess
{
    public static FileStream Open(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
namespace TeamsSync.Infrastructure.Graph;

// GraphSdkClientの公開コンストラクターは、Graph SDK通信の診断ログをILogger<T>のジェネリック
// カテゴリとして受け取る必要があるが、その型引数はコンストラクターと同じ可視性(public)でなければ
// ならない(CS0051)。実体であるGraphSdkTransportHandlerは外部から使われるべきではない実装詳細
// なので、ログカテゴリ名のためだけの公開マーカー型をここへ用意し、GraphSdkTransportHandler自体は
// internalのまま保つ
/// <summary>Graph SDK通信の診断ログのカテゴリ名として使う、インスタンス化できない公開マーカー型</summary>
public sealed class GraphSdkLogCategory
{
    private GraphSdkLogCategory()
    {
    }
}

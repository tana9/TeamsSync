namespace TeamsSync;

/// <summary>
///     起動処理のうち、WPFリソースの読み込みを伴わない段階(設定読込・DI構築)で発生した、
///     原因が特定できる既知の起動失敗を表す。それ以外の想定外の例外とは区別し、
///     利用者へより具体的な案内を表示するために使う。
/// </summary>
internal sealed class StartupConfigurationException(string message) : Exception(message);

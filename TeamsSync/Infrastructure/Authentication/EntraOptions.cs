namespace TeamsSync.Infrastructure.Authentication;

/// <summary>
///     appsettings.json・環境変数・起動引数から束縛されるMicrosoft Entra IDのアプリ登録設定
/// </summary>
public sealed class EntraOptions
{
    /// <summary>設定ファイル上でこのオプションを束縛するセクション名</summary>
    public const string SectionName = "Entra";

    /// <summary>Entra IDに登録したアプリケーション(クライアント)ID</summary>
    public string ClientId { get; init; } = "";

    /// <summary>サインインを許可するテナントID。既定は複数テナントを許可する"organizations"</summary>
    public string TenantId { get; init; } = "organizations";
}
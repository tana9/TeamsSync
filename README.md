# TeamsSync

Microsoft Graph API を使い、CSV または Excel の一覧と Microsoft Teams の
一般メンバーを同期する WPF デスクトップアプリです。サインインユーザーが
所有者になっているチームだけを選択できます。

## セットアップ

1. Microsoft Entra 管理センターで「アプリの登録」を作成します。
2. 「認証」→「プラットフォームを追加」→「モバイルとデスクトップ
   アプリケーション」で `http://localhost`（ポート番号なし）を追加し、
   「パブリック クライアント フローを許可」を有効にします。MSAL.NETの
   システムブラウザ認証はループバックURIのみ対応しており、
   `.../nativeclient`を登録すると`AADSTS50011`やMSALの
   `Only loopback redirect uri is supported`エラーになります。
3. Microsoft Graph の**委任されたアクセス許可**を追加します。
   - `User.Read`
   - `User.ReadBasic.All`
   - `Team.ReadBasic.All`
   - `TeamMember.Read.All`
   - `TeamMember.ReadWriteNonOwnerRole.All`
4. 組織のポリシーに応じて管理者の同意を付与します。
5. `TeamsSync/appsettings.json` の `Entra:ClientId` をアプリケーション
   (クライアント) ID に置き換えます。単一テナントに限定する場合は
   `Entra:TenantId` もテナントIDに置き換えます。 TenantIdは省略でき、その場合は`organizations`が使用されます。ClientIdやTenantIdが未設定でもアプリは起動でき、ClientIdが必要になるのはサインイン時です。

```json
{
  "Entra": {
    "ClientId": "アプリケーション（クライアント）ID",
    "TenantId": "organizations"
  }
}
```

```powershell
dotnet restore TeamsSync.slnx
dotnet run --project TeamsSync/TeamsSync.csproj
```

## マニュアルの生成

利用者向けマニュアル([MANUAL.md](MANUAL.md))は、exeに埋め込むHTML
(`TeamsSync/Resources/Manual.html`)としても保持しています。MANUAL.mdを
更新したら、[Task](https://taskfile.dev/)と[pandoc](https://pandoc.org/)を
インストールした環境で次を実行し、HTMLを再生成してください。

```powershell
task manual
```

`docs/manual-style.css`(見た目)と`docs/manual-callouts.lua`(GitHub風の
`> [!NOTE]`/`> [!IMPORTANT]`をコールアウト表示に変換するpandoc Luaフィルタ)
を使って、TeamsSyncパープルを基調とした自己完結HTMLを生成します。

## シングルバイナリの作成

[Task](https://taskfile.dev/)をインストールしたWindows環境で、次を実行します。

```powershell
task publish-single
```

`artifacts/TeamsSync-win-x64/TeamsSync.exe`に、.NETランタイムと既定設定を含む
Windows x64向けの単一EXEが生成されます。発行処理は全テストを実行し、出力先に
EXE以外のファイルが残っていないことも検証します。

配布先では、Entra ID設定を環境変数または起動引数で指定できます。

```powershell
$env:TEAMSSYNC_Entra__ClientId = "アプリケーション（クライアント）ID"
$env:TEAMSSYNC_Entra__TenantId = "organizations"
.\TeamsSync.exe

# または
.\TeamsSync.exe --Entra:ClientId="アプリケーション（クライアント）ID" --Entra:TenantId="organizations"
```

環境変数と起動引数は、EXEへ埋め込まれた`appsettings.json`より優先されます。
管理者向けの登録・権限・データ削除手順は[運用ガイド](docs/OPERATIONS.md)を参照してください。
## 入力ファイル

- `.csv` または `.xlsx`（Excel 97-2003 `.xls` は非対応）
- CSVまたはExcelの1枚目のシートから、対応するヘッダー列を自動検出します。複数候補がある場合はメールアドレスまたはUPN列を氏名列より優先し、ヘッダーがなければ1列目を読み込みます。
- 先頭行が `email`、`mail`、`upn`、`userPrincipalName`、
  `メール`、`メールアドレス`、`name`、`displayName`、`氏名`、`姓名`、`名前` のいずれかならヘッダーとして扱います。
- 値は Microsoft Entra ID の UPN、メールアドレス、または表示名（氏名）を指定します。`山田 太郎`、`山田　太郎`、`山田太郎`のような姓名間の空白は区別しません。
- 同じ表示名のユーザーが複数見つかった場合は誤登録を避けるため同期を停止します。その場合はメールアドレスまたはUPNで指定してください。
- 空行と大文字小文字を無視した重複は除外します。
- 選択中チームの現在の一般メンバーを編集の基準にする場合は、`現在のメンバーを取り込む`を使用できます。所有者を除き、`表示名 <メールアドレス>`形式でテキスト入力へ反映します。同期の識別には山括弧内のメールアドレスだけを使用し、既存入力がある場合は置き換え確認を表示します。

例:

```csv
email
user1@contoso.com
user2@contoso.com
```

## 同期モード

- `追加のみ（既存メンバーを維持）`が既定です。リストにないメンバーは削除しません。
- `指定メンバーを削除`では、リストに入力した現在の一般メンバーだけを削除します。入力していないメンバーは維持します。
- `完全同期（リスト外を削除）`では、リストにない一般メンバーを削除します。
- モードを変更すると既存のプレビューは破棄され、再確認が必要になります。

## 安全上の仕様

- チームの全所有者を追加・削除の対象外にします。
- Graph 権限も所有者ロールを変更できない最小権限を使います。
- 未解決ユーザーが1件でもある場合、同期を開始しません。
- 変更前に追加・削除・維持・保護の差分を表示し、最終確認を求めます。
- Graph のレート制限には `Retry-After` と指数バックオフで対応します。
- 各変更は独立して実行するため、一部失敗時は成功分まで自動ロールバック
  されません。結果を表示し、再度差分確認してください。

## 監査ログ

構造化JSONLログを`%LocalAppData%\TeamsSync\Logs`へ保存します。ログは日次および
25MB到達時にローテーションし、既定で30ファイルを保持します。保持数とサイズ上限は
`appsettings.json`の`AuditLogging:RetainedFileCount`と`FileSizeLimitBytes`で変更できます。
アクセストークンやAuthorizationヘッダーは記録しません。氏名、メール、UPN、入力ファイルのフルパス、Graphレスポンス本文も監査ログへ記録しません。入力ファイル名、SHA-256、Entra/GraphのオブジェクトID、操作件数、相関IDだけを保存し、他のログと同じ保持期間を適用します。
## 運用上の考慮点

- ゲストはテナントに既に存在する UPN で指定する必要があります。
- 動的 Microsoft 365 グループ、オンプレミス同期グループなど、Graph から
  メンバーを直接変更できないチームでは失敗します。
- プライベート/共有チャネル固有のメンバーはこのアプリの対象外です。
- 大規模同期は Teams 側への反映に時間がかかります。
- 削除を伴うため、運用環境では実行者、時刻、対象チーム、差分、結果を
  監査ログへ保存する拡張を推奨します。
- 同じ人物を異なるアドレス（新旧メールアドレス、氏名とメールアドレス等）で2行以上入力した場合、
  ユーザーIDで一意化し、差分一覧では1行にまとめて表示します。追加・削除は二重に実行しません。

## テスト

```powershell
dotnet test TeamsSync.slnx
```

### 実テナントなしでUIを確認する

開発専用の`TeamsSync.UiHarness`は、本番のView・ViewModelをそのまま利用し、認証とGraph APIだけをデモ実装へ差し替えます。Microsoft 365へ接続せず、実チームを変更することなく画面を操作できます。

```powershell
task ui-harness
```

または次のコマンドで起動します。

```powershell
dotnet run --project TeamsSync.UiHarness/TeamsSync.UiHarness.csproj
```

起動時にダミーアカウントで自動サインインします。UI確認用チームには、所有者、一般メンバー、長い表示名のメンバーが含まれます。`new.member@example.com`と`second.new@example.com`は追加候補として使用できます。Harness内の追加・削除はメモリ上のデモデータだけを変更し、終了すると初期状態へ戻ります。

リリース候補の確認は[リリース前チェックリスト](docs/RELEASE_CHECKLIST.md)に従ってください。
## アーキテクチャ

CommunityToolkit.Mvvmを利用したMVVMと、DDDの依存方向を意識したレイヤー構成です。

- `Domain/Teams`: チーム、メンバー、同期差分、同期計画などのドメインモデル
- `Application`: Graphやファイル読込のポートと、同期計画・実行ユースケース
- `Infrastructure`: MSAL認証、Microsoft Graph、CSV/Excel読込の実装
- `Presentation`: WPF View、MVVM ToolkitのViewModel、ダイアログ実装

起動基盤には.NET Generic Hostを使用しています。App.xaml.csでHostを開始・停止し、次の標準機能を統合しています。

- レイヤー別のDI登録（AddApplication / AddInfrastructure / AddPresentation）
- IOptionsMonitor<EntraOptions>によるEntra設定
- IHttpClientFactoryによるGraph用HttpClient管理
- ILogger<T>による起動・認証・Graph・同期ログ
- ValidateOnBuild / ValidateScopesを使ったDI構成テスト

依存方向はPresentation/InfrastructureからApplication、ApplicationからDomainです。DomainはUI、Graph、ファイル形式に依存しません。Viewのコードビハインドは`DataContext`設定だけで、操作は`RelayCommand`、状態は`ObservableProperty`でバインドしています。
## UIライブラリ

[WPF UI](https://github.com/lepoco/wpfui) 4.3.0を採用しています。`FluentWindow`、Fluentテーマ、`Card`、`Button`、Fluent System Icons、`ProgressRing`を使用し、Microsoft 365と親和性のある画面にしています。UI操作は引き続きCommunityToolkit.MvvmのCommandとBindingを経由し、Viewに業務ロジックを置きません。
## UI・UXと安全機能

- チーム名のインクリメンタル検索
- CSV/Excelのドラッグ＆ドロップと、件数・シート・検出列・更新日時の表示
- 差分の日本語Badge表示と、変更あり・追加・削除・所有者・エラーなどのフィルター
- 削除がある場合のInfoBar警告
- WPF UI ContentDialogによる対象チーム・ファイル・件数の最終確認
- 件数ベースの進捗バー、現在の操作、キャンセル
- 完了・一部失敗・中止をSnackbarで通知し、結果は画面に保持
- 同期結果CSVを`%LocalAppData%\TeamsSync\Logs`へ一意な名前で自動保存し、画面のリンクから開く
- 前回のファイル保存先フォルダをLocalApplicationDataの`TeamsSync/preferences.json`へ保存
  (誤操作防止のため、同期先チームは毎回明示的に選択する必要があります)
- Windowsのアクセントカラーに追従（テーマは常にライト固定、ダーク・ハイコントラストへの自動切替は非対応）
- AutomationPropertiesとライブ通知によるスクリーンリーダー対応
- `Esc`キーで同期をキャンセル

### Graph API通信の回復性

Graph APIの読み取り・書き込みは共通のHTTP回復性パイプラインを使用し、408、429、5xx、通信例外、タイムアウトを対象に再試行します。`Retry-After`がある場合はその待機時間を優先し、全体・試行単位のタイムアウトとサーキットブレーカーも適用します。

同期処理自体は、応答喪失時の二重操作を避けるため、失敗した追加・削除を自動では再実行しません。失敗後にGraph上の実状態を再取得し、未反映の差分だけを再計画します。

アクセシビリティのリリース確認手順は [ACCESSIBILITY.md](ACCESSIBILITY.md) を参照してください。

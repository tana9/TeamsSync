# TeamsSync コード解説

Microsoft Teamsのメンバーシップを、メンバーリストファイルと同期するWPFデスクトップアプリ。
Clean Architecture(Domain → Application → Infrastructure → Presentation)構成。

## 目次

1. [全体アーキテクチャ](#1-全体アーキテクチャ)
2. [Domain層 — 同期の業務ルール](#2-domain層--同期の業務ルール)
3. [Application層 — ユースケースのオーケストレーション](#3-application層--ユースケースのオーケストレーション)
4. [Infrastructure層 — Graph API通信](#4-infrastructure層--graph-api通信)
5. [Infrastructure層 — Graph以外(認証・ファイルI/O・ログ・設定)](#5-infrastructure層--graph以外認証ファイルio ログ設定)
6. [Presentation層(WPF/MVVM)](#6-presentation層wpfmvvm)

---

## 1. 全体アーキテクチャ

```
Presentation (WPF/MVVM) — 画面・操作
    ↓
Application — ユースケース(同期プラン作成・実行・照合)
    ↓
Domain — 純粋な業務ルール(Graph通信なし)
    ↑
Infrastructure — Graph通信・認証・ファイルI/O・ログ・設定の実装(Application層のポートを実装)
```

Clean Architectureで、`Application/Abstractions/ApplicationPorts.cs` にある
`ITeamsGateway` / `IMemberListReader` / `IMemberTextParser` / `ISyncResultWriter` /
`IUserPreferences` / `IAuthenticationService` が境界インターフェースで、Infrastructureが
それらを実装する。Presentationはさらに `Presentation/Services/PresentationPorts.cs` 側の
UI固有ポート(`IFilePickerService`など)を実装する。

---

## 2. Domain層 — 同期の業務ルール

このアプリの核心は「メンバーリスト(入力)と現在のチーム構成を突き合わせて、追加/削除/維持/
保護/エラーに分類する」ロジックで、**Graph通信を一切含まない純粋な差分計算**として
`SyncPlanFactory` に実装されている。

| ファイル | 役割 |
|---|---|
| `TeamModels.cs` | `TeamInfo`/`TeamMember`/`DirectoryUser`/`ChangeKind`/`ChangeReason`/`SyncMode`/`SyncChange`などの値オブジェクト |
| `TeamRoster.cs` | 現メンバー一覧をメール/正規化氏名/userIdで索引化したコレクション |
| `UserIdentifier.cs` | 氏名の正規化(NFKC化+空白除去)と比較 |
| `AddressResolver.cs` | 入力1件をロスターまたはディレクトリ候補と突き合わせ`AddressResolution`を返す |
| `SyncModePolicy.cs` | 同期モード(追加のみ/削除のみ/両方/完全同期)ごとの分類ルール |
| `SyncPlanFactory.cs` | 差分エンジン本体 |
| `SyncPlan.cs` | 差分結果の集約(変更一覧・件数・実行可否・再計画との同値比較) |
| `MemberIdentifierLine.cs` | `表示名 <email>`形式の1行のフォーマット/パース |
| `ChangeKindText.cs` / `SyncChangeReasonText.cs` | 表示用文言のマッピング |

### 複雑な部分

- **所有者は常に保護**(`SyncPlanFactory.cs:157-166`) — `ClassifyExistingMember`はモードを
  問わず`IsOwner`なら`Protected`を返す。モード依存の判定(`SyncModePolicy.ClassifyMatchedNonOwner`)
  より先に、データ側の不変条件として分離している。
- **完全同期の削除対象決定**(`SyncPlanFactory.cs:174-187`) — 「入力で解決できたユーザー」＋
  「Keep/Protectedになったユーザー」＋「氏名重複などで特定できなかったユーザー(=既にエラー
  表示済み)」を`wantedIds`として除外し、残った非所有者メンバー全員を削除対象にする。曖昧一致の
  メンバーを二重表示しないよう明示的に除外している点がポイント。
- **重複入力のマージ**(`SyncPlanFactory.cs:86-100`) — 同じユーザーが氏名とメールなど複数表記
  で重複指定された場合、2行目以降を別行にせず、最初の行のEmailへ`／`区切りで合流させる。
- **氏名のみ一致の降格**(`AddressResolver.cs`) — メールでなく氏名だけで一致した場合、
  `MatchedByNameOnly`フラグが立ち、`ChangeReason`が「氏名一致のみ」という低信頼の理由に
  差し替わる(同姓同名の別人を誤って残す/消すリスクをUIで警告するため)。
- **氏名正規化**(`UserIdentifier.cs:13-17`) — NFKC正規化後に**空白を全除去**してから比較。
  全角/半角混在や表記ゆれのある日本語氏名を吸収するため。

---

## 3. Application層 — ユースケースのオーケストレーション

| ファイル | 役割 |
|---|---|
| `SyncPlanService.cs` | 現メンバー取得+入力解決(ロスター一致 or ディレクトリ検索)→`SyncPlanFactory`でプラン作成/再検証/照合 |
| `SyncExecutor.cs` | プランのAdd/Remove操作をGraphへ順次実行 |
| `SyncExecutionCoordinator.cs` | 再検証→実行→監査ログ書き込み→事後照合、をひとつのユースケースにまとめる最上位オーケストレーター |
| `TeamsAccessService.cs` | サインインユーザー情報・所有チーム一覧・現メンバーのテキストインポート(読み取り専用の別経路) |

### 呼び出しの流れ

「ファイル/テキストを読み込む→プラン作成→プレビュー確認→同期実行」の流れで、実行時は
`SyncExecutionCoordinator.RevalidateAndExecuteAsync`が**実行直前にもう一度
`SyncPlanService.RevalidatePlanAsync`でプランを作り直し、プレビュー時点のプランと同値か比較**
する。ズレていれば`IsStale=true`を返して実行を中止する(ユーザーがプレビューを見ている間に
チーム状態が変わった場合の事故防止)。

### 複雑な部分

- **キャンセル時の「不明」状態の記録**(`SyncExecutor.cs:109-121`) — Graphへのリクエスト送信後
  にキャンセルされた場合、サーバー側で実際に成功したか判別できない。結果から欠落させると
  「未実行」と誤認されるため、`Uncertain=true`として明示的に記録し、後続の照合
  (`ReconcileAsync`)へ判断を委ねる。GraphのHTTPレベルの429リトライ(Infrastructure/Graph側)
  とは別の、アプリケーションレベルでの「不確実性」の扱い。
- **操作間の300ms待機**(`SyncExecutor.cs:14,104`) — 成功したAdd/Removeごとに固定300msの
  スロットリング。HTTPレベルのリトライとは独立した、アプリ側の負荷抑制。
- **実行後は必ず再照合**(`SyncExecutionCoordinator.cs`) — 成功/失敗/キャンセルを問わず、実行後
  にGraphから最新状態を取り直して「実際に反映されたか」を確認する。キャンセル時は呼び出し元の
  (既にキャンセル済みの)トークンではなく`CancellationToken.None`で照合を行う(照合自体が即
  キャンセルされないように)。
- **ログ書き込みと照合の失敗を独立させる** — 監査ログの書き込み失敗と事後照合の失敗はお互いを
  握りつぶさず、それぞれ独立した`Exception?`として結果に保持される(片方の失敗がもう片方を
  隠さないため)。
- **ディレクトリ検索の並列度制限** — `SemaphoreSlim(15)`で同時実行数を絞りつつ、入力の重複を
  大文字小文字無視でグルーピングしてAPI呼び出し回数を減らし、結果を元の行数に再展開する。
  進捗通知も25件ごとにまとめて発火し、UIスレッドの過負荷を防ぐ。

---

## 4. Infrastructure層 — Graph API通信

```
GraphTeamsGateway (ITeamsGateway実装。ドメインロジックの窓口)
  ├─ GraphSdkClient ……… Graph SDK(GraphServiceClient)経由の通信を一手に担う。read/write用に2系統もち、
  │    │                  チームメンバーの$batch取得(SendTeamMembersBatchAsync)もここから送信する
  │    └─ GraphSdkTransportHandler … SDKの内部HTTP呼び出しを名前付きHttpClientへ転送
  ├─ TeamMembersBatchFetcher … $batchで複数チームのメンバーを一括取得(所有権判定用)
  ├─ GraphUserSearchService …… ユーザー検索の段階的フォールバック
  └─ TeamOwnershipCache ……… 所有権判定結果のキャッシュ

横断的な補助クラス:
  GraphEndpoints ………… ホスト名/BaseURI/名前付きHttpClient名/メンバー取得ページサイズ上限の一元管理
  GraphEndpointValidator … トークン送信先の許可エンドポイント検証
  GraphRequestDiagnostics … client-request-idヘッダー付与
  GraphErrorHandler/Formatter … 失敗応答→ログ記録→GraphException変換の共通化
  GraphException ………… Graph API呼び出し失敗を表す例外
  GraphResponseParser …… SDKモデル → ドメインモデルへの変換
  MsalAccessTokenProvider … SDK側の認証プロバイダー(Kiota用)
```

Graph通信は現在すべて`GraphSdkClient`(Graph SDK経由)に一本化されている。かつては`$batch`送信だけ
生HTTPの`GraphHttpClient`が担っていたが、Microsoft.Graph SDKの`BatchRequestContentCollection`
(公式バッチAPI)へ移行した際に`GraphHttpClient`ごと削除し、経路を1本化した。

| ファイル | 役割 |
|---|---|
| `GraphSdkClient.cs` | Graph SDKラッパー。read/write2系統の`GraphServiceClient`を保持し、ドメイン向けメソッドに加え`$batch`送信(`SendTeamMembersBatchAsync`)・ページング継続(`CollectTeamMembersPagesAsync`)を提供 |
| `GraphSdkTransportHandler.cs` | SDKの内部通信を既存の名前付きHttpClientへ転送する`HttpMessageHandler` |
| `GraphTeamsGateway.cs` | `ITeamsGateway`実装。所有チーム判定・メンバー取得/追加/削除のオーケストレーター |
| `TeamMembersBatchFetcher.cs` | 複数チームのメンバーを`$batch`(SDKの`BatchRequestContentCollection`)で一括取得し、429/503を再試行 |
| `TeamOwnershipCache.cs` | ユーザー×チームの所有権判定結果をセッション内キャッシュ |
| `GraphUserSearchService.cs` | 直接参照で見つからないユーザーの段階的検索フォールバック |
| `GraphResponseParser.cs` | SDKモデル→ドメインモデル(`TeamMember`/`DirectoryUser`)への変換 |
| `GraphErrorHandler.cs` | 失敗応答の診断ログ記録と`GraphException`への変換を共通化 |
| `GraphErrorFormatter.cs` | Graphのエラー応答からユーザー提示用/ログ用の安全な文字列を抽出 |
| `GraphException.cs` | Graph API呼び出し失敗を表す例外(ステータスコード・request-id・client-request-idを保持) |
| `GraphRequestDiagnostics.cs` | `client-request-id`ヘッダー付与の共通処理 |
| `GraphEndpoints.cs` | Graphのホスト名/BaseURI/名前付きHttpClient名(`ReadHttpClientName`/`WriteHttpClientName`)/メンバー取得ページサイズ上限の定数を一元管理 |
| `GraphEndpointValidator.cs` | URLがGraphの想定エンドポイント(https/443/ホスト一致/認証情報なし)かを検証 |
| `MsalAccessTokenProvider.cs` | SDK(Kiota)向け認証プロバイダー。`GraphEndpointValidator`でトークン送信先を検証してから発行 |

### リクエストの流れの具体例(`GetOwnedTeamsAsync`)

1. `GraphTeamsGateway.GetOwnedTeamsAsync`が呼ばれる
2. `sdk.GetJoinedTeamsAsync` → `GraphSdkClient`の`_read`クライアントでGETし、参加中の全チームを取得
3. チームごとに`TeamOwnershipCache`を確認 → 未判定分だけ20件ずつのバッチに分割、同時実行数3で`TeamMembersBatchFetcher.FetchAsync`を呼ぶ
4. バッチ内で429/503が出た項目はGraph応答の`Retry-After`に従って待機・再試行(最大3回)。403/400などは待たずに個別APIへフォールバック
5. 結果を`TeamRoster`で判定し、所有チームのみ返す

### read/write二重クライアントとリトライポリシーの非対称性

`GraphSdkClient`は`_read`/`_write`2つの`GraphServiceClient`を保持し、GET系メソッドは`_read`、
`AddMemberAsync`/`RemoveMemberAsync`は`_write`を使う。`DependencyInjection.cs`でこの2クライアント
にそれぞれ異なるPollyリトライポリシーを設定している。

| 状況 | 読み取り(GET) | 書き込み(POST/PUT/PATCH/DELETE) |
|---|---|---|
| 429(スロットリング) | リトライする | リトライする |
| タイムアウト・503など | リトライする | **リトライしない** |

- **読み取り**: 429・503・タイムアウトなど再試行可能な失敗を全てリトライ(GETは冪等なので安全)。
- **書き込み**: `DisableForUnsafeHttpMethods()`でタイムアウト・503のリトライを止め、429だけ
  例外的に許可(`AllowThrottlingRetryForUnsafeHttpMethods`)。理由は「タイムアウト・503はサーバー
  が処理済みか不明で、そのままリトライするとメンバー追加が二重実行される恐れがある」一方、
  「429はGraphが処理前に明示的に拒否したと確定しているので安全」という判断。

このポリシーは`AddStandardResilienceHandler`が`HttpClient`生成時にハンドラーパイプラインへ組み込む
方式のため、動的に切り替えられず、read/write2つの名前付きクライアントとしてDI起動時に分ける
しかない、という技術的制約が根底にある(=結果的に関心の分離にもなっているが、主目的ではない)。

### `GraphSdkTransportHandler`の役割

Graph通信は`GraphSdkClient`(SDK)経由に一本化されているが、最終的な送信は名前付き`HttpClient`
(`MicrosoftGraph.Read`/`.Write`、DIでリトライポリシー付き登録)を通す必要がある。
`GraphSdkTransportHandler`はSDK(`GraphServiceClient`)が内部で生成するHTTPリクエストを横取りし、
診断ヘッダー付与(`GraphRequestDiagnostics`)を行ったうえでクローンしてその`HttpClient`へ転送する
`HttpMessageHandler`。失敗応答は`GraphErrorHandler`が共通の規約で`GraphException`へ変換する。
URL検証(`GraphEndpointValidator`)はここでは行わず、`MsalAccessTokenProvider`がトークン取得前に
行う(後述)。呼び出し元は`GraphSdkTransportHandler`1箇所だけだが、本体を肥大化させずテストしやすく
するため、診断ヘッダー付与・エラー変換はそれぞれ独立したヘルパークラスに切り出されている。

### `TeamMembersBatchFetcher`の三段構えの失敗処理

`GraphSdkClient.SendTeamMembersBatchAsync`がSDKの`BatchRequestContentCollection`で$batchを送信し、
その応答(`BatchResponseContentCollection`)をステータスごとに3通りに分類する:

1. `200` — 正常。`GetResponseByIdAsync<ConversationMemberCollectionResponse>`で型付き取得し、
   `GraphSdkClient.CollectTeamMembersPagesAsync`で`@odata.nextLink`のページングも継続する
2. `429`/`503`(最終試行でない場合) — `GetResponseByIdAsync(id)`が返す型付き
   `HttpResponseMessage.Headers.RetryAfter`に従い待機し、ジッターを加えて再試行(最大3回)。
   「待たずに個別APIへ切り替えるとGraphへの負荷を増幅する」ため即時フォールバックしない。
3. それ以外(403/400、解析失敗、再試行上限到達) — 結果に含めず、呼び出し元の個別API呼び出し
   フォールバックに委ねる

### `GraphTeamsGateway.GetOwnedTeamsAsync`の並列数制約

Graphの`GET /teams/{id}/members`スロットリング上限が「テナントあたり60rps」であり、`$batch`の
サブリクエストは個別に上限判定されるため、同時実行数3×バッチサイズ20=最大60件という構成は
テナント上限のすぐ下を突いている。意図的なチューニング値で、安易に増やすと429増加のリスクがある。

### `GraphUserSearchService`の段階的フォールバック検索

直接参照(完全一致)で見つからない場合、3段階で緩めながら検索する:

1. `mail`/`userPrincipalName`の完全一致ODataフィルター
2. 表示名の全文検索(`$search`、`ConsistencyLevel: eventual`ヘッダー必須)
3. 表示名の前方一致フィルター(先頭1文字)+アプリ側での名前一致フィルタリング

段階を分けている理由は「`$search`特有のあいまい一致による誤検出を最小限にする」ため。

### セキュリティ上の作り込み

- `GraphEndpointValidator.Validate` — https/ポート443/ホスト一致/認証情報なし、を検証。
  `MsalAccessTokenProvider.GetAuthorizationTokenAsync`がトークン取得前に呼ぶ。Kiotaの
  `RequestAdapter`はトークン取得をHTTP送信より先に実行するため、送信直前(`GraphSdkTransportHandler`)
  だけで検証すると不正なURL(応答内`@odata.nextLink`の改ざん等)でも先にトークン取得(MSALの
  対話サインインを誘発しうる)が走ってしまう。それを避けるため検証をトークン取得の前段に置いている。
  `MsalAccessTokenProvider.AllowedHostsValidator`プロパティは`IAccessTokenProvider`実装のために
  保持しているのみで、実際の検証には使っていない。

---

## 5. Infrastructure層 — Graph以外(認証・ファイルI/O・ログ・設定)

| ファイル | 役割 |
|---|---|
| `MsalAuthenticationService.cs` | MSAL.NETによるサインイン/サインアウト/トークン取得 |
| `EntraOptions.cs` | Entra ID(ClientId/TenantId)の設定バインド |
| `AtomicFileWriter.cs` | 一時ファイル書き込み→リネームによる原子的な保存 |
| `MemberListReader.cs` | CSV/Excelのメンバーリスト読み込み |
| `MemberTextParser.cs` | 貼り付けテキストのパース |
| `CsvEncodingDetector.cs` | CSVの文字コード判定 |
| `MemberFileSecurityValidator.cs` | 入力ファイルのサイズ上限チェックと監査ログ用ハッシュ計算 |
| `SharedFileAccess.cs` | Excelなどで開いたままのファイルも読めるよう共有モードで開く |
| `SyncResultWriter.cs` | 同期結果CSVの監査ログ出力 |
| `AuditLogging.cs` | Serilogによる技術ログ設定、ログ出力先の一元管理 |
| `JsonUserPreferences.cs` | ユーザー設定(最終使用フォルダー等)のJSON永続化 |

### 複雑な部分

- **MSAL認証のシリアライズ** — 全呼び出しが単一の`SemaphoreSlim`を通る。サイレント取得優先→
  失敗時にシステムブラウザでの対話サインインへフォールバック。サインアウトもこのゲートを通す
  ことで、進行中のトークン取得がサインアウト後に結果を復活させてしまう競合を防いでいる。
- **原子的な書き込み**(`AtomicFileWriter.cs:16-41`) — `.{名前}.{guid}.tmp`という隠しファイルへ
  `FileMode.CreateNew`で書き込み、`Flush(true)`後に`File.Move`でリネームする古典的な手法。
  書き込み途中のクラッシュで壊れたファイルが最終パスに残らないことを保証する。監査ログCSVと
  ユーザー設定JSONの両方がこれを使う。
- **ファイルサイズ上限**(`MemberListReader`/`MemberFileSecurityValidator`) — `File.ReadAllBytes`等で
  全体を読み込む前に`FileInfo.Length`だけで超過を判定し即座に拒否する。社内限定ツールのため、
  読込中の改ざん検知や行数・列数上限といった厳密な検証は行わない。
- **文字コード判定**(`CsvEncodingDetector`) — UTF-8 BOM確認→UTF-8として妥当かをストリーミング
  検証→ダメならShift-JIS(cp932)にフォールバック。判定対象はUTF-8/UTF-8 BOM付き/Shift-JISのみ
  だが、UTF-16/UTF-32もBOM付きファイルなら`StreamReader`自身のBOM自動判定が優先されるため
  結果的に読める。BOMなしのUTF-16/UTF-32は文字コードエラー(`InvalidDataException`)になる。
- **改行コードの扱い**(`MemberTextParser`) — `ReadOnlySpan<char>.EnumerateLines()`を使わず
  `\r\n|\n|\r`で手動分割している。標準APIはU+2028/U+2029等でも分割してしまうため。
- **設定ファイル破損時の復旧**(`JsonUserPreferences`) — 読み込み失敗時は壊れたファイルを
  タイムスタンプ付きでバックアップし、既定値で継続。ログにはファイル名のみを出力し、フルパス
  (Windowsユーザー名を含む)は書かない。

---

## 6. Presentation層(WPF/MVVM)

`MainWindowViewModel`が全体の構成ルートで、`TeamSelectionViewModel`(チーム選択)/
`MemberFileViewModel`(メンバー入力)/`SyncWorkspaceViewModel`(プラン確認・実行)/
`ManualViewModel`(操作手順)/`SignInViewModel`/`WorkflowStepsViewModel`(ウィザード風の
ステップ表示)を束ねる。

### 複雑な部分

- **`SyncWorkspaceViewModel`で`BusyOperationRunner`を2つ使い分け** — プレビュー生成用と実行用
  でビジー状態を別管理し、確認ダイアログ表示中に古い進捗表示が残らないようにしている。
- **ウィンドウを閉じるときのキャンセル待ち**(`MainWindow.xaml.cs`) — 同期実行中に閉じようと
  すると、`Closing`イベントを一旦キャンセルし、`SyncWorkspaceViewModel.CancelAndWaitAsync()`で
  キャンセル完了を待ってから再度クローズする、という二段構えの状態機械になっている。
- **実行後は常に最新状態を再照合** — Presentation側でもApplication層の照合結果を使ってUI表示を
  更新する。キャンセル・部分失敗でも「実際に何が反映されたか」を再取得して表示する。

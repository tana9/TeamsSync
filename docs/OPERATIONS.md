# TeamsSync 運用ガイド

## Microsoft Entra IDアプリ登録

1. Microsoft Entra管理センターでアプリ登録を作成する。
2. 対応するアカウントの種類を組織の運用方針に合わせて選択する。単一組織でのみ使う場合は単一テナントを推奨する。
3. 「認証」で「モバイルとデスクトップ アプリケーション」を追加し、リダイレクトURIに`http://localhost`を登録する。
4. 「パブリック クライアント フローを許可」を有効にする。クライアントシークレットは作成・配布しない。
5. Microsoft Graphの委任されたアクセス許可を追加する。

   - `User.Read`
   - `User.ReadBasic.All`
   - `Team.ReadBasic.All`
   - `TeamMember.Read.All`
   - `TeamMember.ReadWriteNonOwnerRole.All`

6. 組織の同意ポリシーに応じて管理者同意を付与する。
7. アプリケーション（クライアント）IDを`Entra:ClientId`へ設定する。単一テナント運用ではディレクトリ（テナント）IDを`Entra:TenantId`へ設定する。

`TeamMember.ReadWriteNonOwnerRole.All`は一般メンバーの追加・削除に使います。所有者ロールは変更できません。不要になった配布環境では、アプリ登録の無効化または削除、管理者同意の取り消しを行ってください。

## 設定方法

埋め込み設定より環境変数、環境変数より起動引数が優先されます。

```powershell
$env:TEAMSSYNC_Entra__ClientId = "クライアントID"
$env:TEAMSSYNC_Entra__TenantId = "テナントID"
.\TeamsSync.exe
```

端末共有時は、起動引数に識別子を残すより、端末管理で配布した環境変数を推奨します。アクセストークンやクライアントシークレットを設定ファイルへ保存しないでください。

## ローカルデータの保存場所

| データ | 既定の場所 | 内容 |
|---|---|---|
| 監査ログ | `%LocalAppData%\TeamsSync\Logs\audit-*.jsonl` | 実行ID、対象ID、件数、結果、Graph相関IDなど |
| ユーザー設定 | `%LocalAppData%\TeamsSync\preferences.json` | 最後に利用したフォルダー |
| 破損設定の退避 | `%LocalAppData%\TeamsSync\preferences.corrupt-*.json` | 読み込めなかった旧設定 |
| 結果CSV | ユーザーが保存ダイアログで指定した場所 | 同期操作ごとの結果 |
| 一時マニュアル | `%Temp%\TeamsSync\Manual.html` | EXEから展開した利用者マニュアル |

入力したCSV・Excelや貼り付け内容そのものはアプリ専用領域へ保存しません。監査ログには入力ファイル名とSHA-256を記録しますが、フルパス、氏名、メールアドレス、UPN、アクセストークンは記録しません。

## データの削除

1. TeamsSyncを終了する。
2. 必要な監査ログと結果CSVを組織の記録保持方針に従って退避する。
3. `%LocalAppData%\TeamsSync`を削除すると、監査ログ、ユーザー設定、破損設定の退避を削除できる。
4. `%Temp%\TeamsSync`を削除すると、展開済みマニュアルを削除できる。
5. 結果CSVは保存先から個別に削除する。

ログは既定で日次または25MBごとにローテーションし、30ファイルを保持します。`AuditLogging:RetainedFileCount`と`AuditLogging:FileSizeLimitBytes`で組織の保持方針に合わせて変更できます。

## 権限エラー時の確認

- サインインできない場合は、ClientId、TenantId、`http://localhost`、パブリッククライアント設定を確認する。
- チームを取得できない場合は、委任されたアクセス許可と管理者同意を確認する。
- メンバー変更だけ失敗する場合は、`TeamMember.ReadWriteNonOwnerRole.All`、実行者のチーム所有者権限、対象チームが動的グループでないことを確認する。
- 問い合わせ時は監査ログの実行ID、`request-id`、`client-request-id`を使用し、アクセストークンを共有しない。

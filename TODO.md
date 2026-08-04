# TODO

TeamsSync の今後の改善項目。メンバー削除を伴うアプリケーションのため、安全性と追跡可能性を優先する。

全項目が完了したセクションは`TODO_DONE.md`へ移動している。過去の経緯を確認したい場合はそちらを参照。

## 優先度: 高

### コード全体レビューで見つかった問題を修正する(2026-08-04)

- [x] メンバー応答の解析失敗(`InvalidDataException`)がバッチ取得・個別フォールバックのどちらでも捕捉されず、所有チーム一覧取得全体が失敗する（`TeamMembersBatchFetcher.cs`のバッチ解析、`GraphTeamsGateway.TryFetchMembersIndividuallyAsync`の両方に`InvalidDataException`のcatchを追加し、`GraphException`時と同様にそのチームだけ判定をスキップして他チームの処理を継続するようにした）
- [x] 設定ファイルパス(Windowsユーザー名を含む)が監査ログにそのまま記録される（`JsonUserPreferences`の警告ログを、フルパスではなく`Path.GetFileName`のファイル名のみを記録するよう変更。利用者向け`LoadWarning`メッセージは従来どおりフルパスを表示する）
- [x] `MsalAuthenticationService.SignOutAsync`が排他制御なしで`_result`を操作する（`GetTokenAsync`と同じ`_tokenGate`で直列化した）
- [x] 同期実行直前のキャンセルが「反映できませんでした」という重大エラーとして誤表示される（`RunSyncAndReconcileAsync`内で、実行直前の内部再検証(`RevalidateAndExecuteAsync`)が投げる`OperationCanceledException`をその場で捕捉し、通常のキャンセル完了と同様に案内するよう変更）

完了条件: 上記いずれも再現条件下で機能停止・情報漏えい・誤表示が発生しない。

## 優先度: 中

### コード全体レビューで見つかった軽微な問題を修正する(2026-08-04)

- [x] `SyncPlan.EnsureExecutable()`が`CanExecute`と異なり`HasNoActionableChanges`を検査しない（`EnsureExecutable()`に`HasNoActionableChanges`のチェックを追加し`CanExecute`と条件を揃えた）
- [x] `SyncPlan.WithoutChanges`(個別除外)が配線された際、`IsEquivalentTo`が`Excluded`を比較対象外にしているため再検証が常に「状態変化あり」と誤判定する（`IsEquivalentTo`が個別除外したユーザーIDを両プランの比較対象から取り除くよう変更し、除外の有無自体は再検証の不一致要因にならないようにした）
- [x] `SyncResultWriter`の結果ログファイル名に長さ上限がなく、極端に長いチーム名でパス長超過により監査ログ保存だけが失敗しうる（`SanitizeFileName`にチーム名部分の長さ上限(100文字)を追加し切り詰めるようにした）

完了条件: 上記いずれも再現条件下で不整合・保存失敗が発生しない。

### コード品質レビューで見つかった重複・デッドコード・命名を整理する(2026-08-04)

- [x] ログ保存先パス`%LocalAppData%\TeamsSync\Logs`の組み立てが`StartupFailureLog.cs`、`AuditLogging.cs`、`SyncResultWriter.cs`の3箇所に重複している（`AuditLogging.LogDirectory`を共有し、他2箇所はそれを参照するよう変更）
- [x] Graphのホスト名`graph.microsoft.com`が6箇所に散在し、検証方式も`GraphEndpointValidator`(厳格)と`MsalAccessTokenProvider`(Kiota既定、ホスト名のみ)の2系統に分かれている（`GraphEndpoints`を新設し、ホスト名・ベースURIを1箇所へ集約。検証方式自体は用途が異なるため2系統のまま定数だけ共有した）
- [x] OData用シングルクォートエスケープ`Replace("'", "''")`が`GraphUserSearchService.cs`内に2箇所ある（`EscapeODataLiteral`ヘルパーへ抽出）
- [x] SHA-256計算パターンが`MemberFileSecurityValidator.ComputeSha256`と`MemberTextParser.cs`に別々に実装されている（`byte[]`オーバーロードを追加し共有）
- [x] 貼り付け入力のヒント文言`"1行につき1ユーザー（氏名またはメールアドレス）"`が`MemberFileInputStates.cs`、`MemberFileViewModel.cs`、`MemberInputSelectionCoordinator.cs`の3箇所にハードコードされている（`MemberPasteInputState.DefaultInfoText`定数へ集約）
- [x] `GraphHttpClient.SendAsync`(`replaceNames`引数付き)がどこからも呼ばれていない未使用コードを削除する
- [x] `Application/DependencyInjection.cs`が`SyncPlanService`/`SyncExecutor`を具象型としても二重登録しているが、具象型を直接要求する箇所がないため不要（インターフェースのみの単純登録へ変更）
- [x] `MemberListInputView.xaml.cs`だけ`MemberInputMethod`列挙型を使わず生の`1`を比較している（`(int)MemberInputMethod.Paste`に統一）
- [x] `GraphTeamsGateway`と`GraphSdkClient`だけ旧来のフィールド+コンストラクター記述で、同フォルダの他クラスはプライマリコンストラクター（両方ともプライマリコンストラクターへ変更。`GraphSdkClient`は認証プロバイダーの構築をフィールド初期化子の制約(他の非staticフィールドを参照不可)のため`Create`メソッド内へ寄せた）
- [x] `SyncExecutionResult`/`SyncExecutionOutcome`/`SyncExecutionAttempt`という近い名前の3段階ラップ構造が、名前だけでは包含関係を読み取りにくい（`SyncExecutionResult`を`SyncOperationsResult`へリネームし「Graphへの操作結果そのもの」であることを明示。3クラスとも包含関係を説明するコメントを追加。71箇所・16ファイルにまたがる変更だったため`Outcome`/`Attempt`は影響範囲を抑えるためリネームせずコメントのみで補強した）
- [x] `MemberTextParser.Parse`が全体長・行分割・タブ/制御文字検出・行長・パース・重複排除を1メソッドで担っている（行単位の検証(タブ・制御文字・行長)を`ValidateLine`へ抽出）
- [x] `SyncPlan`レコードが`CanExecute`/`EnsureExecutable`/`WithoutChanges`/`IsEquivalentTo`と業務ロジックを多く抱えている（同値比較ロジックを`SyncPlanEquivalence`静的クラスへ切り出し）
- [x] `SyncExecutionCoordinator.ExecuteAsync`内の「try実行→成功/失敗を変数化」というtry/catchパターンがログ保存とreconciliationの2箇所でほぼ同型（`TryRunAsync<T>`共通ヘルパーへ抽出）
- [x] `MemberFileViewModel`が貼り付け文書のクリアを`_selectionCoordinator`経由と直接`_documents.SetPastedDocument(null)`呼び出しの2通りの経路で行っている（解析失敗時の直接呼び出しを`_selectionCoordinator.InvalidatePastedDocument()`へ統一）

完了条件: 重複箇所が単一の実装に集約され、未使用コードが削除され、命名・構文スタイルがプロジェクト内で一貫している。

### 指定した複数メンバーだけをまとめて削除できるようにする

- [x] 同期モードに`指定メンバーを削除`を追加し、`追加のみ`や`完全同期（リスト外を削除）`とは入力リストの意味が異なることを選択肢の近くへ明示する
- [x] CSV、Excel、テキスト貼り付けで入力したユーザーをTeams上のメンバーと照合し、現在所属している一般メンバーだけを削除候補にする
- [x] 同じユーザーがUPN、メールアドレス、氏名などの別表記で重複していても、解決後のユーザーIDで一意化して削除要求を1回だけ送る
- [x] 差分一覧で`削除対象`、`所有者のため保護`、`チームに存在しない`、`ユーザーを特定できない`を区別し、削除対象者を実行前に確認できるようにする
- [ ] 差分一覧から削除候補を個別に除外できるようにし、除外後の件数と最終確認内容を同期させる
- [x] 所有者、未解決ユーザー、曖昧な氏名を削除せず、未解決または曖昧な入力が1件でもある場合は実行を開始できないようにする
- [x] 削除対象が0件の場合は実行不可とし、チーム未所属、所有者保護、手動除外など理由別の件数を表示する（手動除外の件数表示は個別除外機能とあわせて実装する）
- [x] 最終確認ダイアログで対象チーム、`指定メンバーを削除`モード、削除人数、対象者一覧を表示し、削除操作であることを明確にする
- [x] 実行直前にチームメンバーと所有者情報を再取得し、対象者が引き続き一般メンバーである場合だけ削除する。プレビューから状態が変わっていた場合は中断して最新の差分を再表示する
- [x] 結果CSVを実行前プラン全体を基準に出力し、キャンセルで未着手の対象も「未実行」として欠落させず記録する
- [ ] 対象者ごとの成功、失敗、未実行を結果画面と監査ログへ記録し、キャンセルまたは部分失敗後はGraph上の最新状態から未反映分だけを再計画する（監査ログは個人情報を記録しない方針を維持し、状態別件数と対象IDで追跡可能にする）
- [ ] 指定削除では追加操作やリスト外メンバーの削除が発生しないこと、所有者保護、重複入力、未所属、曖昧入力、手動除外、実行直前の状態変化、部分失敗、キャンセルを自動テストする
- [x] README、利用者マニュアル、組み込みマニュアル、リリースチェックリストへ操作方法と安全上の注意を追記する

完了条件: 利用者が入力した複数の一般メンバーだけをプレビューで確認してまとめて削除でき、入力していないメンバー、所有者、曖昧または未解決のユーザーを誤って削除しない。

### 同期結果CSVの数式注入を防止する

- [x] チーム名、メールアドレス、エラー文など外部由来の値が`=`, `+`, `-`, `@`で始まる場合に数式として解釈されない形式へ無害化する
- [x] 先頭のタブ、改行、空白後に数式開始文字があるケースも表計算ソフトの挙動を確認する
  (先頭空白等をトリムしてから判定し、シングルクォートは元の値の先頭に付与)
- [x] 通常値、数式風文字列、引用符、改行を含む値のCSV出力テストを追加する
- [ ] Excelで出力ファイルを開き、式や外部リンクが実行されないことを確認する (実機Excelでの手動確認が必要、未実施)

完了条件: テナントやGraph APIから取得した文字列を含む結果CSVを表計算ソフトで開いても、セルが数式として評価されない。

### 同期実行の安全ゲートと取りこぼし記録を修正する

- [x] 実行直前の再検証結果(`MembershipSnapshot`)を`SyncPlan.EnsureExecutable`または`SyncExecutor.ExecuteAsync`の実行判定に組み込み、プレビュー後にOwnerへ昇格したメンバーが削除されないようにする（呼び出し順序に依存しない構造的な保証にするため、`SyncExecutionCoordinator.RevalidateAndExecuteAsync`を新設し、実行直前に必ず`RevalidatePlanAsync`で再検証してから実行するよう変更。`SyncWorkspaceViewModel`側の既存の事前チェックはUX上そのまま残し、二重の防御とした）
- [x] `AddressResolver.TryResolveFromRoster`が氏名一致で確定した場合でも、ディレクトリ側に別人の候補がないか確認するか、氏名一致はメール一致より確度が低いことをUI・監査ログへ明示する（追加のGraph検索は行わず、`AddressResolution.MatchedByNameOnly`で確度を判別できるようにし、差分一覧に`ChangeReason.AlreadyMemberNameMatchOnly`/`RemoveSpecifiedNameMatchOnly`として表示。指定削除モードでの誤削除リスクが特に高いため両モードに対応した）
- [x] FullSyncで同姓同名の衝突(`AmbiguousCurrentMember`)を起こした現メンバーが`wantedIds`に含まれず削除候補として表示される挙動を修正する（`AddressResolution.AmbiguousMembers`で衝突した現メンバーを`SyncPlanFactory`まで伝搬し、`ComputeRemovals`の`wantedIds`へ合流させて二重表示を解消）
- [x] キャンセル時、サーバー側では成功していた操作が`OperationCanceledException`により未記録のまま取りこぼされる問題と、キャンセル時に限り`ReconcileAsync`(最新状態との差分検出)がスキップされる問題を修正する（`SyncOperationResult.Uncertain`を追加してキャンセル時も操作を記録するようにし、`SyncExecutionCoordinator.ExecuteAsync`はキャンセル時も`CancellationToken.None`で`ReconcileAsync`を実行するよう変更。ViewModel側もキャンセル後に最新の未反映件数を表示するよう統合）

完了条件: 実行直前の状態変化とキャンセル時の取りこぼしが検出・記録され、同姓同名の衝突が差分表示や削除判定を誤らせない。

### 認証の安全性を高める

- [x] `MsalAuthenticationService`の共有結果フィールドを排他化し、`GetOwnedTeamsAsync`の並列トークン取得で誤ったアカウントのトークンが返る、または複数の対話サインインが同時に起動する競合を解消する（`GetTokenAsync`全体を`SemaphoreSlim(1,1)`で直列化）
- [x] `TenantId`設定の変更が、`ClientId`が同じ場合キャッシュされた`IPublicClientApplication`へ反映されない問題を修正する（`_configuredTenantId`を追加し、ClientIdまたはTenantIdのいずれかが変わればアプリを再構築するよう変更。再構築で古いテナントのサインイン状態は失われ再サインインが必要になるが、テナント制限の即時反映を優先した）

完了条件: 認証トークンの取り違え・同時サインイン、テナント制限の無効化が発生しない。

## 優先度: 中

### コード品質レビューで見つかった重複・命名を整理する(2026-08-03)

- [x] `SyncExecutionCoordinator`を、同フォルダの他クラスと同様プライマリコンストラクターへ揃える（`Application\Services\SyncExecutionCoordinator.cs:8-20`）
- [x] `Application\Abstractions\RuntimeServices.cs`のXMLコメント末尾に残る「。」を削除する
- [x] `TeamModels.cs`の`AddCount`/`RemoveCount`等7件の件数プロパティを`CountOf(ChangeKind)`ヘルパーへ集約する（`Domain\Teams\TeamModels.cs:130-148`）
- [x] `SyncPlanFactory.cs`の重複アドレス結合区切り文字`" ／ "`を名前付き定数化する（`Domain\Teams\SyncPlanFactory.cs:73`）
- [x] `GraphHttpClient.SendOnceAsync`と`GraphSdkTransportHandler.SendAsync`のエラー判定・ログ出力・`GraphException`化ロジック（コメント含め重複）を共通ヘルパーへ切り出す（`GraphErrorHandler`を新設）
- [x] `Required`/`Optional`ヘルパーが`GraphHttpClient`・`GraphResponseParser`・`GraphTeamsGateway`の3箇所に分散重複しているのを1箇所へ集約する（`GraphResponseParser`へ統一）
- [x] `GraphSdkClient`の`GetJoinedTeamsAsync`/`GetTeamMembersAsync`/`FindUsersAsync`に重複するページング処理をジェネリックヘルパーへ抽出する（`CollectAllPagesAsync<TItem, TResponse>`）
- [x] `GraphTeamsGateway`が`TeamOwnershipCache`/`TeamMembersBatchFetcher`/`GraphUserSearchService`を`new`で直接生成しておりモック差し替えができない問題をDI経由に変更する（`TeamMembersBatchFetcher`/`GraphUserSearchService`をコンストラクター注入化。`TeamOwnershipCache`はパラメーターなしのため据え置き）
- [x] Graphページサイズ上限`999`が`GraphSdkClient.cs`と`TeamMembersBatchFetcher.cs`に生の数値で重複しているのを共有定数化する（`GraphHttpClient.MaxMembersPageSize`）
- [x] `MsalAuthenticationService.GetTokenAsync`が対話サインイン分岐を例外のスロー・キャッチで表現している箇所を、素直な条件分岐へ書き換える
- [x] `SyncResultWriter.WriteCsv`の、本番コードパスでは到達不能な後方互換フォールバック分岐とその誤解を招くコメントを削除する（実装は既存テスト(`CreatePlan`ヘルパーで空プランを使う複数のテスト)がこの分岐へ依存していたため削除は見送り、コメントを実態(テスト用の簡易経路)に合わせて修正するに留めた。`Infrastructure\Files\SyncResultWriter.cs:54-63`）
- [x] `CsvEncodingDetector`と`MemberListReader`に重複する`OpenShared`ヘルパー(実装・コメントとも同一)を共有ヘルパーへ集約する（`SharedFileAccess`を新設）
- [x] `MemberListReader`が`MemberFileSecurityValidator`の`internal`定数をそのまま再公開している未使用の`public const`4つを削除する（`InternalsVisibleTo`済みでテストは直接参照可能）
- [x] `MemberTextParser`の本番未使用・インターフェース外の公開オーバーロード`Parse(string)`を削除または`internal`化する（削除）
- [x] `CsvEncodingDetector.cs`のバッファサイズ`8192`を名前付き定数化する
- [x] 確認ダイアログの骨格（アイコン・ボタン構成）が`WpfMemberInputConfirmationService`(2箇所)と`WpfSyncConfirmationService`で重複しているのを`ConfirmationDialogHelper`のファクトリメソッドへ集約する（`BuildConfirmDialog`を新設）
- [x] `WpfNotificationService`が`ConfirmationDialogHelper.BuildTitle`と同じタイトル構築を再実装している箇所を共通化する（`BuildTitle`にアイコン・強調色の引数を追加して共用）
- [x] `MemberListInputView`/`SyncModeSelectorView`/`TeamSelectionCardContent`/`SyncDiffCardContent`で完全重複している手順見出しXAMLブロックを共有`Style`かユーザーコントロールへ切り出す（`StepHeaderView`ユーザーコントロールを新設）
- [x] `MemberListInputView`と`TeamSelectionCardContent`で重複する「未入力警告」バナーXAMLを共有`Style`へ切り出す（`StepIncompleteWarningView`ユーザーコントロールを新設）
- [x] `IUserInteractionService.cs`に同名の型が存在しない（6つの無関係な型の寄せ集め）問題を、実態に合わせてファイル名を変更するかファイル分割で解消する（`PresentationPorts.cs`へリネーム）
- [x] XAMLのコメントに残る末尾「。」を、.csファイルの規約（末尾の句点は付けない）に合わせてトリムする
- [x] `MemberFileViewModel`/`MemberInputDocumentState`/`MemberInputSelectionCoordinator`にまたがる入力方法(0/1)の生リテラル比較を、`enum MemberInputMethod`を使った比較へ置き換える（`SelectedInputIndex`自体はTabControlバインド用にint型のまま維持し、比較箇所だけ`(int)MemberInputMethod.Xxx`へ置換）
- [x] `MemberFileViewModel.OnSelectedInputIndexChanged`が`NotifyDocumentChanged()`と同じ処理を再実装している箇所を、呼び出しへ置き換える
- [x] `TeamMemberImportViewModel`が`MemberFileInputCoordinator.ParseAsync`と同じ`Task.Run`解析パターンを再実装している箇所を、コーディネーター共有へ変更する
- [x] `SyncWorkspaceCommandStateEvaluator`だけが不要にインスタンスクラスになっている（同役割の`SyncWorkspaceTextFormatter`は`static`）のを揃える
- [x] `WorkflowStepState`/`WorkflowStepsViewModel`のコメントが「4 同期差分」を含む4手順を謳うが、実装は`Step1State`〜`Step3State`の3つのみである不一致を解消する（手順4はSyncWorkspaceViewModel側で個別管理する旨を明記）

完了条件: コードレビューで指摘された重複ロジック・マジックナンバー・コメント規約違反・命名の不整合が解消され、同種の処理が単一の実装箇所に集約されている。

### (副次的に発見)起動時クラッシュを修正する

- [x] `SyncActionBarView.xaml`/`SyncDiffCardContent.xaml`の`ProgressBar.Value`が、既定でTwoWayバインドされる`RangeBase.ValueProperty`のまま読み取り専用の`ProgressValue`へ束縛されており、起動直後のレイアウトパスで`InvalidOperationException`が発生しアプリが必ず落ちる状態だった（今回のコード品質修正とは無関係の既存バグ。コミット履歴で存在を確認済み）。両箇所へ`Mode=OneWay`を明示して修正し、`TeamsSync.UiHarness`で実際に起動して解消を確認した

完了条件: `MainWindow`が例外なく起動し、差分確認・同期実行の進捗バーが表示される。

### ViewModelの責務と状態更新をさらに整理する

- [x] `SyncWorkspaceViewModel`から差分プラン、一覧フィルター、削除警告の状態を`SyncPlanDisplayState`へ分離する
- [x] `MemberFileViewModel`に分散しているコマンド実行可否の更新を一つのメソッドへ集約する
- [x] `SyncResultDisplayState`の公開setterを廃止し、実行開始・結果適用・未反映件数更新・確認失敗・クリアの目的別APIに限定する
- [ ] `MainWindowViewModel`と`WorkflowStepsViewModel`のイベント購読について、Singleton前提を明示するか解除可能な購読管理へ統一する
- [x] `MemberFileViewModel`のファイル読込状態と貼り付け解析状態を小さな状態モデルへ分離し、文書切り替えと非同期処理の責務を明確にする（読込・解析状態と、ファイル文書・貼り付け文書・選択中文書の状態を専用モデルへ分離）
- [x] `SyncWorkspaceViewModel`の確認、再検証、同期実行、キャンセル、最終状態取得をアプリケーション層の調整クラスへ寄せられるか検証し、ViewModel固有の通知・フォーカス処理と分離する（プラン作成・再検証・同期実行・実行後再計画は`SyncExecutionCoordinator`へ集約。確認ダイアログ、キャンセル通知、画面状態反映はUI依存のためViewModelに保持）
- [x] `ChangeFilter.Count`の未確認状態を`-1`ではなく`int?`で表し、件数の有無を型で判別できるようにする

完了条件: 各ViewModelの責務と状態更新経路が明確で、機能追加時のコマンド通知漏れや不整合状態を防止できる。

### コード品質と保守性を高める

- [x] `SyncWorkspaceViewModel`と`MemberFileViewModel`の責務を、コマンド実行可否、入力文書処理、同期結果表示などの単位へさらに分割し、ViewModelの肥大化を抑える（`SyncWorkspaceContext`、`SyncExecutionRunner`、`SyncWorkspaceCommandStateEvaluator`、`SyncResultPresenter`、`MemberFileInputCoordinator`、`MemberInputSelectionCoordinator`へ分離し、入力状態は既存の専用状態モデルへ集約）
- [ ] 同期調整やファイル読込の広すぎる`catch (Exception)`を想定例外と予期しない例外に分類し、後者をログへ記録して原因を追跡できるようにする
- [x] 現在時刻と`Guid`生成を`TimeProvider`または専用サービス経由にし、結果ファイル名、監査ログ、設定バックアップのテストを決定論的にする（`TimeProvider`と`IIdentifierGenerator`をDI登録し、ファイル名、Graph診断ID、監査ID、一時ファイル名へ適用）
- [x] WPFイベント境界の`async void`を薄いアダプターに限定し、実処理を`Task`戻り値のメソッドへ集約して例外処理とテスト容易性を高める（起動・終了・Closingの本体を`Task`メソッドへ分離）
- [x] `.editorconfig`とRoslyn analyzerで非同期処理、例外処理、命名、複雑度などの静的検査をCIへ追加する（SDK analyzerを有効化し、レビュー済みの基準ルールを明示）

完了条件: 主要ViewModelの変更影響範囲と障害原因を追跡しやすく、時刻や乱数に依存しないテストと静的検査で品質を継続的に維持できる。

### コードレビューで見つかった残りの防御的修正

- [x] `GraphSdkClient.AddMemberAsync`が受け取る`userId`をエスケープまたは検証し、将来GUID以外の値が渡された場合のOData束縛文字列注入を防ぐ（`Guid.TryParse`で検証し、GUID形式でなければ通信前に`ArgumentException`を投げるよう変更）
- [x] 貼り付けテキストの制御文字フィルタがUnicode行区切り・段落区切り(U+2028/U+2029)を素通りさせる問題を修正する（`char.IsControl`に加えて`char.GetUnicodeCategory`が`LineSeparator`/`ParagraphSeparator`を返す文字も拒否するよう変更）
- [x] Excel読込で列数不一致の行が無警告で除外される挙動をCSVと揃え、除外・欠落をエラーまたは警告として明示する（`ReadExcel`にCSVの`ReadCsv`と同じ「1行目と列数が異なれば行番号付きで例外」チェックを追加。`ExtractColumn`の`.Where(r => r.Length > column)`による無警告の除外に頼らないようにした）
- [x] 同期中にウィンドウを閉じる際のガード(`_closeAfterCancellation`)を`await`前に設定し、待機中の再クローズ操作で二重キャンセル・未処理例外が起きないようにする（`_cancellingBeforeClose`を追加し、キャンセル待機中の再クローズ操作は`CancelAndWaitAsync`・`Close()`を再実行せず保留するよう`DecideCloseAction`として切り出し。`DecideEscapeAction`と同様の形でテスト可能にした）
- [x] 「テキストとして編集」確認ダイアログ後のフォーカス復元が、直後のタブ切替でボタンがビジュアルツリーから外れて失敗する問題を修正する（`ShowRestoringFocusAsync`の汎用復元(旧フォーカス先への復元)には頼らず、`CopyFileContentToTextAsync`でタブ切替後に既存の`InputFocusRequested`を発行し、切替後に有効な貼り付けテキスト欄へ明示的にフォーカスするよう変更）

完了条件: 上記いずれも再現条件下で例外・情報欠落・フォーカス消失が発生しない。

### 初回利用時の操作順序と同期モードを分かりやすくする

- [x] 画面上の手順番号と実際の操作順を一致させ、無番号の「同期モード」を独立した手順として扱う（「同期モード」を専用カードへ分離し「3
  同期モード」、差分カードを「4 同期差分」へ繰り上げた）
- [x] 未完了・現在・完了のステップを視覚的かつ読み上げ可能に示す（バッジ方式は廃止し、各手順が「現在の操作」の間だけブロッカー箇所の直近に赤いエラーメッセージを表示。当該
  `TextBlock`に`AutomationProperties.LiveSetting="Polite"`を設定して読み上げ可能にした）
- [x] 「追加のみ」と「完全同期」の違いを選択肢の近くに表示する（`SyncModeSelectorView.xaml`
  に常時表示テキストを追加していたが、画面の高さを圧迫し文字も薄く読まれにくかったため撤回。各ラジオボタンの`ToolTip`
  へ移動した。実行前の最終確認ダイアログには同内容が引き続き明記される）
- [x] 「完全同期」はリストにいない一般メンバーを削除することと、所有者は削除されないことを選択時に明示する（同上のテキストに明記。最終確認ダイアログにも同内容を表示）
- [x] サインイン前や入力不足で広い範囲を無効化するだけでなく、各領域に利用できない理由と解決操作を表示する（当初はヘッダー直下の
  `InfoBar`
  1本で「次の操作」を案内していたが、同期モードのように既定値がありメッセージと実操作が噛み合わない手順があった。全カードに同じサインイン案内を重複表示していたのがくどいとの指摘もあり撤回。現在は各手順が「現在の操作」の間だけ、実際にブロックしているコントロールの直近1箇所に赤いエラーメッセージを表示する方式にした:
  サインインボタン下/チーム選択欄下/メンバーリスト欄。同期モードは既定値があり選択操作が不要なため、代わりに「差分を確認」ボタンをPrimary外観で強調する）
- [ ] 初回利用者がマニュアルを見なくても、サインインから安全なプレビューまで到達できるかユーザビリティ確認を行う（実利用者による読み合わせが必要なため未実施。GUIの実見た目も確認できないため保留）

完了条件: 初めて使う非エンジニアが、現在位置、次の操作、各同期モードの影響を画面だけで説明できる。

### 動的な画面更新後のフォーカスと読み上げを整える

- [x] 差分確認後は差分の集計または最初のエラーへ、入力失敗後は修正対象へフォーカスを移す（
  `SyncWorkspaceViewModel.DiffFocusRequested`と`MemberFileViewModel.InputFocusRequested`イベントを追加し、
  `SyncDiffCardContent.xaml.cs`/`MemberListInputView.xaml.cs`で購読してフォーカス移動）
- [x] 同期モード変更による差分クリア、再検証による差分更新、同期完了・部分失敗をスクリーンリーダーへ一度だけ明確に通知する（既存の
  `StatusText`(`AutomationProperties.LiveSetting="Polite"`)を維持しつつ、`ApplyPlan`に`announceStatus`
  引数を追加し、再検証・再取得後に既定の案内文と具体的な結果メッセージが二重に読み上げられないよう修正）
- [x] 読込中オーバーレイ表示時に背後のDataGridへキーボードフォーカスが入らないようにする（`SyncDiffCardContent.xaml`
  のDataGridに`IsBusy`時`IsEnabled=False`とするスタイルトリガーを追加）
- [x] ダイアログを閉じた後、操作元のボタンへフォーカスが戻ることを確認する（
  `ConfirmationDialogHelper.ShowRestoringFocusAsync`
  で最終確認と続行不能な重大エラーダイアログに適用。通常エラーはフォーカスを奪わない持続型Snackbarへ移行。実機Narrator操作での見た目確認は未実施）
- [ ] キーボードのみ、Narrator、200%表示を組み合わせた一連の操作テストをリリース確認へ追加する（手動確認項目のため未実施。ACCESSIBILITY.mdの既存チェックリストで代替）

完了条件: マウスを使わない利用者が、動的更新で現在位置を失わず、処理結果と修正箇所へ移動できる。

### 利用者マニュアルを非エンジニア向けに再構成する

- [x] 冒頭に「このアプリで起きること」「事前に必要な権限」「最も安全な初回手順」を1ページ相当でまとめる
- [x] UPN、一般メンバー、所有者、完全同期などの用語を初出時に平易に説明し、`InfoBar`や`Graph API`など操作に不要な実装用語を避ける
- [x] そのまま試せる1列のCSV入力例を追加する
- [ ] 画面上で確認すべき場所を示す図または注釈付き画像を追加する
- [x] 完全同期、キャンセル、部分失敗について「実行済み操作は元に戻らない」ことと復旧手順を具体例で説明する
- [x] 通信レベルの自動再試行と、失敗後に利用者が行う再実行を区別して説明する
- [x] 「結果が画面に残る」など実装と一致しない表現を修正し、各ボタン名・手順番号を画面と同期する
- [x] よくあるトラブルを「表示・状況／原因／利用者が行うこと」の表にし、管理者へ伝える情報を併記する
- [ ] 非エンジニアの代表利用者による、初回操作と障害復旧の読み合わせを行う
- [x] `MANUAL.md`更新時に埋め込み`Manual.html`の再生成漏れをCIで検出する

完了条件: Entra IDやGraphの知識がない利用者が、管理者の助けが必要な場面を判断し、安全なプレビュー、同期、失敗後の復旧をマニュアルだけで実施できる。

### 画面サイズと同期操作の導線を改善する

- [x] `MinHeight`を見直し、入力領域をスクロール可能にして1024×768でも操作できるようにする
- [ ] 表示倍率125%、150%、200%で主要ボタンと差分一覧が利用できることを確認する
- [x] 下部のステータス、進捗、実行ボタンを常に確認できる配置にする
- [x] 空状態に「チームを選択」「ファイルを選択」「差分を確認」の操作導線を表示する
- [x] 同期実行ボタンが無効な理由をツールチップまたはステータスで表示する

完了条件: 小さい画面や高DPI環境でも、次に必要な操作と同期できない理由を把握できる。

### 所有チーム検索を高速化する

- [x] 現在のチーム単位の逐次メンバー取得について計測する
- [x] 所有判定結果をアプリのセッション内でキャッシュする
- [x] `SemaphoreSlim`などによる上限付き並列取得を検証する
- [x] 読み取り処理へのGraph JSONバッチ適用を検証する（`$batch`で最大20チームずつメンバー取得をまとめる）
- [x] 各バッチ項目の成否を個別に処理する（失敗したチームだけ個別リクエストへフォールバック）
- [ ] 429を増加させない並列数を決定する

注意: メンバー変更APIはスロットリングを考慮し、単純な一括並列化を行わない。

完了条件: 多数の所有チームがある環境でもUIをブロックせず、スロットリングを悪化させずに一覧を取得できる。

### 差分確認のユーザー解決並列数を検証する

- [x] `$batch`によるバッチ化を試作したが、ユーザー解決では見送り、個別リクエスト並列化を維持する
- [x] `AddressResolutionConcurrency`を10から15へ微増する
- [ ] 数百件規模のメンバーリストで実測する
- [ ] 429が増える場合は並列数を調整するか、リトライ待機時間を見直す

完了条件: 大きめのメンバーリストでも429の増加なく差分確認が完了する。

### メンバー照合と大量差分表示を高速化する

- [ ] 入力100・1000・5000件、現在メンバー100・1000件程度の組み合わせで、差分作成時間、Graphリクエスト数、割り当てメモリ、一覧反映時間の基準値を計測する
- [x] 現在メンバーをメールアドレス・正規化氏名・ユーザーIDの辞書へ索引化し、入力1件ごとの全メンバー走査を避ける（同姓同名は複数候補として保持し、現在どおり安全側でエラーにする）
- [x] 差分一覧の`ObservableCollection`へ1件ずつ追加する更新を、一括置換とReset通知1回へ変更し、フィルター・件数計算・
  `ICollectionView`更新を最後に1回だけ行う
- [x] 1回のプレビュー内で同じ識別子のディレクトリ検索を共有し、重複入力によるGraphリクエストを避ける
- [x] プレビュー進捗を25件単位と最終件にまとめ、大量入力時のUIスレッドへの通知回数を抑える
- [x] 差分作成ログへ入力件数、重複除外後件数、Graph検索数、処理時間を記録し、性能変化を比較できるようにする
- [ ] プレビュー時に解決したユーザーIDを同期プランへ保持し、実行直前の再検証では最新チームメンバーを再取得しつつ、不要なディレクトリ検索を再実行しない方式を検証する（ユーザー削除・無効化・メール変更時も安全側で実行を中止する）
- [ ] 5000件分の待機Taskを先に生成する方式と、`AddressResolutionConcurrency`
  個のワーカーが入力キューを処理する方式を実測比較する（固定ワーカー化の試作はWPFのCollectionView更新スレッドへ影響したため撤回。明確な改善が確認できる場合だけUIスレッド復帰を明示して再検討する）
- [ ] CSV読込で全行・全列を保持せず、ヘッダー判定後は対象列と列数検証に必要な情報だけを保持するストリーミング方式を検討する
- [ ] XLSX読込の時間とピークメモリを実測し、ClosedXMLがボトルネックである場合に限りOpen XML SDK等による先頭シート・対象列限定の読込を検討する
- [ ] 最適化前後で差分内容、同姓同名判定、所有者保護、キャンセル、進捗件数が変わらないことを回帰テストする

完了条件: 数千件規模でも安全判定と進捗表示を維持したまま、Graphリクエストの重複、不要な全件走査、UIへの行単位通知を削減し、実測値で改善を確認できる。

### 同期実行の操作間ディレイを見直す

- [x] `TeamSyncService.ExecuteChangeAsync`が追加・削除操作ごとに固定2秒のディレイを挟んでおり、数百件規模の完全同期では実行時間が線形に伸びる点を見直す
  (コードレビューで指摘、2026-08-02)
- [x] Microsoft Graph公式のTeamsスロットリング制限 (1チームあたりアプリ1つにつき秒4リクエストまで)
  を確認し、上限に約20%の余裕を残す300msへ短縮した (`OperationThrottleDelay`)
- [x] `POST /teams/{team-id}/members`の「リソースあたり4rpm」表記と「秒4リクエスト」表記が公式ドキュメント内で食い違ったままであることを確認した
  (2026-08-02、`throttling-limits`と`throttling-teams.md`の両方で再確認)。300msは緩い方 (秒4リクエスト)の解釈に基づく未検証の前提のまま
- [x] 書き込み (POST/DELETE)クライアントは非冪等操作のため429以外 (503・タイムアウト等)
  の自動再試行を無効化していたが、429は処理前の明示的な拒否で重複実行の懸念がないため例外的に再試行を許可した
  (`DependencyInjection.AllowThrottlingRetryForUnsafeHttpMethods`、2026-08-02)
  。これにより300msの見積もりが外れていても同期失敗ではなく待機の増加で吸収されるようになったが、実際のリクエスト量が上限を超えている根本原因は解消していない
- [ ] 変更後、実テナントでスロットリング (429)の増加がないことを実測する (自動テストと実装は完了。実測は`非本番テナントで結合テストする`
  の一環として実施)

完了条件: 大規模な完全同期でも不要な待機を減らしつつ、429の増加を招かない。

### 依存パッケージを更新する

- [x] Microsoft.NET.Test.Sdk `18.0.1`から`18.8.1`へ更新する
- [x] ClosedXML `0.105.0`から`0.105.1`へ更新する
- [x] CommunityToolkit.Mvvm `8.4.0`から`8.4.2`へ更新する
- [x] Microsoft.Extensions.Hosting/Http `10.0.0`から`10.0.10`へ、Http.Resilienceを`10.8.0`へ更新する
- [x] Microsoft.Identity.Client `4.77.0`から`4.87.0`へ更新する
- [ ] ビルド、単体テスト、ログイン、ファイル読込、同期プレビューのスモークテストを行う
- [x] 脆弱性と更新状況をCIで定期確認する

完了条件: 全テストと主要な手動スモークテストが成功し、既知の非互換がない。

## 優先度: 低・配布準備

### ファイルの読取結果をテキストとして編集できるようにする

- [x] ファイル読込成功後に、抽出した識別子を貼り付け入力へコピーする「ファイル内容をコピーして編集」操作を追加する（ファイル自体を更新する機能ではないことが伝わる名称にする）
- [x] 操作後は「テキスト貼り付け」タブへ切り替え、元ファイルではなく編集後のテキストが同期元になることと、編集後に「入力を反映」が必要なことを表示する
- [x] 既存の貼り付け内容がある場合は、Teamsからのメンバー取り込みと同様に置き換え確認を表示し、キャンセル時は入力と有効な文書を維持する
- [x] 現行のファイル読込結果は検出した1列の識別子だけを保持しているため、まずはその値を1行1ユーザーでコピーする（メール列なら
  `user@example.com`、氏名列なら`山田 太郎`）
- [ ] 将来、ファイル読込で氏名列とメール列の両方を保持する場合に限り、`山田 太郎 <user@example.com>`形式への変換を検討する
- [x] ファイルからの変換、タブ切替、置き換え確認、キャンセル、変換後の編集と再反映をViewModelテストで確認する

完了条件: 利用者が元ファイルを変更せず今回の同期内容だけを微調整でき、同期元がファイルからテキストへ切り替わったことを誤認しない。

### XLSXの展開処理を追加で制限する

- [x] 展開後合計サイズに加えて、ZIPエントリー数と単一エントリーサイズに上限を設ける
- [x] 必要に応じて圧縮率の異常値を拒否し、大量の小さなエントリーによるCPU・オブジェクト数の負荷を防ぐ
- [ ] 上限内のXLSXをClosedXMLで読み込んだ際の実メモリ使用量を計測し、100MBの展開後サイズ上限が妥当か見直す
- [x] エントリー数、単一エントリーサイズ、圧縮率の上限直前・上限超過テストを追加する
- [ ] 現状の検証はZIPヘッダーの自己申告サイズ・圧縮率のみで、ヘッダー偽装で上限をすり抜けたファイルは`ClosedXML`の実展開時に負荷がかかる（`MemberFileSecurityValidator.cs:74-118`）。DoS対策は低優先度・簡易チェックまでの方針のため、実展開バイト数を都度計測する本格対応はせず、現状のヘッダーチェックのままとする（コードレビューで指摘、2026-08-03。方針確認済み）

完了条件: 圧縮後サイズと展開後合計サイズだけでは検出できない、極端な構造のXLSXを過大なCPU・メモリ消費の前に拒否できる（ヘッダー偽装への完全な対策は対象外）。

### 配布と更新の仕組みを整備する

- [ ] 実行ファイルとインストーラーをコード署名する
- [x] バージョン情報を画面から確認できるようにする
- [ ] 自動更新または更新通知の方式を決定する
- [x] Entra IDのアプリ登録手順と必要権限を運用文書へ記載する
- [x] ログ、設定、結果CSVの保存場所と削除方法を文書化する

### 同期結果表示の大量データ対応と折り返しを改善する

- [x] 失敗一覧の`ScrollViewer`内`ItemsControl`を仮想化対応の一覧へ変更するか、先頭N件と総件数を表示する方式にする
- [x] 数百～数千件の失敗結果でも、結果パネルの表示とスクロールが重くならないことを確認する
- [x] アクションバーの`StatusText`を折り返し表示し、最小ウィンドウ幅や長いエラー文でも同期実行ボタンと重ならないようにする
- [ ] 800px幅、表示倍率200%、長い日本語メッセージでレイアウトをUIハーネスから確認する

完了条件: 大量の失敗結果や長い状態文でもUIが応答し、必要な情報と操作ボタンが欠けずに表示される。

### 非本番テナントで結合テストする

- [ ] テスト専用チームとテストユーザーを用意する
- [ ] 所有者保護、追加、削除、存在しないユーザー、部分失敗を確認する
- [ ] 大規模なメンバー数と多数チームで性能を計測する
- [ ] Graph権限不足や管理者同意がない場合の表示を確認する
- [x] リリース前チェックリストを作成する

## 継続的な確認

- [x] `dotnet test TeamsSync.slnx`を成功させる
- [x] `dotnet list TeamsSync.slnx package --vulnerable --include-transitive`で既知の脆弱性がないことを確認する
- [x] `dotnet list TeamsSync.slnx package --outdated`を確認する
- [ ] 削除対象件数が多い場合の警告と所有者保護をリリースごとに確認する
- [ ] アクセシビリティ、キーボード操作、ハイコントラストモードを確認する (テーマはライト固定)
- [ ] 各ボタンのアクセスキー (日本語文字+アンダースコア)がUS配列キーボードでも機能するか確認する
- [x] 同期差分DataGridがMainScrollViewerにネストされ行仮想化が実質無効だった問題を解消。画面を「手順1〜3 (スクロール)
  」「手順4=同期差分カード (固定領域)」の2段に分離し、DataGridが独自のスクロール範囲を持つようにした。大量データ時の性能は「大規模なメンバー数と多数チームで性能を計測する」で引き続き確認する

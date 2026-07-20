# Prompt World TAC — リリース手順書

このセッションで入れた変更を実際にストア公開／本番反映するための手順。
コード側の準備は済んでいる。ここから先は **人間（あなた）の作業**が中心。

最終更新: 2026-07-20

---

## 0. まず Unity で開く（最初に必ず）

今回の変更は全てソースにあるだけで、まだアプリ／本番に入っていない。

1. Unity Hub から PromptWorld を開く（Unity `6000.5.3f1`）
2. **コンパイルエラーが出ないこと**を確認
   - ※ CLIバッチ（`BuildScript.RebuildScenes`）で検証済み・エラーなし（2026-07-20）
3. `Assets/Editor/AppIcon/icon_1024.png` が Project ビューに見えること
   （見えない＝未インポートなら、一度エディタを開けば取り込まれる）

アイコンは **ビルド時に `AppIconPreBuild` が自動割り当て**するので手動メニューは不要。
手動でやるなら `PromptWorld → Configure App Icon`。

---

## 1. iOS ビルド → App Store 提出

### ビルド（あなたのコマンドで正しい）
```bash
cd ~/develop/promptworld && \
/Applications/Unity/Hub/Editor/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath PromptWorld \
  -buildTarget iOS \
  -executeMethod BuildScript.BuildTacIOS \
  -logFile ~/tac-ios-build.log
```
→ `PromptWorld/Builds/iOS-Tac/` に Xcode プロジェクト。

### 実機確認（提出前に必ず）
- Xcode で開いて実機にRun
- **起動時に「トラッキングを許可しますか？」ダイアログが出る**（今回のATT実装）
- game over で**広告が出る**（テストIDなら確実。実IDは在庫待ちのことあり）
- APC（装甲車）が正面弾を弾き背後で崩れる／ドローン接近で赤い方向矢印が出る

### App Store Connect（ブラウザ）
- 新規App作成（Bundle ID `com.appuppu.promptworldtac`）
- スクショ（6.7" と 6.5" 必須）
- **プライバシーポリシーURL** = `https://<ドメイン>/privacy`（privacy.html、AdMob対応済み）
- **プライバシー「データ収集」申告** → IDFA＋使用状況データを広告目的で収集、と申告（必須）
- 年齢レーティング、「広告を含む」
- Xcode で Archive → Distribute → App Store Connect → TestFlight確認 → 審査提出

---

## 2. Android ビルド → Play 提出

### ① 動作確認用 APK（署名不要・すぐ入る）
```bash
cd ~/develop/promptworld && \
/Applications/Unity/Hub/Editor/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath PromptWorld \
  -buildTarget Android \
  -executeMethod BuildScript.BuildTacAndroid \
  -logFile ~/tac-android-build.log
```
→ `PromptWorld/Builds/Android-Tac/PromptWorldTac.apk`

### ② Play提出用 AAB（リリース署名・keystore必須）
```bash
# 初回だけ: キーストア生成（パスワードは自分で決める・絶対に紛失しない・バックアップ必須）
keytool -genkey -v -keystore ~/promptworld.keystore -alias promptworld \
        -keyalg RSA -keysize 2048 -validity 10000

# ビルド（環境変数で keystore を渡す）
cd ~/develop/promptworld && \
PW_KEYSTORE_PATH="$HOME/promptworld.keystore" \
PW_KEYSTORE_PASS="（ストアパスワード）" \
PW_KEY_ALIAS="promptworld" \
PW_KEY_PASS="（鍵パスワード）" \
/Applications/Unity/Hub/Editor/6000.5.3f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath PromptWorld \
  -buildTarget Android \
  -executeMethod BuildScript.BuildTacAndroidRelease \
  -logFile ~/tac-android-release.log
```
→ `PromptWorld/Builds/Android-Tac/PromptWorldTac.aab`

### Google Play Console（ブラウザ）
- 新規アプリ作成（パッケージ名 `com.appuppu.promptworldtac`）
- ストア掲載情報、スクショ、**アイコン512px**、**フィーチャーグラフィック1024×500**
- **プライバシーポリシーURL**（iOSと同じ）
- **データセーフティ**フォーム → 広告ID＋アプリアクティビティを広告目的で、と申告
- コンテンツレーティング、「広告を含む」
- AAB アップロード → 内部テスト → 製品版レビュー提出

---

## 3. Web 本番反映（プライバシーURLを有効化するため）

App Store / Play のプライバシーURLは `https://<ドメイン>/privacy` を指すので、
先に web をデプロイして privacy.html（AdMob対応済み）と app-ads.txt を配信する。

```bash
cd ~/develop/promptworld && ./scripts/deploy-web.sh
```
デプロイ前に必ず 6点回帰チェック（下記）を確認すること。

---

## 4. 広告テストID の切り替え（任意・切り分け用）

実広告が出ない時、テスト広告なら100%出るので実装の正否を切り分けられる。

- Player Settings → Scripting Define Symbols に `PROMPTWORLD_ADTEST` を追記 → テストID
- 外すと実IDに戻る（本番に戻し忘れない）

---

## 5. ★ UGC 自動公開フロー（MCP作成 → 検証待ち → クリアで自動公開）

**新規実装は不要。既に動く設計になっている。**

- MCP（ADMIN_TOKENS付き）で `create_stage` すると `status:'unverified'` で作成される
- unverified ステージは **web・iOS 両方の「検証待ち／testbench」棚に即配信**され、誰でもプレイ可能
- **最初の“検証済みクリア”で自動的に `published` に昇格**（worker.js `opRecordClear`）
- クリアは **サーバー側で replay を再シミュレーションして検証**（bot・改ざんは通らない）
- **無料**: Cloudflare Worker + D1 上で完結、追加コストなし

つまり普段通り MCP で作れば、この挙動になっている。確認したいときは:
`stage_status` で `status` が `unverified` → 誰かがクリア後 `published` に変わるのを見る。

（一般ユーザーが web の create ページで作った場合は従来通り `draft`。本人がクリア
＋publish が必要。「作成者本人でなく“誰かが”クリアで公開」を効かせたいのは admin/MCP 経路。）

---

## デプロイ前 6点回帰チェック（web デプロイ時に毎回）

1. 既存公開ステージに影響がないか（append-only）
2. 決定論・リプレイ検証が無傷か（tac tests ALL PASS／同一 tacsim.js が client と _worker.js に載る）
3. キャッシュバストが効いているか（新 TAC_HASH の `?v=` が edge で配信されている）
4. 既存データが消えないか（D1 は意図した列だけ／clear・creator・vote 行は保全）
5. ステージ音楽が壊れていないか
6. 2D（/classic）が無傷か

---

## このセッションで入れた変更（未コミット）

- 速度チューニング（歩行/走行/弾/敵、3段階）
- 敵強化（視界拡大・歩速・反応・追跡）
- **新敵 APC（type 7）**: 背面弱点・正面装甲・爆発貫通（JS/C#ミラー＋crosscheck bit一致）
- バグ修正3件: ドローン/スコープ後の連射漏れ（fireGate）、リトライ/開始直後の移動不能（prevB=255）
- 広告: `PROMPTWORLD_ADTEST` でテスト/実ID切替
- **ATT（iOS トラッキング許可）**: AttBridge.mm ＋ AttPrompt.cs ＋ plist注入
- SKAdNetwork plist注入、iOS build番号 0→1
- **アプリアイコン**（ゲーム内キャラ再現、単色背景、全スロット自動割当）
- Android リリース署名＋AAB ビルド（`BuildTacAndroidRelease`）
- ドローン接近の視覚警告（HUD方向矢印、5言語）
- privacy.html を AdMob 対応に更新、app-ads.txt 追加、store-listing.md（日英）

参照: `store-listing.md`（ストア説明文の下書き）

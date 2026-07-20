# Prompt World TAC — コース(ステージ)まわり要件まとめ

設計が積み上がって複雑になったので、コース作成のルール・流れ・状態遷移・
デプロイ・制限を一枚に整理する。実装(`server/worker.js`)の現状に基づく。

最終更新: 2026-07-20

---

## 1. コースの状態(status)と遷移

コースは実質2状態で運用(`stages.status`。`draft` はレガシー):

| status | 意味 | 一覧に出るか | 公開のされ方 |
|---|---|---|---|
| `unverified` | **検証待ち棚**。誰でも遊べるが未検証 | ✓ 検証待ちタブ(+検索) | **誰かの“検証済みクリア”で自動的に `published` へ昇格** |
| `published` | 公開。メイン一覧に出る | ✓ メインタブ | (公開済み) |
| `draft` | (レガシー)非公開の下書き | ✗ | もう生成されない。既存の孤児のみ |

### ★ 全員 unverified(2026-07-20 決定 — draft 廃止)
`opCreateStage`(`worker.js`):
```
initialStatus = 'unverified'   // admin かどうかに関係なく、誰が作っても検証待ち棚へ
```
- **誰が作っても**(MCP admin / 非admin どちらも)即・検証待ち棚に載る = web/app で誰でも遊べる
- **誰かがクリアで自動公開**。作者本人である必要はない。`publish_stage` の明示呼び出しは不要
- 「誰でも作って、誰でも遊んで、クリアされたら公開」という体験を実現

### 自動公開(unverified → published)
`opRecordClear`(`worker.js`): 検証待ちステージを **サーバー側で replay を再シミュレーション
して検証**し、正当なクリアなら status を `published` に更新。ボット/改ざんは replay 検証で弾く。

### flood 安全弁(全員 unverified に伴う対策)
未検証プール(`draft` + `unverified`)は容量上限 `MAX_TOTAL_DRAFTS`(15000)を超えると
**古いものから自動 eviction**(`evictOldestDrafts`)。クリアされたコースは `published` に
昇格してプールを抜けるので **eviction 対象外**。TTL GC は unverified を消さない(良コースは
クリアされるまで棚に残る)。= 体験(棚に残す)と安全(flood 時は古い未クリアを間引く)の両立。

---

## 2. 作成の流れ(MCP 経由)

1. `get_toolbox { game:"tac" }` で仕様を取得
2. `create_stage`(game:"tac", arena JSON)→ **id と editKey と testUrl** が返る
   - admin なら即 `unverified`(検証待ち棚)、そうでなければ `draft`
   - **★ レスポンスに editNote が付く: id と editKey を必ず保存すること**(後述)
3. 人間が testUrl を開いてブラウザ/アプリでプレイ
4. クリアされると:
   - `unverified` は自動で `published` に昇格
   - `draft` は作者が名前を承認 → `publish_stage` で公開
5. 後から直したいときは **同じ id + editKey** で `update_stage`

### ★ editKey メモの注意喚起(実装済み)
`create_stage` のレスポンスに以下を含める(worker.js):
- `instructionsForClaude`: 「人間に id と editKey を保存させろ。後の編集に両方必要、復旧不可」
- `editNote`: 「後で編集するには stage id と editKey が要る。復旧手段はない(アカウント/ログインなし)」

アカウントもパスワードも無い設計なので、**id + editKey を失うと二度と編集できない**。
Claude は作成時に必ず人間へ保存を促すこと。

---

## 3. コースの制限(バリデーション)

`LIMITS`(`worker.js`):

| 項目 | 値 |
|---|---|
| timeLimit(一般範囲) | 5〜1800 秒 |
| **timeLimit(tac 実際)** | **★ 300 秒(5分)に強制固定** — 作成・編集の両方で上書き。作者が何を渡しても5分 |
| パーツ数 | 最大 300 |
| 座標 | ±500 |
| パーツサイズ | 0.05〜100 |
| power(HP等) | 最大 60 |
| period | 0.5〜30 |
| JSON サイズ | 最大 256 KB |
| 敵タイプ | soldier, gatling, sniper, drone, operator, bomber, shield, **apc**(新: 装甲車) |

> **tac の 5分固定**は house rule。`opCreateStage` と `opUpdateStage` の両方で
> `stage.timeLimit = 300` を強制(2D classic は影響なし)。8分等で作れてしまう穴は塞いだ。

### 公開の最低条件
- **人間のブラウザ/アプリでの検証済みクリアが必須**(ボット不可)
- クリアタイムが `MIN_CLEAR_MS_TO_PUBLISH`(3000ms)未満 = 短すぎる → 公開拒否

---

## 4. レート制限・容量(flood 対策)

`RATE_LIMITS`(1日あたり。admin トークンはこれらをバイパス):

| アクション | 上限 |
|---|---|
| create(新規 draft / IP) | 30 |
| create(全体) | 20,000 |
| update(所有コース編集 / IP) | 300 |
| clear 提出 / IP | 200 |
| publish / IP | 10 |
| publish / 作成者 | 5 |
| publish 全体 | 200 |
| vote / IP | 300 |
| score / IP | 300 |

容量上限:
- `MAX_PUBLISHED_STAGES = 5000`(公開コースの上限。**eviction されない**)
- `MAX_TOTAL_DRAFTS = 15000`(draft プールの上限。**古いものから自動削除**=flood安全弁)
- `DRAFT_TTL_DAYS = 7`(未公開 draft は7日で GC)

> **不変条件**: 攻撃者が作れるのは draft だけ(公開は人間クリアが必要)。draft は
> eviction されるので、flood しても公開枠を食えない。← この設計は維持すること。

---

## 5. 検索と通報・非表示(全プラットフォーム: web / iOS / Android)

### 検索
- `GET /api/stages?q=<語>` で **公開・検証待ちの両方**を名前検索(サーバー側 LIKE)
- web(tac-home)・iOS/Android(TacGame)とも検索UIあり

### 通報・非表示(hide)
- `POST /api/stages/:id/hide`(body: playerId)
- **押した本人の一覧からのみ消える**(グローバルには残る=嫌がらせ通報で作者が損しない)
- 通報数は `stages.reports` に集計 → **admin が手動で有害コースを判断・削除**(App Store Guideline 1.2 対応)
- 一覧取得は `?playerId=` で、そのデバイスが hide したコースを除外
- DB: `hides` テーブル(1デバイス1通報)+ `stages.reports` 列

---

## 6. デプロイ(web / 本番反映)

```bash
./scripts/deploy-web.sh
```
- `sim.js + tacsim.js + tacreach.js + worker.js` を連結して `_worker.js` を生成
- `tacsim.js` と `tac-client.js` の md5 から **TAC_HASH** を作り `?v=` でキャッシュバスト
- **D1 マイグレーション**を冪等に流す(`ALTER ... || true` + ハード検証):
  game 列 / survive 列 / **hides テーブル + reports 列**
- 静的ページ(privacy/terms/ads.txt/**app-ads.txt**)も配置

### ★ デプロイ前 6点回帰チェック(必ず)
1. 既存公開ステージに影響なし(append-only)
2. 決定論・リプレイ検証が無傷(**tac tests ALL PASS** / 同一 tacsim.js が client と _worker.js に載る)
3. キャッシュバストが効く(新 TAC_HASH の `?v=` が edge で配信)
4. 既存データが消えない(D1 は意図列のみ / clear・creator・vote 行保全 / URL 不変)
5. ステージ音楽が壊れない
6. 2D(/classic)が無傷

### 決定論の担保(TAC 固有)
- `server/tacsim.js`(JS)と `TacSim.cs`(C# ミラー)は **ビット一致**が必須
- 変更時は `./scripts/tac-crosscheck.sh` で JS↔C# のトレース一致を確認
- `tacsim.js` を変えたら client(web)・_worker.js(サーバー再検証)・C# の3つを同期

---

## 7. アプリ(iOS / Android)配信の要点

- コースは web と同じ `/api/stages` から取得 → **公開・検証待ちの両方が即アプリに反映**
- 検証待ちで誰かがクリア → 自動公開 → 全プラットフォームで遊べる
- App Store 対応: ATT(トラッキング許可)・SKAdNetwork・通報導線(hide)実装済み
- 詳細な公開手順は `RELEASE-GUIDE.md`、ストア文面は `store-listing.md`

---

## 未決/今後の論点
- 通報が一定数を超えた際の admin 通知/自動棚下げ(現状は手動レビュー)
- 2D classic の廃止(ユーザー指示 2026-07-20「消していい」— 影響範囲確認のうえ対応予定)

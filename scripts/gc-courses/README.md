# Abandoned-course GC

放置された **unverified**(検証待ちシェルフ)コースを2段階で自動掃除する仕組み。

## なぜ必要か

MCPで作られたコースは `unverified` として棚に載り、最初のクリアで `published` に
昇格する。誰もクリアせず放置されたコース、あるいはクリアしようがないコースが
棚に溜まり続けるのを防ぐ。

## 判定ロジック(`gcAbandonedUnverified` / `server/worker.js`)

**最終アクティビティ** = `MAX(created_at, plays内のMAX(updated_at))`
= 「作成 または 最後に誰かがトライした時刻」の新しい方。

- **論理削除(soft)**: 最終アクティビティから **14日**(`UNVERIFIED_SOFT_TTL_DAYS`)
  無活動 かつ `clears = 0`(まだ誰もクリアしていない unverified)→ `hidden_at` を
  スタンプし、testbench シェルフから外す(DBには残る)。
- **物理削除(hard)**: 論理削除された(`hidden_at`)状態が **さらに14日**
  (`UNVERIFIED_HARD_TTL_DAYS`)続き、依然 `clears = 0` かつ新しい活動なし
  → 子テーブル(votes/hides/scores/plays)ごと物理 DELETE。

**復活条件**: 物理削除の前なら、誰かがトライ/クリア、または作成者が `update_stage`
で編集すれば棚に戻る(クリアで published 昇格、編集/クリアで `hidden_at` クリア)。
published は対象外(=一度でもクリアされたコースは絶対に消えない)。

## 実行(Cloudflare Pages に cron がないので launchd で叩く)

worker に秘密トークンで保護した `POST /api/gc` があり、これを毎日叩く。

### 初回セットアップ

1. GCトークンを生成して Cloudflare の secret に設定:
   ```sh
   TOKEN=$(uuidgen)
   echo "$TOKEN"                 # 控えておく
   npx wrangler pages secret put GC_TOKEN --project-name promptworld   # プロンプトに $TOKEN を貼る
   ```
2. ローカルにも同じ値を(git外の)環境ファイルで置く:
   ```sh
   echo 'export PW_GC_TOKEN="<上のTOKEN>"' > ~/.pw_gc_env
   chmod 600 ~/.pw_gc_env
   ```
3. launchd エージェントを登録:
   ```sh
   cp scripts/gc-courses/com.promptworld.gccourses.plist ~/Library/LaunchAgents/
   launchctl load ~/Library/LaunchAgents/com.promptworld.gccourses.plist
   ```

### 手動実行・確認

```sh
zsh scripts/gc-courses/run.sh
# => {"ok":true,"softHidden":N,"hardDeleted":M}
```

ログは `scripts/gc-courses/cron.log`。

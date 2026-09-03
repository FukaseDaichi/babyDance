---
name: update-learnings
description: セッション終盤、または学びの更新を求められたときに、再利用価値のある新しい洞察を LEARNINGS.md へ追記する。
---

このセッションの作業を振り返り、新しい学びを洗い出せ:
効いた型 / 失敗と再発防止 / 業務・仕様の知識 / 覚えておく価値のある解法 / 未解決の疑問。
LEARNINGS.md の該当セクションに「- YYYY-MM-DD: 洞察」の形式で、1項目1洞察で追記せよ。
日付は実行環境のシステムコマンドで取得し、推測するな。
Consolidated Principles には書き込むな（統合パス専用）。
学びが無ければ何も書かず「学びなし」と報告せよ。
追記後、生の観察（Consolidated Principles を除く上4セクションの項目）の合計を
次のコマンドで数え、80件を超えていたら続けて consolidate-learnings スキルを実行せよ。

```bash
awk '/^## Patterns That Work/{on=1} /^## Consolidated Principles/{on=0} on && /^- /{c++} END{print c+0}' LEARNINGS.md
```

`grep -c '^- '` で数えるな。記法ルールのコメント行と Consolidated Principles の
項目を巻き込んで過大カウントする。

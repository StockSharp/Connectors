# Coinstore コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Coinstore コネクター**は、StockSharp を Coinstore の暗号資産スポット市場へ接続します。幅広い上場銘柄の監視や、暗号資産とステーブルコインの取引自動化に適しています。

## 主な機能

- 取引状態、価格・数量精度、最小注文条件を含むスポット銘柄情報。
- Level 1、Level 2 板情報、公開約定、OHLCV ローソク足。
- WebSocket によるティッカー、板、約定、ローソク足のリアルタイム配信。
- REST による直近約定、板スナップショット、ローソク足履歴。
- 残高、未約定注文、注文状態、自己約定。
- 成行、指値、Post-only、IOC 注文と個別・一括キャンセル。
- REST と WebSocket の接続先を設定可能。

公開市場データに認証情報は不要です。口座情報と取引には Coinstore の API キーとシークレットが必要です。非公開状態は認証済み REST リクエストで更新されます。

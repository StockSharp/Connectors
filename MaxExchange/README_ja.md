# MAX Exchange コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**MAX Exchange コネクター**は、StockSharp を MaiCoin Group が運営する台湾の現物取引所へ接続します。TWD と USDT 建ての暗号資産市場に特に適しています。

## 主な機能

- 取引状態、精度、最小注文数量を含む現物銘柄の取得。
- Level 1、Level 2 板情報、公開約定、OHLCV ローソク足。
- WebSocket によるティッカー、板、約定、ローソク足のリアルタイム配信。
- REST API v3 による過去ローソク足と最新市場スナップショット。
- 残高、未約定・過去注文、プライベート約定。
- Market、Limit、Stop Market、Stop Limit、Post-only、IOC Limit 注文。
- 個別・一括取消と、変更可能な REST／WebSocket アドレス。

## 利用例

自動売買、取引端末、TWD 市場データ収集、監視、注文管理システムに利用できます。

公開市場データに認証情報は不要です。口座情報と取引には MAX Exchange の API キーとシークレットが必要です。

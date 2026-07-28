# AltCoinTrader コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**AltCoinTrader コネクター**は、StockSharp を南アフリカの AltCoinTrader スポット市場に接続します。ZAR 建ての板情報を利用できるため、現地価格の把握、市場監視、データ収集、暗号資産の自動売買に適しています。

## 主な機能

- 取引状態、価格と数量の精度、最小注文金額を含むスポット銘柄情報。
- Level 1 気配、Level 2 板情報、公開約定。
- 公開 WebSocket によるティッカー、板情報、約定のリアルタイム配信。
- REST による市場スナップショットと直近の公開約定。
- 認証済み WebSocket による残高、未約定・過去注文、自己約定、口座更新。
- GTC、IOC、FOK の指値注文、成行注文、個別取消、条件付き一括取消。
- REST と WebSocket の接続先を設定可能。

公開市場データに認証情報は不要です。ポートフォリオと取引機能には、適切な権限を持つ AltCoinTrader の API キーとシークレットが必要です。

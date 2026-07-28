# BigONE コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**BigONE コネクター**は、StockSharp を BigONE の現物市場とコントラクト市場に接続します。1 つのアダプターで通常の暗号資産ペアと、コインまたは USDT 証拠金の無期限契約を扱えます。

## 主な機能

- 現物ペアと利用可能な無期限契約の取得。
- Level 1、板情報、公開約定、OHLCV ローソク足。
- JSON WebSocket による現物配信と、専用 URL による契約配信。
- 現物ローソク足履歴と両市場の最新 REST スナップショット。
- 現物・契約残高、契約ポジション、注文、自己約定履歴。
- 成行、指値、IOC、FOK、post-only、現物ストップ、契約 reduce-only 注文。
- 個別注文と注文グループの取消。
- 現物・契約 REST、公開・非公開 WebSocket アドレスの設定。

## 利用例

BigONE の現物流動性とデリバティブを組み合わせる売買ロボット、端末、データ収集、監視、注文管理に利用できます。

公開市場データに認証情報は不要です。口座照会と取引には BigONE の API キーとシークレットが必要です。

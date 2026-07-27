# BitoPro コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**BitoPro コネクター**は、StockSharp を台湾市場向けの規制対象暗号資産取引所 BitoPro に接続し、活発な TWD 現物市場を利用できるようにします。

## 主な機能

- 現物銘柄、価格・数量精度、取引制限の取得。
- Level 1、市場深度スナップショット、公開約定データ。
- WebSocket によるティッカー、板、約定のリアルタイム配信。
- BitoPro が提供する全時間枠の過去 OHLCV ローソク足。
- 残高、未約定・過去注文、ユーザー約定履歴。
- 指値、成行、ストップ指値、Post-only 注文と個別・一括取消。
- REST と WebSocket の接続先設定。

## 利用例

売買ロボット、取引端末、TWD 市場データ収集、監視、注文管理システムに利用できます。

公開市場データに認証情報は不要です。口座・取引機能にはメールアドレス、API キー、シークレットが必要です。BitoPro の成行買いはクオート通貨額を受け取るため、コネクターは StockSharp のベース数量を最新公開価格で換算します。

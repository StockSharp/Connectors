# CoinTR コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**CoinTR コネクター**は、StockSharp をトルコ市場に重点を置く暗号資産取引所 CoinTR に接続します。CoinTR の現物銘柄を StockSharp の標準メッセージモデルから利用できます。

## 主な機能

- 現物銘柄、価格・数量精度、取引制限の取得。
- Level 1 クオート、Level 2 板スナップショット、公開約定。
- WebSocket によるティッカー、板、約定、ローソク足のリアルタイム配信。
- CoinTR が対応する時間足の過去 OHLCV データ。
- ポートフォリオ残高、未約定注文、非公開の約定通知。
- 成行、指値、トリガー注文の発注と注文取消。
- REST、公開 WebSocket、非公開 WebSocket の接続先設定。

## 利用例

CoinTR の現物市場を扱う売買ロボット、取引端末、マーケットデータ収集、監視、注文管理サービスで利用できます。

公開マーケットデータに認証情報は不要です。取引と口座操作には、適切な権限を持つ API キー、シークレット、パスフレーズが必要です。成行買いでは CoinTR は数量をクオート通貨の金額として扱い、指値注文と成行売りではベース資産の数量として扱います。

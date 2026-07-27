# BtcTurk コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**BtcTurk コネクター**は、StockSharp をトルコの暗号資産現物取引所 BtcTurk Kripto に接続します。StockSharp の標準メッセージモデルを通じて、TRY、BTC、USDT などの市場を扱う取引システムや市場データ収集システムに適しています。

## 主な機能

- 現物銘柄と価格、数量、注文制限の取得。
- Level 1 気配、Level 2 板スナップショット、公開約定。
- WebSocket によるティッカー、板、約定のリアルタイム配信。
- BtcTurk が対応する時間足の OHLCV 履歴。
- ポートフォリオ残高、未約定・過去注文、口座約定。
- 成行、指値、ストップ成行、ストップ指値注文。
- 個別注文および注文グループの取消。
- REST、履歴データ、WebSocket エンドポイントの設定。

## 用途

BtcTurk Kripto の現物市場を利用する取引ロボット、ターミナル、データ収集、注文管理、監視システムで使用できます。

公開市場データには認証情報は不要です。取引と口座操作には、適切な権限を持つ BtcTurk API キーと Base64 形式のシークレットが必要です。成行買いでは数量は決済通貨の金額として扱われ、それ以外の注文では基準資産の数量として扱われます。

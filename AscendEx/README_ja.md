# AscendEX コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**AscendEX コネクター**は、公開された AscendEX Pro API を StockSharp に統合します。1 つのアダプターで現物 cash、証拠金、無期限先物市場を扱い、複数市場を使う暗号資産戦略や、公開仕様に基づく取引所プロトコルの実装保存に利用できます。

## 主な機能

- 取引状態、価格刻み、数量刻み、注文制限を含む現物・証拠金・無期限先物銘柄の取得。
- Level 1、Level 2 板、約定、OHLCV ローソク足。
- REST によるスナップショットと履歴、および現物・先物別のリアルタイム WebSocket。
- Cash/証拠金残高、先物担保とポジション、未約定・履歴注文、約定。
- GTC、IOC、FOK、post-only、先物 reduce-only に対応する market、limit、stop-market、stop-limit 注文。
- 個別および一括の注文取消。
- REST、現物 WebSocket、先物 WebSocket、アカウントグループ、cash/margin モードの設定。

公開市場データには認証情報は不要です。ポートフォリオと取引には API キー、シークレット、および AscendEX が割り当てたアカウントグループが必要です。

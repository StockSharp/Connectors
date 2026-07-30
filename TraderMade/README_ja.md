# TraderMade コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**TraderMade コネクター**は、StockSharp を TraderMade の外国為替・暗号資産市場データへ接続します。REST 履歴と WebSocket クォートを統一された StockSharp 市場データモデルへ変換します。

## 主な機能

- プロバイダーの通貨一覧と設定した決済通貨からペアを生成、または明示的なシンボル一覧を使用。
- ストリーミング API から Level 1 の Bid、Ask、中間値をリアルタイム取得。
- 口座権限があり機能を有効にした場合、TraderLadder 板情報を取得。
- REST から期間別の履歴ローソク足を取得し、任意で週末の暗号資産データも含める。
- REST とストリーミングの別々のキーにより、履歴のみ、ライブのみ、併用を選択可能。
- ローソク足購読は有限の履歴要求で、ライブ更新とティック取引には非対応。
- 市場データ専用であり、ポートフォリオ、残高、注文操作は提供しない。
- TraderMade のシンボル、通信、応答形式は標準 StockSharp API の背後に隠蔽。

## 主な用途

ブローカー執行を必要としない FX・暗号資産ダッシュボード、ライブ価格監視、チャート、分析、履歴バックテストに利用できます。

ペア、TraderLadder 深度、期間、履歴、制限、週末データ、ストリーミング権限は TraderMade と API プランによって決まります。

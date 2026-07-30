# 0x コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**0x コネクター**は、StockSharp を 0x Swap API v2 と対応 EVM ネットワークへ接続します。設定したトークンペアを銘柄として表し、実行可能価格、ウォレット残高、ルーティングされたスワップを StockSharp メッセージへ変換します。

## 主な機能

- Ethereum、Optimism、BNB Chain、Polygon、Base、Arbitrum、Avalanche、Linea で設定したトークンペアを検出。
- 実行可能な 0x 価格をポーリングし、Level 1 の売買価格を算出。
- 選択ネットワークの JSON-RPC を通じて即時成行スワップの見積もりを取得し、署名、送信。
- 任意の自動 Allowance 承認と、スリッページ、試算数量、レシート待機時間の設定。
- ウォレットのトークン残高と、レシート、注文状態、約定を追跡。
- 0x Dashboard API キー、ウォレット、トークンペア、API／RPC エンドポイントを設定可能。
- ティック取引、板、ローソク足、履歴、待機注文、変更、取消は提供しない。
- 0x ルート、単位、承認、署名、EVM レシートは標準 StockSharp API の背後に隠蔽。

## 主な用途

対応 EVM ネットワークでの実行可能トークン価格監視、ウォレットダッシュボード、0x 経由の直接スワップに利用できます。

ペア、ルート、流動性、価格影響、Gas、承認、確定性、制限は 0x、選択ネットワーク、RPC プロバイダーに依存します。

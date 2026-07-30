# Velora コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Velora コネクター**は、StockSharp を Velora Market API と対応 EVM ネットワークへ接続します。設定したトークンペアを銘柄として表し、実行可能価格、ウォレット残高、ルーティングされたスワップを StockSharp メッセージへ変換します。

## 主な機能

- Ethereum、Optimism、BNB Chain、Gnosis、Polygon、Base、Arbitrum、Avalanche で設定したトークンペアを検出。
- 実行可能な Velora ルートをポーリングし、Level 1 の売買価格を算出。
- 選択ネットワークの JSON-RPC を通じて即時成行スワップを構築、署名、送信。
- 任意の自動トークン承認と、スリッページ、試算数量、レシート待機時間の設定。
- ウォレットのトークン残高と、レシート、注文状態、約定を追跡。
- Velora Partner ID、ウォレット、トークンペア、API／RPC エンドポイントを設定可能。
- ティック取引、板、ローソク足、履歴、待機注文、変更、取消は提供しない。
- Velora ルート、単位、承認、署名、EVM レシートは標準 StockSharp API の背後に隠蔽。

## 主な用途

対応 EVM ネットワークでのトークン間価格監視、ウォレットダッシュボード、Velora 経由の直接スワップに利用できます。

ペア、ルート、流動性、価格影響、Gas、承認、確定性、制限は Velora、選択ネットワーク、RPC プロバイダーに依存します。

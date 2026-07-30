# Velodrome コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Velodrome コネクター**は、StockSharp を Optimism 上の Velodrome クラシックプールと Slipstream プールへ接続します。設定プール、実行可能価格、オンチェーンスワップ、ウォレット残高、送信済み取引を StockSharp メッセージへ変換します。

## 主な機能

- 設定したクラシックプールと集中流動性プールをトークン情報とともに検出。
- 実行可能なプール試算から Level 1 の売買価格を生成し、WebSocket とポーリングのフォールバックで更新。
- オンチェーンのスワップログから履歴・ライブのティック取引を取得し、期間別ローソク足を構築。
- 任意の EVM 秘密鍵で署名する即時成行スワップ。Allowance 処理と設定可能なスリッページに対応。
- ウォレットのトークン残高、取引レシート、注文・約定状態を更新。
- 履歴収集は設定した Optimism のブロック範囲と件数に制限。
- 中央集権型の板、待機する指値注文、原子的な変更、取消は提供しない。
- RPC、トークン単位、プール種別、署名、ログは標準 StockSharp API の背後に隠蔽。

## 主な用途

Optimism DEX の監視、Velodrome プール分析、イベント型バックテスト、ウォレット追跡、直接スワップに利用できます。

プール範囲、価格、流動性、RPC 履歴、Gas、確定性、可用性は Velodrome、Optimism、RPC サービスに依存します。

# Zonda Crypto コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Zonda Crypto コネクター**は、StockSharp を中央集権型暗号資産現物取引所 zondacrypto へ接続します。取引所の REST／WebSocket データと口座操作を統一された StockSharp メッセージモデルへ変換します。

## 主な機能

- 通貨、価格・数量ステップ、最低金額を含む現物市場を検索。
- 公開ストリームから Level 1、ティック取引、板スナップショットと更新をリアルタイム取得。
- ライブ継続前に REST スナップショットと利用可能な直近取引履歴を取得。ローソク足には非対応。
- 対応する GTC、IOC、FOK、Post-only を使った成行・指値注文。
- 個別または条件付き一括取消と注文・約定状態の更新。原子的な変更には非対応。
- 非公開ストリームと定期 REST 照合によるウォレット残高とポートフォリオ更新。
- 非公開操作には API キーと Secret が必要。公開データには取引認証情報は不要。
- 認証、市場コード、通信、フィルター、形式は標準 StockSharp API の背後に隠蔽。

## 主な用途

zondacrypto 現物端末、ライブ戦略、直近取引分析、口座監視、注文管理に利用できます。

市場、直近履歴の深度、取引権限、注文オプション、制限、可用性は zondacrypto と接続口座によって決まります。

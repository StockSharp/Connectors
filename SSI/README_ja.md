# SSI コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**SSI コネクター**は、SSI FastConnect API v3 を通じて StockSharp をベトナム証券市場へ接続します。SSI の市場データと証券取引操作を統一された StockSharp メッセージモデルへ変換します。

## 主な機能

- HOSE、HNX、UPCOM の証券と指数を検索し、株式と対応する先物を取得。
- 利用可能な場合は最初に REST スナップショットを取得し、Level 1、ティック取引、板情報をリアルタイム購読。
- 対応期間の履歴ローソク足を取得し、その後ストリーミング更新を継続。
- SSI 固有の注文条件を含む個別注文の送信、変更、取消。
- 口座検索と、ストリーミングおよび定期照合による残高、ポジション、注文、約定の更新。
- REST／WebSocket エンドポイントとポートフォリオのポーリング間隔を設定可能。
- FastConnect 認証情報が必須で、取引には Client ID、口座、RSA 秘密鍵、現在の OTP も必要。
- SSI 固有のセッション、形式、ストリームトピックは標準 StockSharp API の背後に隠蔽。

## 主な用途

SSI 証券サービスへ直接接続するベトナム市場向け端末、ライブ戦略、注文管理、監視ツールに利用できます。

銘柄、履歴深度、取引権限、制限、サービス状況は SSI と接続した FastConnect 口座の権限によって決まります。
